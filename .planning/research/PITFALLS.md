# Pitfalls Research

**Domain:** SNMP monitoring system — C# .NET 9, MediatR, Quartz.NET, OTel push to Prometheus/Grafana
**Researched:** 2026-03-04
**Confidence:** HIGH (verified via official docs, Cisco RFC references, OTel official docs, Quartz.NET docs, SharpSnmpLib docs)

---

## Critical Pitfalls

### Pitfall 1: Counter32/Counter64 Wrap-Around Treated as Real Spike

**What goes wrong:**
When a Counter32 reaches its maximum value (4,294,967,295) it rolls over to zero. If your delta computation does `current - previous` without checking for wrap-around, you get a negative number. Naive implementations discard this as invalid or emit it as a massive positive spike (via unsigned arithmetic overflow). On a 1 Gbps link, Counter32 wraps in ~34 seconds, meaning this can happen multiple times per polling cycle.

**Why it happens:**
Developers treat SNMP counter values like regular integers. The RFC1155 wrap-around behavior is not obvious, and high-speed interfaces make it a frequent event rather than a rare edge case.

**How to avoid:**
Always apply the SNMP delta formula:
- If `current >= previous`: delta = `current - previous`
- If `current < previous` AND difference is within plausible range: delta = `(MAX_VALUE - previous) + current + 1`
- If `current < previous` AND difference is implausibly large (device reboot): discard the sample (see Pitfall 2)

Use Counter64 (ifHCInOctets, ifHCOutOctets) where the device supports it — Counter64 wraps in years at 10 Gbps. Note: Counter64 requires SNMPv2c or SNMPv3; SNMPv1 does not support it.

**Warning signs:**
- Graphs showing unexplained rate spikes of billions of units
- Negative delta values logged then silently discarded
- Zero-value samples immediately following a spike

**Phase to address:**
Counter delta engine implementation phase — establish the wrap-aware delta formula before any metric publishing is wired up.

---

### Pitfall 2: Device Reboot Causes False Counter Reset — Delta Misread as Traffic Spike

**What goes wrong:**
When a device reboots, all SNMP counters reset to zero. If the previous cached value was, say, 3,000,000,000 and the polled value is now 50,000 (post-reboot), the delta computation produces a massive false spike (or a large negative that unsigned arithmetic inflates). This can trigger false alerts and corrupt rate graphs.

**Why it happens:**
Counter resets via reboot look identical to counter wrap-around. There is no SNMP flag for "this counter just reset." Developers implement wrap detection but miss the reboot case.

**How to avoid:**
Poll `sysUpTime` (OID 1.3.6.1.2.1.1.3.0) on every poll cycle alongside counter OIDs. If `sysUpTime` has decreased since the last poll, the device has rebooted — discard counter deltas for that cycle and re-seed the cache from the new counter values. Alert separately on the reboot event. Note: `sysUpTime` measures how long the SNMP agent has been running, not necessarily OS uptime — use `hrSystemUptime` (1.3.6.1.2.1.25.1.1) for true OS uptime, but expect less device support.

**Warning signs:**
- Extremely large rate values appearing immediately after network maintenance windows
- Counter values decreasing on a device that should not have rebooted
- Alert fatigue around maintenance windows

**Phase to address:**
Counter delta engine implementation phase — add `sysUpTime` polling and reset detection before wiring metrics to dashboards.

---

### Pitfall 3: OTel Metric Cardinality Explosion from Per-Device or Per-OID Labels

**What goes wrong:**
Every unique combination of label values creates a distinct time series in Prometheus. If you add a label for `device_ip`, `oid`, `interface_name`, and `community_string`, you can end up with hundreds of thousands of unique series for a modest device fleet. The OTel SDK enforces a default 2,000 time-series-per-metric limit — instruments that exceed it silently drop data. Prometheus also struggles with cardinality at scale, consuming excessive memory.

**Why it happens:**
Developers apply labels generously because Prometheus labels feel like free-form metadata. The cost is invisible until the system is loaded with real device data.

**How to avoid:**
Design label sets before writing any metric. Rules:
1. Never use a label whose value is unbounded (request IDs, raw OID strings, raw IP addresses if the fleet is large)
2. Limit to labels that are genuinely needed for alerting/grouping: `device_name`, `interface_index`, `metric_type`
3. Use the OTel View API and `MetricStreamConfiguration.CardinalityLimit` to set explicit limits per instrument
4. Model device metadata (location, vendor) as info metrics joined at query time, not as labels on every time series

