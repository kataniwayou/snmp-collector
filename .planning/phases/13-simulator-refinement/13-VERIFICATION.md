---
phase: 13-simulator-refinement
verified: 2026-03-07T20:00:00Z
status: passed
score: 5/5 must-haves verified
---

# Phase 13: Simulator Refinement Verification Report

**Phase Goal:** Both OBP and NPB simulators respond with realistic OID subsets matching the populated OID maps and send appropriate trap types
**Verified:** 2026-03-07T20:00:00Z
**Status:** passed
**Re-verification:** No -- initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | OBP simulator responds to SNMP GET for all 4-link OIDs | VERIFIED | Simulator registers 24 OIDs via loop range(1,5) x [1,4,10,11,12,13] -- exact 1:1 match with oidmap-obp.json (diff confirmed empty). Power values use random walk with distinct baselines per link (-50 to -200 range). |
| 2 | OBP simulator sends StateChange traps for all 4 links | VERIFIED | per_link_trap_loop created for range(1,5) = links 1-4. Trap OID = BYPASS_PREFIX.{link}.3.50.2. Varbind uses channel poll OID with Integer32 value. Channel toggles 0/1 before trap send. |
| 3 | NPB simulator responds to SNMP GET for all 8-port OIDs | VERIFIED | Registers 68 OIDs: 4 system (OctetString) + 64 per-port (Integer32 + Counter64). Exact 1:1 match with oidmap-npb.json. Counter64 increments use traffic profiles. System health float random walk as OctetString. |
| 4 | NPB simulator sends link up/down traps | VERIFIED | per_port_trap_loop for TRAP_PORTS = [1,2,3,5,6,7]. P4/P8 permanently down. Trap OID = NPB_PREFIX.3.{port}.0. Varbind = (port_status_oid, Integer32(new_status)). |
| 5 | Both simulators use Simetra.{DeviceName} community string | VERIFIED | OBP defaults to Simetra.OBP-01, NPB to Simetra.NPB-01. K8s probes use CommunityData with matching strings. |

**Score:** 5/5 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| simulators/obp/obp_simulator.py | OBP SNMP agent, 24 OIDs | VERIFIED (320 lines) | Substantive, no stubs/TODOs. DynamicInstance callback. Supervised async tasks. |
| simulators/npb/npb_simulator.py | NPB SNMP agent, 68 OIDs | VERIFIED (463 lines) | Substantive, no stubs/TODOs. Traffic profiles. OctetString system health. |
| deploy/k8s/simulators/obp-deployment.yaml | K8s deployment | VERIFIED (114 lines) | Pysnmp liveness/readiness probes using Simetra.OBP-01. DEVICE_NAME env var. |
| deploy/k8s/simulators/npb-deployment.yaml | K8s deployment | VERIFIED (114 lines) | Pysnmp liveness/readiness probes using Simetra.NPB-01. DEVICE_NAME env var. |
| deploy/k8s/simulators/configmap-devices.yaml | Device config | VERIFIED (125 lines) | NPB-01 and OBP-01 entries. Correct OID trees. Template placeholders preserved. |
| src/SnmpCollector/config/oidmap-obp.json | 24-entry OID map | VERIFIED | 4 links x 6 metrics. Exact match with simulator. |
| src/SnmpCollector/config/oidmap-npb.json | 68-entry OID map | VERIFIED | 4 system + 8x8 per-port. Exact match with simulator. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| OBP sim OID registration | oidmap-obp.json | OID string pattern | WIRED | 24 vs 24 diff = empty |
| NPB sim OID registration | oidmap-npb.json | OID string pattern | WIRED | 68 vs 68 diff = empty |
| OBP trap varbind | Channel poll OID | CHANNEL_POLL_OIDS lookup | WIRED | Uses channel OID not trap OID |
| NPB trap varbind | Port status OID | NPB_PREFIX.2.{port}.1.0 | WIRED | Uses port_status poll OID |
| K8s health probes | Community string | CommunityData | WIRED | Correct per-device strings |
| K8s configmap | OID maps | OID literals | WIRED | ConfigMap OIDs match map |

### Requirements Coverage

| Requirement | Status | Details |
|-------------|--------|---------|
| SIM-01: OBP 4-link realistic OID subset | SATISFIED | 24 OIDs, power random walk, distinct baselines |
| SIM-02: OBP StateChange traps for all 4 links | SATISFIED | Trap loops links 1-4, correct OID and varbind |
| SIM-03: NPB realistic OID subset across 8 ports | SATISFIED | 68 OIDs, traffic profiles, Counter64 wrapping |
| SIM-04: NPB link up/down traps | SATISFIED | portLinkChange for 6 active ports |
| SIM-05: Simetra.{DeviceName} community string | SATISFIED | Configurable via env var, defaults correct |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| obp_simulator.py | 198 | return [] | Info | Legitimate DNS failure handling |
| npb_simulator.py | 262 | return [] | Info | Same DNS failure handling |

No TODOs, FIXMEs, placeholders, or stub patterns found.

### Human Verification Required

#### 1. OBP SNMP GET Response

**Test:** Run snmpget -v2c -c Simetra.OBP-01 simulator-ip 1.3.6.1.4.1.47477.10.21.1.3.10.0
**Expected:** Integer32 around -85, changing due to random walk.
**Why human:** Needs live simulator instance.

#### 2. NPB Counter64 Incrementing

**Test:** Run snmpget twice 15s apart on 1.3.6.1.4.1.47477.100.2.1.2.0 (P1 rx_octets).
**Expected:** Counter64 increases by 500K-2M per 10s interval.
**Why human:** Requires running simulator over time.

#### 3. Trap Receipt

**Test:** Start trap receiver, launch simulators, wait 1-5 minutes.
**Expected:** OBP StateChange traps with channel varbind; NPB portLinkChange traps with status varbind.
**Why human:** Timing-dependent live behavior.

### Gaps Summary

No gaps found. All 5 observable truths verified through structural code analysis:

- OBP simulator serves exactly 24 OIDs matching oidmap-obp.json 1:1, with power random walk on 16 R1-R4 OIDs and StateChange traps for all 4 links.
- NPB simulator serves exactly 68 OIDs matching oidmap-npb.json 1:1, with traffic-profiled Counter64 increments, OctetString system health, and portLinkChange traps for 6 active ports.
- Both simulators default to Simetra.{DEVICE_NAME} community string, K8s deployments and health probes aligned.

The configmap-devices.yaml contains only representative MetricPoll entries (template for Phase 14) -- full config is Phase 14 scope.

---

_Verified: 2026-03-07T20:00:00Z_
_Verifier: Claude (gsd-verifier)_
