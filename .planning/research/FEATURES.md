# Feature Research

**Domain:** SNMP Monitoring System (C# .NET 9, OTel push to Prometheus/Grafana)
**Researched:** 2026-03-04
**Confidence:** MEDIUM — Table stakes sourced from multiple industry surveys (WebSearch verified against Exabeam, Uptrace, OneUptime official docs). Differentiators and anti-features draw on ecosystem analysis with MEDIUM confidence.

---

## Feature Landscape

### Table Stakes (Users Expect These)

Features users assume exist. Missing these = product feels incomplete.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| SNMP Trap Reception (UDP 162) | All SNMP monitoring tools receive traps — this is the protocol's primary async notification mechanism | LOW | Already in design: UDP 162 listener. Must handle burst floods without dropping. |
| Scheduled Polling (SNMP GET/GETNEXT/GETBULK) | Traps only fire on events; polling catches baseline drift, silent failures, and devices that can't send traps | MEDIUM | Already in design: Quartz poller. Interval configurability expected. |
| Multi-version SNMP support (v1, v2c, v3) | Devices in the wild run all three; any production environment has mixed versions | MEDIUM | v3 is security-critical; v1/v2c still common on OBP/NPB gear. Must not assume v3-only. |
| OID → human-readable metric name resolution | Raw OIDs (1.3.6.1.2.1.1.1.0) are opaque; operators need metric_name labels like `system_description` | LOW | Already in design: flat Dictionary<string,string> OID map. Verify map is hot-reloadable. |
| Metric export to Prometheus/Grafana | The standard observability backend for infrastructure; operators expect PromQL queryability | MEDIUM | Already in design: OTel push to Prometheus. snmp_gauge, snmp_counter, snmp_info instruments. |
| Threshold-based alerting surface | Operators must be able to set alert conditions on collected metrics | LOW | This system produces metrics; actual alerting lives in Prometheus Alertmanager + Grafana. System responsibility: export clean, labelled metrics with correct type (counter vs gauge) so downstream alerts work. |
| Structured logging with correlation IDs | Without correlation IDs, debugging multi-device poll failures or trap storms is nearly impossible | LOW | Already in design: structured logging + OTLP + correlation IDs. Must propagate trace context. |
| System self-health metrics | Operators need to know if the monitoring system itself is working — dropped traps, poll failures, queue depth | MEDIUM | Already in design: pipeline metrics. Critical to expose: traps_received, traps_dropped, poll_successes, poll_failures, oid_resolution_misses. |
| Device-agnostic operation | The system must work with any SNMP-capable device without device-specific code paths | LOW | Already in design: device-agnostic by OID map. Validate that NPB and OBP OIDs resolve correctly. |
| Graceful handling of unreachable devices | Devices go offline; the system must not crash, flood logs, or block the poller queue | MEDIUM | Need: per-device error budget, backoff on repeated failures, circuit-breaker pattern for dead devices. |
| Configuration via environment/config file | Operators must be able to tune poll targets, intervals, SNMP community strings without code changes | LOW | Standard .NET 9 IConfiguration pattern. Secrets (community strings) via Kubernetes secrets, not config files. |

---

### Differentiators (Competitive Advantage)

Features that set the product apart. Not required, but valued.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Hot-reloadable OID map | Add or change OID mappings without restarting the service — critical for NPB/OBP rollouts where new firmware brings new OIDs | MEDIUM | Requires FileSystemWatcher or config reload trigger. Dictionary<string,string> makes this tractable since no schema migration needed. |
| Typed metric inference (gauge/counter/info) | Automatically selecting the right OTel instrument based on SNMP TypeCode means downstream PromQL `rate()` and `increase()` work correctly without operator tuning | LOW | Already in design (three instruments based on TypeCode). This is a genuine differentiator over naive "dump everything as gauge" approaches. |
| Leader-based business metric export in K8s | In a replicated K8s deployment, having all instances active for resilience but only the leader exporting business-level aggregates avoids double-counting in Prometheus | HIGH | Already in design: K8s Lease leader election. This is sophisticated and non-obvious — most SNMP exporters don't address this. |
| Pipeline health as first-class metrics | Exposing collector pipeline metrics (traps_received, poll_latency_p99, oid_miss_rate) lets operators detect silent data loss before it becomes an incident | MEDIUM | Already in design. Differentiator: most SNMP tools have no self-observability. OTel pipeline health monitoring is an emerging best practice (2026). |
| MediatR pipeline for extensibility | Request/handler pattern means adding a new processing step (e.g., enrichment, filtering, deduplication) doesn't require touching existing handlers | MEDIUM | Already in design. Differentiator over monolithic poll-and-push patterns. Enables future: trap correlation, event deduplication. |
| SNMP v3 authentication + encryption | SNMPv1/v2c send community strings in cleartext; v3 provides user-based auth and AES/DES encryption, required for any security-conscious deployment | HIGH | v3 is table stakes in enterprise; differentiator in internal tooling where teams often skip it. Requires EngineID management and key exchange complexity. |
| Trap storm protection / rate limiting | Without it, a misbehaving device can flood the listener and cause memory exhaustion or cascade failures | MEDIUM | Per-source-IP rate limiting on the UDP listener. Configurable burst allowance. Differentiator: most open-source SNMP listeners have no protection. |
| Configurable polling intervals per device | Critical devices (core NPB) warrant 30s polls; low-priority devices can be 5min. Flat interval for all wastes resources or misses fast-moving metrics | MEDIUM | Quartz job configuration per target. Allows tiered monitoring strategy. |
| OID walk / auto-discovery of available OIDs | SNMP walk discovers what OIDs a device actually supports, populating the OID map automatically rather than requiring manual configuration | HIGH | High value for onboarding new NPB/OBP firmware versions. Complex: requires GETNEXT walk, deduplication, and presenting results for operator review. Defer to post-MVP. |
| Inform vs Trap acknowledgment support | SNMP Inform messages require acknowledgment; devices retry until acknowledged. More reliable than fire-and-forget traps | HIGH | SNMPSharpNet or similar library must support Informs. Adds reliability for high-priority events. Complexity: reply must go back to originating device. |

---

### Anti-Features (Commonly Requested, Often Problematic)

Features that seem good but create problems.

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Built-in alerting engine (thresholds, notifications) | "Why go to Grafana for alerts when the monitor already has the data?" | Duplicates Prometheus Alertmanager + Grafana alerting — two places to define alert rules, two places to silence, two notification systems to maintain. Creates split-brain alerting. | Export clean, correctly-typed metrics with good labels. Let Prometheus Alertmanager handle alerting. This system's job is collection, not action. |
| MIB file parsing and MIB browser UI | "We need to browse the MIB tree to find OIDs" | MIB parsing is a complex domain (ASN.1 grammars, MIB dependencies, enterprise extensions). Adding a MIB browser turns a metrics exporter into a management platform. Scope creep. | Provide a flat OID map config format. Operators use external MIB browsers (MIB Browser, SNMPwalk) to discover OIDs, then add them to the map. |
| Configuration management / device provisioning | "The monitor should push SNMP SET commands to configure devices" | SNMP SET is a write operation with serious security implications. Mixing read-monitoring with write-configuration in one service violates single responsibility and creates a privilege escalation attack surface. | Keep this service read-only (GET/GETNEXT/GETBULK + trap receive only). Device configuration is a separate tool with separate access controls. |
| Full network topology discovery (auto-detect all devices) | "Should automatically find all SNMP devices on the network" | ARP scanning, ICMP ping sweeps, and SNMP community probing are noisy, potentially disruptive, and require broad network access. Wrong for a targeted NPB/OBP monitor. | Accept an explicit target list in configuration. Operators know their NPB/OBP devices — they don't need discovery. |
| Time-series storage / TSDB embedded in the service | "Why depend on Prometheus? Just store metrics internally." | Reinventing a TSDB is enormous scope, and the result will be worse than Prometheus. No PromQL, no Grafana integration, no standard query API. | Push to Prometheus via OTel. This is already in the design and is the correct architecture. |
| Web UI / dashboard embedded in service | "Operators want a UI in the monitor itself" | Grafana already provides a production-quality dashboard. Building a second UI duplicates work and creates a maintenance burden. Grafana has rich SNMP dashboard templates. | Build Grafana dashboards (JSON model, provisioned via ConfigMap) that consume the exported metrics. |
| Alert deduplication / event correlation engine | "Group related traps from the same device into one incident" | This is event correlation — a hard problem. Products like PagerDuty, OpsGenie, and Grafana OnCall are built specifically for this. Building it here leads to partial, brittle solutions. | Export traps as structured log events with device labels. Let upstream AIOps or on-call platforms handle correlation. |

---

## Feature Dependencies

```
[Quartz Poller]
    └──requires──> [OID Map (Dictionary<string,string>)]
                       └──requires──> [SNMP GET/GETNEXT/GETBULK client]

[Trap Listener UDP 162]
    └──requires──> [OID Map (Dictionary<string,string>)]
    └──enhances──> [Pipeline metrics (traps_received, traps_dropped)]

[OTel Metric Export]
    └──requires──> [Typed metric instruments (snmp_gauge, snmp_counter, snmp_info)]
                       └──requires──> [SNMP TypeCode inference]

[Leader Election (K8s Lease)]
    └──requires──> [K8s API access (in-cluster service account)]
    └──enhances──> [Business metric export (deduplication)]

[Pipeline Health Metrics]
    └──enhances──> [Quartz Poller] (poll_latency, poll_failure_rate)
    └──enhances──> [Trap Listener] (traps_received, traps_dropped)
    └──enhances──> [OTel Export] (export_queue_depth, export_failures)

[Trap Storm Protection]
    └──requires──> [Trap Listener UDP 162]
    └──enhances──> [Pipeline Health Metrics] (rate_limited_traps counter)

[Hot-reloadable OID Map]
    └──enhances──> [OID Map (Dictionary<string,string>)]

[Per-device polling intervals]
    └──enhances──> [Quartz Poller]

[SNMP v3 auth+encryption]
    └──requires──> [SNMP GET/GETNEXT/GETBULK client]
    └──conflicts──> [v1/v2c community string config] (separate auth config path needed)
```

### Dependency Notes

- **Quartz Poller requires OID Map:** Without OID resolution, polled values are raw numeric OIDs that Grafana operators can't interpret.
- **Typed metric instruments require TypeCode inference:** The snmp_gauge/snmp_counter/snmp_info split is only meaningful if TypeCode from the SNMP response is used — otherwise default-to-gauge loses counter semantics.
- **Leader Election requires K8s API access:** K8s Lease API requires in-cluster service account with appropriate RBAC. Fails outside K8s without a fallback (always-leader or no-leader mode for local dev).
- **Pipeline health metrics enhance everything:** These are orthogonal to device monitoring — they observe the observer. Should be wired in at each collection point, not bolted on later.
- **Trap storm protection requires listener:** Must be implemented inside the UDP receive loop, not as a MediatR handler (by the time the handler runs, UDP buffer may already be full).

---

## MVP Definition

### Launch With (v1)

Minimum viable product — what's needed for the system to be useful for NPB/OBP monitoring.

- [ ] **SNMP trap listener (UDP 162)** — core async event path; without this, the system misses device-initiated alerts
- [ ] **Quartz-based poller (SNMP GET/GETBULK)** — baseline polling for devices that don't send traps for all state changes
- [ ] **OID map resolution** — flat Dictionary<string,string> config; must resolve the specific OIDs used by target NPB/OBP devices
- [ ] **Three metric instruments (snmp_gauge, snmp_counter, snmp_info)** — correct TypeCode-to-instrument mapping so downstream PromQL works
- [ ] **OTel push to Prometheus** — the delivery mechanism for all collected metrics
- [ ] **Pipeline health metrics** — traps_received, poll_success/failure counts, oid_miss_rate; critical for knowing the system is working
- [ ] **Structured logging + correlation IDs** — without this, debugging is guesswork
- [ ] **K8s leader election** — needed for correct multi-replica behavior; avoids duplicate metric export
- [ ] **Graceful device unreachability handling** — NPB/OBP devices reboot; the service must not crash or spam logs

### Add After Validation (v1.x)

Features to add once core collection is confirmed working.

- [ ] **Hot-reloadable OID map** — trigger: operators report needing restart to add new device OIDs after firmware updates
- [ ] **Per-device polling intervals** — trigger: some NPB devices show performance impact from 30s polling; others need tighter intervals
- [ ] **Trap storm protection / rate limiting** — trigger: first incident where a failing device floods the listener
- [ ] **SNMP v3 auth + encryption** — trigger: security review requirement or deployment to untrusted network segment
- [ ] **SNMP Inform acknowledgment support** — trigger: specific device types require inform reliability guarantees

### Future Consideration (v2+)

Features to defer until operational experience informs requirements.

- [ ] **OID walk / auto-discovery** — defer: the explicit OID map is simpler and more maintainable; auto-discovery adds complexity before operators know they need it
- [ ] **Grafana dashboard provisioning** — defer: useful but doesn't affect data quality; build when dashboards stabilize
- [ ] **SNMP SET / configuration push** — defer (and reconsider carefully): requires separate security model; do not add to this service without deliberate design

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Trap listener (UDP 162) | HIGH | LOW | P1 |
| Quartz poller (GET/GETBULK) | HIGH | MEDIUM | P1 |
| OID map resolution | HIGH | LOW | P1 |
| snmp_gauge / snmp_counter / snmp_info instruments | HIGH | LOW | P1 |
| OTel push to Prometheus | HIGH | MEDIUM | P1 |
| Structured logging + correlation IDs | HIGH | LOW | P1 |
| Pipeline health metrics | HIGH | MEDIUM | P1 |
| K8s leader election | HIGH | MEDIUM | P1 |
| Graceful device unreachability | HIGH | MEDIUM | P1 |
| Hot-reloadable OID map | MEDIUM | MEDIUM | P2 |
| Per-device polling intervals | MEDIUM | MEDIUM | P2 |
| Trap storm protection | HIGH | MEDIUM | P2 |
| SNMP v3 auth + encryption | HIGH | HIGH | P2 |
| SNMP Inform acknowledgment | MEDIUM | HIGH | P3 |
| OID walk / auto-discovery | MEDIUM | HIGH | P3 |
| Grafana dashboard provisioning | MEDIUM | LOW | P2 |

**Priority key:**
- P1: Must have for launch
- P2: Should have, add when possible
- P3: Nice to have, future consideration

---

## Competitor Feature Analysis

Commercial SNMP monitoring tools (Datadog NDM, PRTG, SolarWinds NPM, Nagios, Zabbix) vs this system's approach.

| Feature | Datadog NDM | PRTG / SolarWinds | This System |
|---------|-------------|-------------------|-------------|
| OID resolution | Device profiles (auto) | MIB import + auto-discovery | Explicit flat OID map (simpler, deliberate) |
| Metric typing | Auto-inferred from MIB | Gauge-dominant | TypeCode-based inference (correct counters) |
| Trap handling | Trap receiver + event rules | Built-in trap receiver | UDP 162 listener + MediatR pipeline |
| Export format | Proprietary + DatadogAPI | Proprietary | OTel → Prometheus (open, standard) |
| HA / multi-instance | Agent-based, distributed | Probe-based clustering | K8s Lease leader election (elegant for K8s-native) |
| Self-observability | Basic agent metrics | Probe health | First-class pipeline metrics (superior) |
| Alert engine | Datadog monitors | Built-in alerts | Delegate to Prometheus/Grafana (correct separation) |
| Deployment target | SaaS/cloud | On-prem appliance | K8s-native (differentiator for cloud-native orgs) |
| Cost | High (per device license) | High (per sensor license) | Free (open, no per-device cost) |

**Our position:** This system is more narrowly scoped than commercial tools (no built-in alerting, no MIB browser, no topology maps) but has a cleaner architecture for K8s-native deployments, correct metric typing, and first-class pipeline observability. The deliberate narrow scope is a strength, not a gap.

---

## Sources

- [The 10 Best SNMP Trap Monitoring Tools for 2025](https://network-king.net/the-10-best-snmp-trap-monitoring-tools-for-2025-features-pros-cons/) — feature inventory for trap monitoring tools (WebSearch + WebFetch, MEDIUM confidence)
- [SNMP Monitoring with OpenTelemetry Guide](https://openobserve.ai/blog/snmp-monitoring-opentelemetry/) — OTel SNMP receiver capabilities (WebFetch, MEDIUM confidence)
- [How to Monitor and Alert on OpenTelemetry Pipeline Health](https://oneuptime.com/blog/post/2026-02-06-monitor-alert-opentelemetry-pipeline-health/view) — pipeline metrics and alert strategy (WebFetch, MEDIUM confidence)
- [Configure SNMP Receiver in OpenTelemetry Collector](https://oneuptime.com/blog/post/2026-02-06-snmp-receiver-opentelemetry-collector/view) — OTel SNMP receiver feature set (WebFetch, MEDIUM confidence)
- [Essentials of SNMP Monitoring — Last9](https://last9.io/blog/essentials-of-snmp-monitoring/) — core components and capabilities (WebFetch, MEDIUM confidence)
- [SNMP Monitoring: The Complete Guide — Uptrace](https://uptrace.dev/glossary/snmp-monitoring) — differentiating capabilities (WebFetch, MEDIUM confidence)
- [SNMP Trap Receiver Basics and Best Practices — WhatsUp Gold](https://www.whatsupgold.com/snmp/snmp-trap-receiver) — trap receiver best practices (WebSearch, LOW confidence, consistent with above)
- [Network Monitoring Best Practices 2025 — Netflow Logic](https://www.netflowlogic.com/network-monitoring-best-practices-for-2025-navigating-the-hyperconnected-future-with-enhanced-netflow-and-snmp/) — polling and threshold guidance (WebSearch, LOW confidence)
- [Best Network Monitoring Software 2026 — Domotz](https://blog.domotz.com/think-like-msp/best-network-monitoring-tools/) — enterprise feature expectations (WebSearch, LOW confidence)
- [Datadog SNMP Profiles](https://docs.datadoghq.com/integrations/rapdev-snmp-profiles/) — device profile approach comparison (WebSearch, LOW confidence)

---
*Feature research for: SNMP monitoring system (C# .NET 9, OTel, Prometheus/Grafana)*
*Researched: 2026-03-04*
