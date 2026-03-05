# Phase 7: Leader Election and Role-Gated Export - Context

**Gathered:** 2026-03-05
**Status:** Ready for planning

<domain>
## Phase Boundary

In a multi-instance Kubernetes deployment, exactly one pod exports business metrics (snmp_gauge, snmp_counter, snmp_info) while all pods export pipeline metrics and System.Runtime metrics. K8s Lease API drives leader election with near-instant failover on graceful shutdown. AlwaysLeaderElection provides the local dev fallback. ILeaderElection as a single DI singleton satisfies both the interface and IHostedService.

</domain>

<decisions>
## Implementation Decisions

### Lease behavior
- Lease duration, renewal interval, and ratio: Claude decides based on K8s lease best practices and client-go patterns
- Network partition handling: Claude decides the safest approach for metric deduplication (stop exporting on renewal failure vs keep exporting until lease expires)
- Lease identity: Claude decides based on K8s lease API conventions (pod name, pod name + namespace, etc.)

### Failover mechanics
- Graceful shutdown: explicit lease delete on SIGTERM for near-instant failover (ROADMAP SC#3 requires "within the Kubernetes lease renewal interval")
- Metric gaps during transition: Claude decides based on Prometheus dedup behavior and OTel export semantics
- Role change observability: Claude decides based on operational value vs complexity (log only, log + counter, log + counter + label)
- New leader warm-up: Claude decides based on how OTel metric export works with existing instrument state (export immediately vs wait one cycle)

### Export gating strategy
- Gating location: Claude decides based on OTel SDK architecture and existing Simetra patterns (custom exporter wrapper vs conditional handler)
- Business vs pipeline meter distinction: Claude decides based on OTel meter filtering capabilities and existing instrument naming (separate meter names vs instrument name prefix)
- System.Runtime metrics: exported by all instances — needed for per-pod health dashboards
- Accumulated state on role change: Claude decides based on OTel cumulative vs delta semantics and Prometheus dedup (full accumulated state vs reset on role change)

### Local dev experience
- AlwaysLeaderElection: Claude decides based on how much lifecycle simulation is useful for local testing (simple IsLeader=true vs mock lifecycle)
- K8s detection: Claude decides based on ROADMAP SC about "auto-detected via IsInCluster()" (KUBERNETES_SERVICE_HOST env var vs explicit config flag)
- Dynamic role in logs: Claude decides based on operational value vs complexity (dynamic leader/follower vs static from config)
- Force leader mechanism: Claude decides based on operational needs (no force mechanism vs config override)

### Claude's Discretion
- All lease timing parameters (duration, renewal interval, retry backoff)
- Network partition handling strategy
- Lease identity derivation
- Metric gap vs overlap preference during failover
- Role change observability approach (log only vs counters vs labels)
- New leader warm-up strategy
- Export gating mechanism (exporter wrapper vs handler conditional)
- Business vs pipeline meter discrimination approach
- Accumulated state semantics on role change
- AlwaysLeaderElection implementation depth
- K8s auto-detection mechanism
- Console formatter role update behavior
- Force leader debug mechanism

</decisions>

<specifics>
## Specific Ideas

- ROADMAP SC#5 locks: K8s lease election instance registered as single DI singleton satisfying both ILeaderElection and IHostedService — verifiable by confirming both interfaces resolve to the same object
- ROADMAP SC#3 locks: near-instant failover via explicit lease deletion on graceful shutdown
- HA-04 locks: all instances poll and receive traps (not leader-only) — leader election gates metric export only
- HA-06 locks: pipeline metrics + System.Runtime exported by all instances
- Phase 1 decision [01-03]: SnmpLogEnrichmentProcessor takes string role not Func<string> — Phase 7 should make this dynamic if role changes are reflected in logs

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 07-leader-election-and-role-gated-export*
*Context gathered: 2026-03-05*
