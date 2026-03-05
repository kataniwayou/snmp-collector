# Phase 4: Counter Delta Engine - Context

**Gathered:** 2026-03-05
**Status:** Ready for planning

<domain>
## Phase Boundary

The counter delta engine correctly computes deltas for all counter scenarios — normal increment, Counter32 wrap-around at 2^32, Counter64 (no wrap handling), device reboot detection via sysUpTime, and first-poll skip. The engine is built, wired into the existing MediatR pipeline (OtelMetricHandler), and unit-testable with synthetic data. Actual SNMP polling is Phase 6. Trap ingestion is Phase 5.

</domain>

<decisions>
## Implementation Decisions

### Wrap vs reboot disambiguation
- Poll sysUpTime (OID 1.3.6.1.2.1.1.3.0) **alongside every counter poll** — bundled into the same SNMP GET request, not a separate poll
- sysUpTime stored **per-device (shared)** — one sysUpTime value per device IP, all counter OIDs for that device use the same reboot detection
- If sysUpTime decreased since last poll → **reboot detected** — current counter value is used as the delta
- If sysUpTime increased and current < previous → **wrap-around** (Counter32 only)
- If sysUpTime is **unavailable** (device doesn't expose it or SNMP error) → **assume reboot (conservative)** — treat current < previous as reboot, use current value as delta

### Counter type handling
- **Counter32**: Wrap-around detection at 2^32 (4,294,967,296). Wrap delta = (2^32 - previous) + current
- **Counter64**: No wrap-around detection — 2^64 is unreachable in practice. If Counter64 current < previous, always treat as reboot
- Use **SNMP TypeCode** to determine which threshold applies (Counter32 vs Counter64)
- All computed deltas **clamped to non-negative** — counters should never produce negative values in Prometheus

### First-poll and cache lifecycle
- **Always cold start** — no persistence across app restarts. All counters need one baseline poll before emitting deltas. Simple, stateless, appropriate for K8s pod lifecycle
- Cache entries kept **indefinitely** (no eviction) — the set is bounded by device config (OID+agent combinations)
- First-poll baseline stored, logged at **Debug** level
- Wrap-around events logged at **Debug** (routine on high-traffic Counter32 interfaces)
- Reboot events logged at **Information** (notable operational events operators want to see)

### Pipeline integration
- Add **RecordCounter** to ISnmpMetricFactory — symmetric with RecordGauge and RecordInfo, creates the `snmp_counter` instrument
- Build the engine **AND** wire it into OtelMetricHandler — Counter32/Counter64 switch arms call the delta engine instead of logging and skipping. End-to-end counter flow works with synthetic data in tests
- sysUpTime polling integrated into counter OID polls (bundled SNMP GET) — Phase 6 poll scheduler includes sysUpTime automatically when counter OIDs are present

### Claude's Discretion
- Whether to implement as a new IPipelineBehavior or as a service called by OtelMetricHandler (user said "you decide" on integration pattern)
- Internal cache data structure (ConcurrentDictionary keyed by OID+agent, or other)
- How sysUpTime values flow through the pipeline to the delta engine (notification property, separate lookup, etc.)
- Test helper design for synthetic counter scenarios

</decisions>

<specifics>
## Specific Ideas

- The delta cache key must be OID+agent combination so two different agents reporting the same OID maintain independent delta state (DELT-05 requirement)
- Counter deltas should flow to a new `snmp_counter` OTel Counter<double> instrument via RecordCounter, using the same 5-label taxonomy (site_name, metric_name, oid, agent, source) as snmp_gauge
- sysUpTime (OID 1.3.6.1.2.1.1.3.0) is a TimeTicks value — centiseconds since device boot. A decrease means the device rebooted.
- Phase 3 already has the Counter32/Counter64 switch arms in OtelMetricHandler that log at Debug and skip — Phase 4 replaces those with actual delta computation and recording

</specifics>

<deferred>
## Deferred Ideas

- Actual sysUpTime SNMP GET polling happens in Phase 6 (Poll Scheduling) — Phase 4 builds the engine and tests it with synthetic sysUpTime values
- Rate-of-change calculation (delta per second) — not in scope, raw deltas are sufficient for Prometheus rate() functions

</deferred>

---

*Phase: 04-counter-delta-engine*
*Context gathered: 2026-03-05*