**Warning signs:**
- Prometheus `tsdb.head_series` growing faster than device count would explain
- OTel SDK warning logs about `InstrumentationScope` being dropped due to cardinality limit
- Grafana queries taking > 5 seconds for dashboards with no complex aggregations

**Phase to address:**
OTel metric model design phase — finalize label taxonomy on paper before instrument creation; cardinality decisions are very hard to migrate post-deployment.

---

### Pitfall 4: MediatR Notification Handler Failure Blocks All Downstream Handlers

**What goes wrong:**
The default MediatR notification publisher (`ForeachAwaitPublisher`) executes handlers sequentially and stops at the first exception. In an SNMP monitoring pipeline, if the "store to database" handler throws, the "export to OTel" handler and "send alert" handler never run. The polling cycle silently loses metric observations for that device.

**Why it happens:**
MediatR's notification semantics look like broadcast (fire to all), but the default behavior is actually sequential with fail-fast. Developers assume "notifications = fire-and-forget to all handlers" without verifying the publisher strategy.

**How to avoid:**
Switch to `TaskWhenAllPublisher` for notification dispatch or implement a custom publisher with per-handler try/catch. With `TaskWhenAllPublisher`, all handlers execute regardless of individual failures, and exceptions are aggregated into `AggregateException`. Wrap each handler's execution in its own catch block and log failures without rethrowing. Alternatively, use MediatR v12+'s configurable `NotificationPublisherType` property in `AddMediatR`.

Critical caveat: `TaskWhenAllPublisher` runs handlers in parallel, which means they share the same DI scope. Services that are not concurrency-safe (EF Core `DbContext`) will cause race conditions. Audit all handler dependencies before enabling parallel dispatch.

**Warning signs:**
- Missing metric observations for specific devices when any downstream handler has errors
- Exception logs from one handler followed by zero logs from subsequent handlers for the same notification
- Alert handlers that never fire when persistence handlers are degraded

**Phase to address:**
MediatR pipeline design phase — set the notification publisher strategy and validate handler error isolation before integrating multiple handlers.

---

### Pitfall 5: SharpSnmpLib ObjectStore is Not Thread-Safe

**What goes wrong:**
SharpSnmpLib's `ObjectStore` (used by `SnmpEngine` for agent/trap handling) is explicitly documented as not thread-safe. The `SnmpEngine` is multi-threaded by design — both `ListenerBinding` and `SnmpApplication` dispatch to the CLR thread pool. Concurrent reads/writes to `ObjectStore` from multiple inbound traps or SNMP requests can produce data corruption, race conditions, or crashes.

**Why it happens:**
The library handles concurrency at the engine level but delegates object storage safety to the developer. The documentation warns about this but it is easy to miss when following samples that do not simulate concurrent load.

**How to avoid:**
- Initialize the thread pool minimum worker thread count before calling `SnmpEngine.Start()` (the docs recommend setting it explicitly)
- Wrap all `ObjectStore` operations in locks or replace with a concurrent-safe data structure (e.g., `ConcurrentDictionary`)
- For trap listener use cases (common in SNMP monitoring), avoid `ObjectStore` entirely — process traps immediately in the handler and dispatch to a channel or queue for downstream processing
- Test under concurrent trap bursts early; single-device dev environments will never surface this

**Warning signs:**
- Sporadic `NullReferenceException` or `KeyNotFoundException` under load in engine handler code
- Trap processing that works in single-device test but fails randomly in production
- ObjectStore corruption only visible during bursts

**Phase to address:**
Trap listener implementation phase — validate thread safety under concurrent load before declaring the listener complete.

---

### Pitfall 6: SNMPv3 USM "Not In Time Window" Silently Drops Traps

**What goes wrong:**
SNMPv3's User-based Security Model (USM) requires that the time stamp in a received message fall within 150 seconds of the receiver's clock (RFC 3414). If the clock on the sending device drifts by more than 150 seconds from the trap listener's clock, the listener silently rejects all inbound traps. SharpSnmpLib will not send a response in this case, which prevents SNMP time re-synchronization, creating an indefinite rejection loop.

**Why it happens:**
NTP misconfiguration or clock drift on network devices is common. This failure mode is invisible — no error log appears on the sending device side, and the listener silently discards the packet.

**How to avoid:**
- Ensure NTP synchronization between all monitored devices and the monitoring server
- Log `notInTimeWindow` rejections explicitly in the trap handler (SharpSnmpLib raises these as `ErrorStatus` results — parse and log them)
- Monitor the count of rejected traps as a metric; alert if it grows
- For development: test with an intentionally drifted clock to confirm rejection logging works

