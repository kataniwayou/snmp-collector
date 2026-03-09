# Phase 21: Test Harness and Pipeline Counter Verification - Context

**Gathered:** 2026-03-09
**Status:** Ready for planning

<domain>
## Phase Boundary

Build a reusable bash test runner with poll-until-satisfied utilities and delta-based counter assertions, then verify all 10 pipeline counters increment correctly from existing simulator activity. The test runner framework built here is reused by Phases 22-24. No SnmpCollector code modifications.

</domain>

<decisions>
## Implementation Decisions

### Test Runner Structure
- **Location: `tests/e2e/`** — dedicated E2E test directory under tests/
- **Invocation: Run all sequentially** — single command runs every scenario in order, no cherry-picking or category filtering
- **Port-forward management: Runner manages them** — start port-forwards at beginning, kill on exit via `trap EXIT`
- **Claude's discretion: Script layout** — Claude decides whether single script or modular (lib/ + scenarios/)

### Pre-flight & Port-forwards
- **Port-forwards needed: Prometheus (9090) + kubectl logs** for trap/poll evidence
- **Pre-flight checks: Pods + Prometheus** — verify all snmp-collector pods Running + Prometheus reachable via port-forward
- **Pre-flight failure: Fail immediately** — exit with clear error showing what's missing, no retries
- **Cleanup: trap EXIT** — handler kills background port-forward processes on any exit including Ctrl+C

### Counter Assertion Patterns
- **poll_unreachable / poll_recovered: Fake device** — add a device entry pointing to a non-existent host, verify poll_unreachable increments. Remove device to test recovery.
- **Claude's discretion: event_validation_failed** — Claude investigates the codebase validation path and decides how/whether to trigger it. If it doesn't naturally increment, document that as correct behavior.
- **Claude's discretion: Delta thresholds** — Claude picks what delta value proves each counter works (any non-zero vs minimum 2+), based on what's realistic per counter
- **Claude's discretion: trap_dropped** — Claude investigates the drop path in the codebase and decides the trigger strategy

### Test Output & Reporting
- **Live output: Pass/fail per scenario** — each scenario prints PASS or FAIL with a one-line description (go test style)
- **Report file: Markdown** — generate a .md report with pass/fail table and evidence (Prometheus query results, log excerpts)
- **Evidence: In the report file** — Prometheus query results and log excerpts saved inline in the markdown report
- **Failure mode: Run all, report at end** — continue through all scenarios, show full summary at end regardless of failures

</decisions>

<specifics>
## Specific Ideas

- Poll-until-satisfied utility must use 30s timeout with 3s interval (from STATE.md E2E Test Context) — never fixed sleeps
- Counter assertions use delta patterns: capture pre-value, wait for activity, capture post-value, assert post > pre
- Delta queries must filter by device_name to exclude heartbeat noise
- All 10 counters: trap_received, trap_auth_failed, trap_dropped, poll_executed, poll_unreachable, poll_recovered, event_handled, event_validation_failed, oid_resolved, oid_unresolved

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 21-test-harness-and-pipeline-counter-verification*
*Context gathered: 2026-03-09*
