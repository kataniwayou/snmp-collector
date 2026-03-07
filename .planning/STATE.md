# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-07)

**Core value:** Every SNMP OID — from a trap or a poll — gets resolved, typed correctly, and pushed to Prometheus where it's queryable in Grafana within seconds.
**Current focus:** v1.0 milestone complete. Planning next milestone.

## Current Position

Phase: v1.0 complete (10 phases, 48 plans, 16 quick tasks)
Status: Between milestones
Last activity: 2026-03-07 — v1.0 milestone archived

## Milestone History

| Milestone | Phases | Plans | Shipped |
|-----------|--------|-------|---------|
| v1.0 Foundation | 1-10 | 48 | 2026-03-07 |

See `.planning/MILESTONES.md` for details.
See `.planning/milestones/` for archived roadmaps and requirements.

## Accumulated Context

### Key Architectural Facts

- MediatR 12.5.0 (MIT) — do NOT upgrade to v13+ (RPL-1.5 license)
- Two-meter architecture: MeterName for all instances, LeaderMeterName for leader only
- Community string convention: Simetra.{DeviceName} for both auth and device identity
- host_name from NODE_NAME env var (K8s spec.nodeName), pod_name from HOSTNAME
- Heartbeat is internal infrastructure — pipeline metrics prove liveness, no metric export

### Known Tech Debt

- IDeviceRegistry.TryGetDevice(IPAddress) orphaned — community string replaced IP lookup
- PollSchedulerStartupService thread pool log off-by-one (HeartbeatJob not counted)

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-07
Stopped at: v1.0 milestone completion
Resume file: None