**Warning signs:**
- Traps stop arriving on the listener after a device NTP reconfiguration
- V3 trap reception works in dev (same machine) but fails in production (separate hosts)
- No errors on the device side, no errors in app logs — total silence

**Phase to address:**
Trap listener implementation phase — build explicit rejection logging before testing with real network devices.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Poll with a single Quartz thread pool size for all jobs | Simple config | Long-running jobs (SNMP timeout = 5s) block short-cycle jobs; thread starvation at 50+ devices | Never — size the pool relative to device count from day 1 |
| Use raw OID strings as metric label values | No OID map needed | Unbounded label cardinality; Prometheus memory explosion | Never — always map OIDs to stable human-readable names |
| Store mutable objects in `AsyncLocal` for correlation IDs | Easy context flow | Copy-on-write semantics cause child tasks to see stale IDs; mutations leak across execution contexts | Never — store only immutable value types (strings, GUIDs) in `AsyncLocal` |
| Skip `sysUpTime` polling to save one OID per device | Fewer poll round trips | Counter resets on reboot generate false spikes with no way to detect them | Never — `sysUpTime` is cheap and mandatory |
| Use `ForeachAwaitPublisher` (MediatR default) for metric dispatch | Zero config | One failing handler silently drops all downstream metric exports | Acceptable in early dev before multi-handler integration |
| Dispose `MeterProvider` at process exit without explicit flush | Relies on GC | Last metric batch before shutdown lost; counters appear to reset on next start | Never — explicitly call `ForceFlush` then dispose in `IHostedService.StopAsync` |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| SharpSnmpLib trap listener | Using `SnmpEngine` default thread pool without pre-sizing | Call `ThreadPool.SetMinThreads` before `SnmpEngine.Start()` per SharpSnmpLib docs |
| OTel → Prometheus (OTLP push) | Prometheus `prometheus-pushgateway` never forgets pushed series — stale device metrics linger forever after a device is decommissioned | Use OTLP native ingestion (Prometheus 2.47+ with `--enable-feature=otlp-write-receiver`) or manage stale series via delete API |
| OTel + Prometheus pull exporter | Prometheus exporter caches scrape responses for 300 seconds by default — polling changes are invisible for 5 minutes | Configure `ResponseCacheDurationMilliseconds` to match scrape interval; or use OTLP push to avoid this entirely |
| OTel temporality | OTLP defaults to cumulative; Prometheus is cumulative-only, but some OTel instruments default to delta | Explicitly set `AggregationTemporality.Cumulative` on all SNMP counter instruments; never mix delta/cumulative for the same metric |
| Quartz + SNMP timeout | Default SNMP timeout (5s) × concurrent jobs can exhaust Quartz thread pool before jobs signal completion | Set `maxConcurrency` in Quartz to at least `(device_count × polls_per_minute × snmp_timeout_seconds) / 60`; tune per environment |
| Leader election + OTel export | Multiple instances all export the same device metrics → Prometheus sees duplicate series with timestamp conflicts | Gate `MeterProvider` registration behind leader check; only the current leader activates metric instruments; followers run in standby mode |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Linear OID lookup in poll handler | CPU spikes every poll cycle; latency grows proportional to OID map size | Use `Dictionary<string, OidMetadata>` with string keys — O(1) lookup; never iterate a list to match OIDs | Noticeable at ~200 OIDs; painful at 500+ |
| Creating new OTel instruments per poll cycle | Memory growth; `InstrumentationScope` warnings; OTel SDK overhead | Instruments must be created once and reused as static/singleton instances — creation is expensive | Immediately visible at any scale |
| Quartz job scheduling 500+ individual device jobs | Scheduler startup latency; `IScheduler.Start()` blocks for seconds | Group devices by poll interval and use a single job that fans out to devices, or use `DisallowConcurrentExecution` + a device queue | At ~100 jobs with short intervals |
| Blocking async SNMP calls in Quartz job without timeout | Job hangs forever if device is unreachable; thread pool thread held indefinitely | Always wrap SNMP `Get`/`GetBulk` in a `CancellationToken` with timeout ≤ configured poll interval; propagate `context.CancellationToken` from Quartz into SNMP call | First unreachable device in production |
| `AsyncLocal` with mutable correlation state across thread boundaries | Correlation ID bleeds between device poll contexts; logs from device A appear under device B's correlation | Store only `string` or `Guid` (immutable) in `AsyncLocal`; reassign (don't mutate) to get copy-on-write isolation | In thread pool under concurrent polls |

