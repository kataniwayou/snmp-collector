# Project Research Summary

**Project:** SnmpCollector v1.4 -- E2E System Verification
**Domain:** End-to-end test infrastructure for K8s-based SNMP monitoring pipeline
**Researched:** 2026-03-09
**Confidence:** HIGH

## Executive Summary

The SnmpCollector v1.4 milestone is a verification-only effort: build an E2E test harness that proves the full SNMP-to-Prometheus pipeline works correctly under normal operation, configuration mutations, and edge cases. The existing system (C# .NET 9, OTel, MediatR, Quartz.NET, 3-replica K8s deployment with leader election) is not modified. The test infrastructure is purely additive -- a bash/Python harness that exercises the system through its existing interfaces (SNMP UDP, ConfigMap K8s API, Prometheus HTTP API) and reports pass/fail results.

The recommended approach is a bash test runner (`run-e2e.sh`) orchestrating 42 test scenarios across 8 categories, with a dedicated Python SNMP test simulator (`e2e-sim`) for edge cases that existing OBP/NPB simulators cannot cover. The test runner queries Prometheus directly via HTTP API and parses kubectl logs for evidence. ConfigMap manipulation follows an extract-merge-apply pattern with snapshot/restore safety. No new heavyweight dependencies -- just `curl`, `jq`, `kubectl`, and a pysnmp-based simulator matching the existing simulator stack.

The dominant risk is timing: the OTel push pipeline introduces a 15-25 second latency between metric recording and Prometheus queryability, and Prometheus metric staleness persists for 5 minutes after a series stops being written. These two characteristics mean tests must use poll-until-satisfied patterns (never fixed sleeps) and verify metric removal via label changes or counter stagnation (never via absence queries). Leader election adds a third timing dimension -- business metrics only export from the leader pod, so tests must verify leadership state before asserting on `snmp_gauge`/`snmp_info`. All three timing pitfalls are well-understood and have documented mitigations.

## Key Findings

### Recommended Stack

The E2E stack aligns with the existing Python simulator ecosystem to minimize cognitive overhead. No C# code changes or new heavyweight dependencies.

**Core technologies:**
- **pytest 9.0.2 + requests + tenacity:** Test runner, HTTP queries, retry-with-backoff for Prometheus polling. Python is the right glue language for subprocess (kubectl) and HTTP (Prometheus API) orchestration
- **pysnmp 7.1.22:** Dedicated test simulator matching existing OBP/NPB simulator patterns exactly. Same imports, same engine setup, deterministic behavior
- **kubectl CLI (subprocess):** Four operations needed (logs, get, patch, rollout). CLI calls are simpler than pulling in the kubernetes Python client for this narrow use case
- **bash (run-e2e.sh):** Orchestration script extending the existing `verify-e2e.sh` pattern. Dependencies: `curl`, `jq`, `kubectl` -- already available

**Explicitly rejected:** kubernetes Python client (overkill), pytest-xdist (shared state prevents parallelism), testcontainers (system already deployed on K8s), Selenium/Playwright (no browser UI), pytest-asyncio (no async code needed).

### Expected Features

**Must have (28 test scenarios -- Categories 1-4):**
- Pipeline counter verification (10 counters, all incrementing with correct `device_name` labels) -- TC-01 through TC-10
- Business metric correctness (snmp_gauge/snmp_info with correct snmp_type, source, metric_name labels) -- TC-11 through TC-17
- Unknown OID handling (unmapped OIDs resolve to metric_name="Unknown", still flow through pipeline) -- TC-18 through TC-21
- OID map hot-reload (rename/remove/add OID mappings via ConfigMap, verify metric_name changes) -- TC-22 through TC-26

**Should have (8 test scenarios -- Categories 5-6):**
- Device lifecycle (add/remove devices via ConfigMap, verify poll job creation/removal) -- TC-27 through TC-31
- ConfigMap watcher resilience (invalid JSON, missing keys, null values -- system retains previous config) -- TC-32 through TC-36

**Defer:**
- Community string auth tests (TC-37 through TC-39) -- already well-covered by unit tests
- Leader election gating tests (TC-40 through TC-42) -- TC-42 (failover) is too disruptive for automated E2E; document as manual verification
- Performance/load testing, chaos testing, Grafana dashboard rendering -- separate disciplines, not E2E functional verification

### Architecture Approach

The test runner lives on the host machine (not in-cluster), matching the existing `verify-e2e.sh` pattern. It interacts with 4 integration surfaces: SNMP UDP (simulator <-> collector), ConfigMap K8s API (test runner -> watchers), Prometheus HTTP API (verification queries), and pod logs (evidence collection). A dedicated test simulator (`e2e-sim`) uses enterprise OID subtree `47477.999` to isolate from production OBP/NPB OIDs, with community string `Simetra.E2E-SIM`.

**Major components:**
1. **Test Simulator (e2e-sim)** -- Deterministic SNMP agent serving mapped + unmapped OIDs, sending traps on 10s intervals, controllable lifecycle via `kubectl scale`
2. **Test Runner (run-e2e.sh)** -- Sequential scenario orchestration with ConfigMap snapshot/restore, Prometheus polling, log evidence collection, and plain-text report output
3. **ConfigMap Fixtures (tests/e2e/fixtures/)** -- OID map entries and device config for E2E-SIM, merged into existing ConfigMaps via extract-jq-apply pattern
4. **K8s Manifest (e2e-simulator.yaml)** -- Deployment + Service following exact same pattern as existing OBP/NPB simulator manifests

### Critical Pitfalls

1. **OTel 15-second export blind spot** -- Metrics take 15-25s to reach Prometheus. Use poll-until-satisfied with 30s timeout and 3s interval, never fixed sleeps. Build the polling utility as the first deliverable.

2. **Prometheus 5-minute staleness window** -- Removed metrics persist for 5 minutes. Never verify removal by checking for absence. Instead verify via metric_name="Unknown" label change (OID removal) or counter stagnation (device removal).

3. **Leader election gaps** -- Business metrics (snmp_gauge/snmp_info) only export from the leader pod. Always verify leadership state before asserting on business metrics. Pipeline counters export from all instances and are safe to query without leader checks.

4. **Test isolation via cumulative counters** -- OTel uses cumulative temporality; counters never reset. Never assert absolute counter values. Record before/after values and assert on deltas. Filter all counter queries by `device_name` to exclude heartbeat noise.

5. **ConfigMap propagation timing** -- K8s API watch events take 1-3s to reach all replicas (up to 10s edge case). Wait for reload log evidence from all pods before querying Prometheus.

## Implications for Roadmap

Based on research, the build follows a strict dependency chain. Each phase produces a standalone deliverable that can be validated before the next phase begins.

### Phase 1: Test Simulator

**Rationale:** Everything depends on having a controllable SNMP device. The simulator can be validated standalone before any test infrastructure exists.
**Delivers:** `simulators/e2e/e2e_simulator.py`, Dockerfile, K8s manifest, OID map fixture entries
**Addresses:** Test simulator requirements from FEATURES.md (9 of 42 scenarios require it)
**Avoids:** Anti-Pattern 2 (modifying existing simulators) -- uses dedicated `.999` OID space

### Phase 2: Test Harness Framework + Pipeline Counter Verification

**Rationale:** Establishes the test framework (polling utility, log capture, report format) using only existing OBP/NPB simulators -- no test simulator dependency. Pipeline counters are the simplest verification target and prove the framework works.
**Delivers:** `tests/e2e/run-e2e.sh` with pre-flight checks, Prometheus query utilities, Phase 1 scenarios (TC-01 through TC-10)
**Addresses:** FEATURES Category 1 (pipeline counters), 10 test scenarios
**Avoids:** Pitfall 1 (fixed sleeps), Pitfall 4 (absolute counter values), Pitfall 9 (heartbeat contamination)

### Phase 3: Test Simulator Integration + Business Metric Verification

**Rationale:** First phase that deploys the test simulator and exercises ConfigMap manipulation (add E2E-SIM device + OID entries). The ConfigMap snapshot/restore pattern built here is reused by all subsequent mutation phases.
**Delivers:** ConfigMap fixture files, merge/restore logic, Phase 2 scenarios (TC-11 through TC-21)
**Addresses:** FEATURES Categories 2-3 (business metric correctness, unknown OID handling), 11 test scenarios
**Avoids:** Pitfall 3 (leader gaps -- verify leadership first), Pitfall 5 (ConfigMap propagation -- wait for reload logs)

### Phase 4: OID Map Mutation + Device Lifecycle Tests

**Rationale:** Depends on Phase 3's ConfigMap infrastructure being proven stable. These are the most operationally valuable tests -- they verify the system handles configuration changes correctly at runtime.
**Delivers:** Phases 3-5 scenarios (TC-22 through TC-31)
**Addresses:** FEATURES Categories 4-5 (OID map hot-reload, device lifecycle), 10 test scenarios
**Avoids:** Pitfall 2 (staleness -- verify via label changes not absence), Pitfall 5 (propagation -- per-pod log verification)

### Phase 5: Watcher Resilience + Trap Verification + Report

**Rationale:** These are the remaining scenarios that round out coverage. Watcher resilience tests (invalid JSON, missing keys) are defensive; trap verification exercises the UDP path. Final report generation wraps the suite.
**Delivers:** Phases 6-8 scenarios (TC-32 through TC-39), comprehensive report output
**Addresses:** FEATURES Categories 6-7 (ConfigMap resilience, community string auth), 8 test scenarios
**Avoids:** Pitfall 7 (UDP unreliability -- retry trap sends), Anti-Pattern 5 (not snapshotting ConfigMaps)

### Phase Ordering Rationale

- **Simulator first** because 9 test scenarios require it and the framework needs a controllable SNMP device for anything beyond pipeline counter verification
- **Framework + pipeline counters second** because they establish the polling, log capture, and reporting patterns that every subsequent phase reuses, and they can run without the test simulator
- **Business metrics third** because they exercise the full data path (SNMP -> MediatR -> OTel -> Prometheus) and introduce ConfigMap manipulation patterns
- **Mutations fourth** because they depend on proven ConfigMap infrastructure and are the highest-value operational tests
- **Resilience + traps last** because they are defensive edge cases with lower business impact -- valuable but not blocking

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 4 (Device Lifecycle):** The DynamicPollScheduler reconciliation timing is complex. May need to verify exact Quartz job teardown behavior and liveness vector cleanup during planning.

Phases with standard patterns (skip research-phase):
- **Phase 1 (Test Simulator):** Existing OBP/NPB simulators provide a complete reference implementation. Copy the pattern.
- **Phase 2 (Framework + Counters):** Well-established pattern from existing `verify-e2e.sh`. Extend, don't reinvent.
- **Phase 3 (Business Metrics):** Prometheus HTTP API queries are straightforward. Leader verification is the only nuance (documented in Pitfall 3).
- **Phase 5 (Resilience + Traps):** ConfigMap error handling is simple code paths (3-line guards). Trap retry is straightforward.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All versions verified against PyPI. Stack aligns with existing simulator ecosystem. No novel dependencies. |
| Features | HIGH | All 42 test scenarios derived directly from codebase analysis of shipped features. Every scenario maps to a specific code path. |
| Architecture | HIGH | Architecture mirrors existing `verify-e2e.sh` pattern and simulator deployment patterns. No architectural novelty. |
| Pitfalls | HIGH | Critical pitfalls verified against OTel SDK source, Prometheus remote write spec, and K8s API watch documentation. Timing constants extracted from actual source code. |

**Overall confidence:** HIGH

### Gaps to Address

- **Leader election failover test (TC-42):** Intentionally deferred as manual-only. Pod kill is too disruptive for automated E2E. Run once, record evidence, document results.
- **K8s watch reconnection (TC-36):** Hard to trigger deterministically. Watch connections close naturally every ~30 minutes. Verify via log observation during long-running operation, not as an automated test.
- **OTel export interval coupling:** The 15-second export interval drives the 30-second test timeout budget. If this changes in a future release, test timeouts must be adjusted accordingly.
- **pytest vs bash decision:** STACK.md recommends pytest; ARCHITECTURE.md describes a bash runner extending `verify-e2e.sh`. Both are viable. Recommendation: **start with bash** to stay consistent with the existing `verify-e2e.sh` pattern and avoid introducing a Python test dependency for the runner. Migrate to pytest only if the suite exceeds ~500 lines or needs parameterized test cases.

## Sources

### Primary (HIGH confidence)
- Codebase analysis: `PipelineMetricService.cs`, `OtelMetricHandler.cs`, `OidMapService.cs`, `OidMapWatcherService.cs`, `DeviceWatcherService.cs`, `MetricRoleGatedExporter.cs`, `SnmpTrapListenerService.cs`, `DeviceUnreachabilityTracker.cs`, `ServiceCollectionExtensions.cs`
- Codebase analysis: `simulators/obp/obp_simulator.py`, `simulators/npb/` -- simulator architecture patterns
- Codebase analysis: `deploy/k8s/verify-e2e.sh` -- existing E2E verification pattern
- [Prometheus Remote Write Spec](https://prometheus.io/docs/specs/prw/remote_write_spec/) -- staleness marker behavior
- [Prometheus HTTP API](https://prometheus.io/docs/prometheus/latest/querying/api/) -- query endpoints
- [OTel Collector Issue #27893](https://github.com/open-telemetry/opentelemetry-collector-contrib/issues/27893) -- remote write staleness gap

### Secondary (MEDIUM confidence)
- [K8s Issue #30189](https://github.com/kubernetes/kubernetes/issues/30189) -- ConfigMap watch propagation delays
- PyPI version verification: pytest 9.0.2, requests 2.32.5, tenacity 9.1.4, pysnmp 7.1.22

---
*Research completed: 2026-03-09*
*Ready for roadmap: yes*
