# Phase 23: OID Map Mutation and Device Lifecycle Verification - Context

**Gathered:** 2026-03-09
**Status:** Ready for planning

<domain>
## Phase Boundary

Verify that runtime ConfigMap changes (OID rename/remove/add in simetra-oidmaps, device add/remove/modify in simetra-devices) propagate correctly to Prometheus metrics without pod restarts. Uses ConfigMap snapshot/restore from Phase 22. No SnmpCollector code modifications.

</domain>

<decisions>
## Implementation Decisions

### OID Mutation Scenarios
- **Rename (MUT-01): Rename e2e_gauge_test** — rename E2E-SIM's primary gauge OID (.999.1.1.0) to a new metric_name (e.g., "e2e_renamed_gauge"), verify new name appears in Prometheus
- **Remove (MUT-02): Verify becomes Unknown** — remove OID from oidmaps, query snmp_gauge{metric_name="Unknown"} and confirm the removed OID now appears there (proves full reclassification)
- **Add (MUT-03): Map .999.2.1.0 (gauge)** — add a new oidmap entry for the unmapped gauge OID, proving it transitions from Unknown to a named metric
- **OID add prereq: Reuse Phase 22 fixture** — apply e2e-sim-unmapped-configmap.yaml to start polling the unmapped OID, then add the oidmap entry
- **Separate scenarios** — one scenario per mutation type (e.g., 18-oid-rename.sh, 19-oid-remove.sh, 20-oid-add.sh)

### Device Lifecycle Scenarios
- **Add (DEV-01): New device at E2E-SIM IP** — add "E2E-SIM-2" pointing at the same E2E simulator, verify poll metrics appear
- **Remove (DEV-02): Poll counter delta = 0** — snapshot poll_executed for the removed device, wait, check delta is 0 (proves no new polls happening)
- **Modify (DEV-03): E2E-SIM 10s → 5s** — halve the poll interval, verify poll_executed counter increases faster (higher delta in same window)
- **Separate scenarios** — one per lifecycle event (e.g., 21-device-add.sh, 22-device-remove.sh, 23-device-modify-interval.sh)

### Scenario Ordering & Isolation
- **OID mutations first (18-20), then device lifecycle (21-23)** — OID mutations are simpler, builds confidence
- **Per-scenario snapshot/restore** — each scenario snapshots before mutating and restores after, maximum isolation, any scenario can run standalone
- **Wait for propagation after restore** — after restoring ConfigMap, poll until original metrics reappear before ending scenario, guarantees clean state for next scenario

### Fixture & ConfigMap Strategy
- **Static fixture files for all scenarios** — pre-built YAML files for each mutation, consistent with Phase 21/22 pattern
- **Full oidmaps replacement** — oidmaps fixtures contain ALL entries (OBP + NPB + E2E-SIM) since kubectl apply replaces the whole ConfigMap
- **New static fixtures per device scenario** — device-added.yaml, device-removed.yaml, device-modified-interval.yaml, each a full simetra-devices ConfigMap
- **Same fixtures/ directory** — keep all fixtures flat in tests/e2e/fixtures/

</decisions>

<specifics>
## Specific Ideas

- Scenario numbering continues from Phase 22: 18, 19, 20 for OID mutations, 21, 22, 23 for device lifecycle
- For OID add (MUT-03), the scenario needs a two-step mutation: first apply devices fixture (to poll the unmapped OID), then apply oidmaps fixture (to add the mapping)
- Poll interval change verification: compare poll_executed deltas in two windows — before change (10s interval) vs after change (5s interval), the delta should roughly double
- Device removal verification uses the same delta pattern from Phase 21 scenarios but asserts delta = 0 instead of delta > 0

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 23-oid-map-mutation-and-device-lifecycle-verification*
*Context gathered: 2026-03-09*
