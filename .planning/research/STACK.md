# Technology Stack: E2E System Verification

**Project:** SnmpCollector E2E Testing
**Researched:** 2026-03-09
**Confidence:** HIGH (versions verified against PyPI and official documentation)

---

## Context

The SnmpCollector application stack is already established and validated (C# .NET 9, SharpSnmpLib, OTel, MediatR, Quartz.NET, KubernetesClient). This document covers ONLY the additions needed for E2E system verification -- the test harness that validates the full pipeline from SNMP simulator through to Prometheus metrics.

Existing simulators (OBP, NPB) use Python 3.12 + pysnmp 7.1.22. The E2E test stack aligns with these to minimize cognitive overhead and share SNMP patterns.

---

## Recommended E2E Stack

### Test Runner

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| pytest | 9.0.2 | Test orchestration, assertions, fixtures, reporting | Industry standard Python test runner. Superior to bash scripts: structured assertions, fixture-based setup/teardown, parallel execution via plugins, JUnit XML output for CI. Already the natural choice given existing Python simulator ecosystem. |

**Why not bash scripts:** Bash works for a single happy-path smoke test. E2E suites need: parameterized test cases, structured assertions with meaningful diff output, fixture-scoped setup/teardown (deploy simulator -> run tests -> cleanup), timeout handling, and CI-friendly reporting. pytest provides all of this. A bash script that grows beyond 100 lines becomes unmaintainable.

**Why not a C# xUnit E2E project:** The tests interact with external systems (kubectl, Prometheus HTTP API, SNMP simulators) via subprocess calls and HTTP requests. Python is a better glue language for this. The existing C# test project (`tests/SnmpCollector.Tests/`) handles unit/integration tests; E2E lives in a separate Python harness.

### HTTP & Verification

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| requests | 2.32.5 | Prometheus HTTP API queries | Standard Python HTTP client. No async needed -- E2E tests are sequential by nature (send SNMP, wait, query Prometheus). Zero learning curve. |

**Why not httpx or aiohttp:** The E2E tests are inherently sequential -- send a trap, wait for pipeline processing, query Prometheus. Async adds complexity with zero benefit. `requests` is the simplest correct choice.

### SNMP Test Simulator

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| pysnmp | 7.1.22 | Dedicated E2E test simulator | Matches existing OBP/NPB simulators exactly. Same import patterns, same SNMP engine setup. Enables edge-case scenarios (counter wraps, rapid traps, invalid OIDs) that production simulators cannot expose without contaminating their purpose. |

**Why a dedicated test simulator instead of reusing OBP/NPB:** The production simulators (OBP, NPB) model real device behavior -- they random-walk power values and send traps on realistic intervals. E2E tests need deterministic, controllable behavior: "set this OID to exactly this value, send exactly one trap now, return a counter that wraps at exactly 2^32-1." A test simulator is purpose-built for verifiability, not realism.

### K8s Interaction

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| kubectl (CLI) | (cluster-matched) | Log parsing, ConfigMap patching, pod management | Already available in the deployment environment. Subprocess calls from Python are simpler than pulling in the kubernetes Python client for the narrow operations needed (get logs, patch configmap, rollout restart). |

**Why not the `kubernetes` Python client library:** The E2E tests need exactly four kubectl operations: `kubectl logs`, `kubectl get`, `kubectl patch configmap`, and `kubectl rollout restart`. Subprocess calls to kubectl are 5 lines each and require no dependency. The kubernetes Python client adds a 15MB dependency with auth complexity for operations that are simpler as CLI calls. If the test suite eventually needs programmatic K8s object creation (deploying test-specific pods), revisit this decision.

### Test Timing & Retry

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| tenacity | 9.1.4 | Retry with backoff for async pipeline verification | E2E tests must poll Prometheus until metrics appear (pipeline has latency: SNMP poll -> MediatR -> OTel -> Collector -> remote_write -> Prometheus). tenacity provides declarative retry with configurable stop/wait strategies, cleaner than hand-rolled while loops. |

**Why tenacity over hand-rolled retry:** Every E2E test that queries Prometheus needs a "wait until metric appears" pattern. Without a retry library, every test function contains a while loop with sleep, timeout check, and assertion. With tenacity: `@retry(stop=stop_after_delay(30), wait=wait_fixed(2))` -- one decorator, clear intent.

---

## What NOT to Add

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| kubernetes Python client | Heavyweight (15MB+), complex auth setup, overkill for 4 CLI operations | `subprocess.run(["kubectl", ...])` |
| selenium / playwright | No browser UI to test. Grafana dashboard validation is out of scope for E2E. | Direct Prometheus HTTP API queries |
| testcontainers-python | The K8s cluster and all services already run. E2E tests validate the deployed system, not spin up a new one. | kubectl against existing namespace |
| pytest-asyncio | No async code in the test harness. SNMP sending, HTTP queries, and kubectl calls are all synchronous. | Standard synchronous pytest |
| docker-compose test orchestration | System is deployed on K8s. Docker-compose would create a parallel universe that does not validate the real deployment. | Test against live K8s namespace |
| pysnmp-lextudio | This is the old fork name. The canonical package is now just `pysnmp` (maintained by LeXtudio). Do not install both. | `pysnmp==7.1.22` |
| pytest-xdist (parallel) | E2E tests share cluster state (metrics in Prometheus, logs in pods). Parallel execution causes flaky tests from metric cross-contamination. | Sequential execution (default pytest) |

---

## Integration Points with Existing Stack

### Prometheus HTTP API

The E2E tests query Prometheus directly via its HTTP API. Key endpoints:

| Endpoint | Method | Purpose in E2E |
|----------|--------|----------------|
| `/api/v1/query` | GET | Instant query -- "does metric X with label Y exist right now?" |
| `/api/v1/query_range` | GET | Range query -- "did metric X appear within the last N seconds?" |
| `/api/v1/series` | GET | Series metadata -- "what series match this selector?" |

**Access pattern:** Port-forward Prometheus (`kubectl port-forward svc/prometheus 9090:9090 -n simetra`) before test execution. Tests hit `http://localhost:9090`.

**Key queries for verification:**
```python
# Verify gauge metric arrived
requests.get("http://localhost:9090/api/v1/query", params={
    "query": 'snmp_gauge{device="TEST-DEVICE", metric_name="link_state"}'
})

# Verify counter metric with delta
requests.get("http://localhost:9090/api/v1/query_range", params={
    "query": 'snmp_counter{device="TEST-DEVICE"}',
    "start": start_ts,
    "end": end_ts,
    "step": "5s"
})
```

### kubectl Log Parsing

Tests verify pipeline behavior by parsing structured logs from collector pods:

```python
result = subprocess.run(
    ["kubectl", "logs", "-l", "app=snmp-collector", "-n", "simetra",
     "--since=60s", "--tail=100"],
    capture_output=True, text=True
)
# Parse for expected log patterns
assert "MetricPollJob completed" in result.stdout
```

### ConfigMap Hot-Reload Testing

Tests verify the ConfigMap watcher by patching config and observing behavior:

```python
# Patch ConfigMap
subprocess.run([
    "kubectl", "patch", "configmap", "snmp-collector-config",
    "-n", "simetra", "--type=merge",
    "-p", json.dumps({"data": {"appsettings.Production.json": new_config}})
])
# Wait and verify pods picked up new config via logs
```

### Test Simulator Integration

The dedicated test simulator runs as a K8s pod in the same namespace, reachable by the collector's SNMP polling:

```
tests/e2e/
  simulator/
    test_simulator.py    # Deterministic SNMP agent
    Dockerfile
    requirements.txt     # pysnmp==7.1.22
  conftest.py            # pytest fixtures (deploy simulator, port-forward, cleanup)
  test_poll_pipeline.py  # Poll -> OTel -> Prometheus verification
  test_trap_pipeline.py  # Trap -> MediatR -> OTel -> Prometheus verification
  test_config_reload.py  # ConfigMap patch -> hot-reload verification
  test_leader_election.py # Multi-replica behavior verification
```

---

## Installation

```bash
# Create test virtualenv
python -m venv tests/e2e/.venv
source tests/e2e/.venv/bin/activate  # or .venv/Scripts/activate on Windows

# Install test dependencies
pip install pytest==9.0.2 requests==2.32.5 tenacity==9.1.4

# Test simulator uses same pysnmp as production simulators
pip install pysnmp==7.1.22
```

**requirements.txt for `tests/e2e/`:**
```
pytest==9.0.2
requests==2.32.5
tenacity==9.1.4
pysnmp==7.1.22
```

---

## Pipeline Latency Budget

Understanding the end-to-end latency is critical for setting test timeouts:

| Stage | Expected Latency | Notes |
|-------|-------------------|-------|
| SNMP poll/trap -> MediatR pipeline | < 100ms | In-process, fast |
| OTel SDK collection interval | 5-15s | Configurable via `PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds` |
| OTel Collector -> remote_write | 1-5s | Batch + flush |
| Prometheus ingestion | < 1s | Near-instant on write path |
| **Total worst case** | **~25s** | Conservative; most metrics appear within 10-15s |

**Recommendation:** Set default test retry timeout to 30 seconds with 2-second polling interval. This covers the worst case with margin. Individual tests can override for faster-expected scenarios.

---

## Version Compatibility

| Package | Python Version | Notes |
|---------|---------------|-------|
| pytest 9.0.2 | 3.9+ | Matches Python 3.12 used by existing simulators |
| requests 2.32.5 | 3.8+ | Universal compatibility |
| tenacity 9.1.4 | 3.9+ | Compatible with Python 3.12 |
| pysnmp 7.1.22 | 3.9+ | Same version as OBP/NPB simulators |

**Python version:** Use 3.12 to match existing simulator Dockerfiles.

---

## Sources

- [PyPI: pytest 9.0.2](https://pypi.org/project/pytest/) -- version verified 2026-03-09
- [PyPI: requests 2.32.5](https://pypi.org/project/requests/) -- version verified 2026-03-09
- [PyPI: pysnmp 7.1.22](https://pypi.org/project/pysnmp/) -- version verified, matches existing simulators
- [Prometheus HTTP API](https://prometheus.io/docs/prometheus/latest/querying/api/) -- instant query, range query, series endpoints documented
- [PyPI: tenacity](https://pypi.org/project/tenacity/) -- retry library for polling patterns

---
*Stack research for: E2E System Verification*
*Researched: 2026-03-09*