---

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Hardcoding SNMP community strings in `appsettings.json` | Community strings checked into source control; read access to all monitored device MIBs | Use .NET secrets manager or environment variables; rotate community strings; prefer SNMPv3 with authPriv |
| Storing SNMPv3 auth/privacy keys in plain text config | Key compromise = full read/write access to device SNMP tree | Use .NET Data Protection API or a secrets vault for USM credentials |
| Exposing Prometheus scrape endpoint without authentication | Internal metric data (device names, IPs, OIDs) leakable to any internal requester | Gate the `/metrics` endpoint behind at minimum network-level ACL; use bearer token auth with the Prometheus `BasicAuthMiddleware` pattern |
| Accepting all trap source IPs without validation | Malicious actors can inject fake traps, triggering false alerts or flooding the trap listener | Maintain an allowlist of device IP ranges; validate source IP in the trap handler before dispatching |
| Quartz admin UI exposed to broad network | Arbitrary job creation = remote code execution (documented in Quartz best practices) | Disable the Quartz admin UI entirely or restrict to localhost; never expose to external networks |

---

## "Looks Done But Isn't" Checklist

- [ ] **Counter delta engine:** Tested with Counter32 wrap-around simulation (inject value = `uint.MaxValue - 10`, next poll = 5) — verify delta is `16` not a spike
- [ ] **Counter reset on reboot:** Tested by injecting a decreasing `sysUpTime` — verify the cycle is discarded and cache re-seeded
- [ ] **Trap listener:** Tested under concurrent trap burst (100 traps/second) — verify no race conditions in handler dispatch
- [ ] **OTel flush on shutdown:** Verified that the last metric batch before process exit reaches Prometheus — check OTLP receiver logs for the final push
- [ ] **Quartz job cancellation:** Verified that SNMP poll jobs honor `context.CancellationToken` when the host shuts down — no hung threads blocking shutdown
- [ ] **MediatR handler isolation:** Verified that a failure in one notification handler does not prevent other handlers from executing
- [ ] **Cardinality limits:** Counted unique label value combinations for the target device fleet — confirmed below OTel default 2,000 limit per instrument
- [ ] **SNMPv3 time window:** Tested with a simulated 151-second clock skew — verify trap is rejected AND the rejection is logged explicitly
- [ ] **Leader election metric gating:** Verified that only the leader instance exports metrics — confirmed no duplicate series appear in Prometheus under two-instance test

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Cardinality explosion already in Prometheus | HIGH | Delete affected series via Prometheus admin API (`/api/v1/admin/tsdb/delete_series`); redeploy with corrected label set; accept data gap during migration |
| OID map performance degradation at scale | LOW | Replace list scan with `Dictionary` lookup — isolated to OID resolution layer; no schema migration needed |
| MediatR handler failure silently dropping metrics | MEDIUM | Switch publisher strategy and add per-handler catch wrappers; no data model change needed but requires testing all handler interaction paths |
| Incorrect counter delta formula producing bad historical data | HIGH | Historical data in Prometheus cannot be corrected retroactively; fix the formula, accept bad data in historical graphs, document the known-bad window |
| SharpSnmpLib `ObjectStore` race condition | MEDIUM | Replace with concurrent-safe structure; requires load testing to validate fix; no external API changes |
| SNMPv3 time window silent drops | LOW | Add NTP validation step to device onboarding checklist; add metric for rejected traps; no code rewrite needed |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Counter32/64 wrap-around miscomputation | Counter delta engine implementation | Unit test: inject wrap-around values, assert correct delta |
| Device reboot false counter reset | Counter delta engine implementation | Unit test: inject decreasing `sysUpTime`, assert cycle discarded |
| OTel cardinality explosion | OTel metric model design (before first instrument created) | Count unique label combinations; review with Prometheus admin |
| MediatR notification handler failure blocking | MediatR pipeline design | Integration test: failing handler + assert other handlers still fire |
| SharpSnmpLib `ObjectStore` thread safety | Trap listener implementation | Load test: 100 concurrent traps, assert no exceptions |
| SNMPv3 USM time window | Trap listener implementation | Test with drifted clock, assert rejection is logged |
| Quartz thread pool starvation | Scheduling infrastructure phase | Load test: max device count × poll frequency × SNMP timeout |
| OTel flush on shutdown | OTel/Quartz integration phase | Kill process mid-cycle, verify last batch in Prometheus |
| `AsyncLocal` correlation ID bleed | Polling engine implementation | Concurrent device poll test, verify per-device log correlation |
| Duplicate metrics from multiple instances | Leader election phase | Two-instance test, verify Prometheus shows exactly one series set |
| OID map linear scan performance | OID resolution implementation | Benchmark: 500 OID lookups per second sustained, assert < 1ms p99 |
| SNMP poll job timeout / hung thread | Scheduling infrastructure phase | Test with unreachable device, verify job cancels within poll interval |

---

## Sources

- Cisco SNMP Counter FAQ: [Consider SNMP Counters: Frequently Asked Questions](https://www.cisco.com/c/en/us/support/docs/ip/simple-network-management-protocol-snmp/26007-faq-snmpcounter.html) — HIGH confidence (official Cisco documentation)
- RFC 3414 USM time window: [RFC 3414 - User-based Security Model for SNMPv3](https://datatracker.ietf.org/doc/html/rfc3414) — HIGH confidence (IETF RFC)
- Netdata counter wrap issue: [incremental chart algorithm doesn't handle counter wrap properly](https://github.com/netdata/netdata/issues/4533) — MEDIUM confidence (production post-mortem)
- sysUpTime reboot detection: [Catch Unexpected Reboots Through Monitoring sysUpTimeInstance](https://packetpushers.net/blog/catch-unexpected-reboots-through-monitoring-sysuptimeinstance/) — MEDIUM confidence (verified against SNMP RFC)
- OTel .NET metrics best practices: [Best practices — OpenTelemetry .NET](https://opentelemetry.io/docs/languages/dotnet/metrics/best-practices/) — HIGH confidence (official OTel documentation)
- OTel graceful shutdown discussion: [Graceful shutdown and forcing exporter to push — opentelemetry-dotnet](https://github.com/open-telemetry/opentelemetry-dotnet/discussions/3614) — HIGH confidence (maintainer response)
- OTel cardinality limit: OTel SDK default of 2,000 per metric confirmed in official docs above — HIGH confidence
- MediatR notification publisher pitfall: [How To Publish MediatR Notifications In Parallel — Milan Jovanovic](https://www.milanjovanovic.tech/blog/how-to-publish-mediatr-notifications-in-parallel) — MEDIUM confidence (verified against MediatR v12 changelog)
- MediatR parallel publisher EF Core warning: Official article warning about shared DI scope — MEDIUM confidence
- SharpSnmpLib ObjectStore thread safety: [Agent Development — C# SNMP Library](https://docs.sharpsnmp.com/samples/agent-development.html) (redirects to lextudio.com) — MEDIUM confidence (library official docs, documented limitation)
- SharpSnmpLib time window issue: [SharpSnmpLib GitHub issue #83](https://github.com/lextudio/sharpsnmplib/issues/83) — MEDIUM confidence (library issue tracker)
- Quartz.NET best practices: [Quartz.NET Best Practices](https://www.quartz-scheduler.net/documentation/best-practices.html) — HIGH confidence (official Quartz documentation)
- Quartz hung job issue: [Scheduler hangs without reporting exception — quartznet #800](https://github.com/quartznet/quartznet/issues/800) — MEDIUM confidence (library issue tracker)
- Prometheus leader election duplicate metrics: [Prometheus Operator with Leader Election](https://medium.com/yotpoengineering/prometheus-operator-with-leader-election-solving-duplicate-remote-write-metrics-in-ha-setup-8b6581d10b45) — LOW confidence (single source, community post)
- AsyncLocal pitfalls: [Conveying Context with AsyncLocal](https://medium.com/@norm.bryar/conveying-context-with-asynclocal-91fa474a5b42), [Hidden Workings of Execution Context in .NET](https://medium.com/net-under-the-hood/hidden-workings-of-execution-context-in-net-43b491726c65) — MEDIUM confidence (multiple consistent sources)
- SNMP false spikes on reboot: [Zabbix Forums — SNMP Counter reset breaks delta item](https://www.zabbix.com/forum/zabbix-help/32899-snmp-counter-reset-breaks-delta-item), [Icinga community post](https://community.icinga.com/t/network-interface-traffic-via-snmp-shows-spikes-on-reboot/11605) — MEDIUM confidence (multiple monitoring platform communities confirm the pattern)

---
*Pitfalls research for: SNMP monitoring system — .NET 9 / MediatR / Quartz.NET / OTel*
*Researched: 2026-03-04*
