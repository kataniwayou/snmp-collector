"""
OBP Bypass SNMP Simulator

Simulates a 4-link CGS OBP optical bypass device for testing Simetra's SNMP
trap and poll pipeline. Serves exactly 24 poll OIDs (4 links x 6 metrics each)
matching oidmap-obp.json 1:1, with power random walk on R1-R4 receivers and
StateChange traps for all 4 links.

OID tree: 1.3.6.1.4.1.47477.10.21.{link}.3.{suffix}.0
  link   = 1..4
  suffix = 1 (link_state), 4 (channel), 10-13 (r1-r4 power)

Community string: Simetra.{DEVICE_NAME} (default: Simetra.OBP-01)
Rejects requests with any other community string.

Environment variables:
  DEVICE_NAME       (default: OBP-01)
  COMMUNITY         (default: Simetra.{DEVICE_NAME})
  TRAP_TARGET       (default: host.docker.internal)
  TRAP_PORT         (default: 162)
  TRAP_INTERVAL_MIN (default: 60)   minimum seconds between per-link traps
  TRAP_INTERVAL_MAX (default: 300)  maximum seconds between per-link traps
"""

import asyncio
import logging
import os
import random
import signal
import socket

from pysnmp.entity import engine, config
from pysnmp.entity.rfc3413 import cmdrsp, context
from pysnmp.carrier.asyncio.dgram import udp
from pysnmp.proto.api import v2c
from pysnmp.hlapi.v3arch.asyncio import (
    SnmpEngine as HlapiEngine,
    CommunityData,
    UdpTransportTarget,
    ContextData,
    send_notification,
    NotificationType,
    ObjectIdentity,
)

logging.basicConfig(level=logging.INFO, format="%(asctime)s [OBP-SIM] %(message)s")
log = logging.getLogger("obp-simulator")

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

DEVICE_NAME = os.environ.get("DEVICE_NAME", "OBP-01")
COMMUNITY = os.environ.get("COMMUNITY", f"Simetra.{DEVICE_NAME}")

TRAP_TARGET = os.environ.get("TRAP_TARGET", "host.docker.internal")
TRAP_PORT = int(os.environ.get("TRAP_PORT", "162"))
TRAP_INTERVAL_MIN = int(os.environ.get("TRAP_INTERVAL_MIN", "60"))
TRAP_INTERVAL_MAX = int(os.environ.get("TRAP_INTERVAL_MAX", "300"))
STARTUP_DELAY = 12  # seconds before first trap

BYPASS_PREFIX = "1.3.6.1.4.1.47477.10.21"
POWER_UPDATE_INTERVAL = 10  # seconds between power random walk updates

# ---------------------------------------------------------------------------
# OID metric definitions -- 6 metrics per link = 24 OIDs total
# ---------------------------------------------------------------------------

LINK_METRICS = [
    (1,  "link_state", v2c.Integer32),
    (4,  "channel",    v2c.Integer32),
    (10, "r1_power",   v2c.Integer32),
    (11, "r2_power",   v2c.Integer32),
    (12, "r3_power",   v2c.Integer32),
    (13, "r4_power",   v2c.Integer32),
]

# StateChange trap OID per link: BYPASS_PREFIX.{N}.3.50.2
LINK_STATE_CHANGE_OIDS = {
    link: f"{BYPASS_PREFIX}.{link}.3.50.2" for link in range(1, 5)
}

# Channel poll OID per link (used as trap varbind): BYPASS_PREFIX.{N}.3.4.0
CHANNEL_POLL_OIDS = {
    link: f"{BYPASS_PREFIX}.{link}.3.4.0" for link in range(1, 5)
}

# ---------------------------------------------------------------------------
# Mutable link state -- each link has distinct power baselines
# ---------------------------------------------------------------------------

link_states = {
    1: {"link_state": 1, "channel": 1, "r1_power": -85,  "r2_power": -92,  "r3_power": -88,  "r4_power": -95},
    2: {"link_state": 1, "channel": 1, "r1_power": -78,  "r2_power": -82,  "r3_power": -90,  "r4_power": -87},
    3: {"link_state": 1, "channel": 0, "r1_power": -110, "r2_power": -105, "r3_power": -115, "r4_power": -108},
    4: {"link_state": 0, "channel": 1, "r1_power": -130, "r2_power": -140, "r3_power": -125, "r4_power": -135},
}

# ---------------------------------------------------------------------------
# SNMP engine setup
# ---------------------------------------------------------------------------

snmpEngine = engine.SnmpEngine()
config.add_transport(
    snmpEngine,
    udp.DOMAIN_NAME,
    udp.UdpTransport().open_server_mode(("0.0.0.0", 161)),
)
config.add_v1_system(snmpEngine, "my-area", COMMUNITY)
config.add_vacm_user(snmpEngine, 2, "my-area", "noAuthNoPriv", (1, 3, 6, 1, 4, 1))
snmpContext = context.SnmpContext(snmpEngine)
cmdrsp.GetCommandResponder(snmpEngine, snmpContext)
cmdrsp.NextCommandResponder(snmpEngine, snmpContext)
cmdrsp.BulkCommandResponder(snmpEngine, snmpContext)
cmdrsp.SetCommandResponder(snmpEngine, snmpContext)

mibBuilder = snmpContext.get_mib_instrum().get_mib_builder()
MibScalar, MibScalarInstance = mibBuilder.import_symbols(
    "SNMPv2-SMI", "MibScalar", "MibScalarInstance"
)


class DynamicInstance(MibScalarInstance):
    """MibScalarInstance that returns a live value via a callback function."""

    def __init__(self, oid_tuple, index_tuple, syntax, get_value_fn):
        super().__init__(oid_tuple, index_tuple, syntax)
        self._get_value_fn = get_value_fn

    def getValue(self, name, **ctx):
        return self.getSyntax().clone(self._get_value_fn())


def oid_str_to_tuple(oid_str):
    """Convert dotted OID string to integer tuple, stripping leading dot."""
    return tuple(int(x) for x in oid_str.strip(".").split("."))


# ---------------------------------------------------------------------------
# OID registration: 24 OIDs (4 links x 6 metrics)
# ---------------------------------------------------------------------------

symbols = {}
registered_oids = []

for link_num in range(1, 5):
    for suffix, state_key, syntax_cls in LINK_METRICS:
        oid_str = f"{BYPASS_PREFIX}.{link_num}.3.{suffix}"
        oid_tuple = oid_str_to_tuple(oid_str)

        def make_getter(ln=link_num, k=state_key):
            return lambda: link_states[ln][k]

        symbols[f"scalar_{link_num}_{suffix}"] = MibScalar(oid_tuple, syntax_cls())
        symbols[f"instance_{link_num}_{suffix}"] = DynamicInstance(
            oid_tuple, (0,), syntax_cls(), make_getter()
        )
        registered_oids.append(f"{oid_str}.0")

mibBuilder.export_symbols("__OBP-SIM-MIB", **symbols)

# ---------------------------------------------------------------------------
# Helper functions
# ---------------------------------------------------------------------------


def random_walk_int(current, step, low, high):
    """Integer random walk, clamped to [low, high]."""
    delta = random.randint(-step, step)
    return max(low, min(high, current + delta))


async def supervised_task(name, coro_fn):
    """Run an async task forever, restarting on unhandled exceptions."""
    backoff = 5
    max_backoff = 300
    while True:
        try:
            await coro_fn()
        except asyncio.CancelledError:
            log.info("Task '%s' cancelled -- shutting down", name)
            raise
        except Exception:
            log.exception("Task '%s' crashed -- restarting in %ds", name, backoff)
            await asyncio.sleep(backoff)
            backoff = min(backoff * 2, max_backoff)
        else:
            log.warning("Task '%s' exited normally (unexpected) -- restarting", name)


async def resolve_trap_targets(hostname):
    """Resolve hostname via DNS and return deduplicated list of IPv4 addresses."""
    try:
        results = socket.getaddrinfo(hostname, None, socket.AF_INET)
        return list({addr[4][0] for addr in results})
    except socket.gaierror as exc:
        log.warning("DNS resolution failed for %s: %s", hostname, exc)
        return []


# ---------------------------------------------------------------------------
# Background tasks
# ---------------------------------------------------------------------------

POWER_KEYS = ["r1_power", "r2_power", "r3_power", "r4_power"]


async def update_power_values():
    """Random walk all 16 power OIDs every POWER_UPDATE_INTERVAL seconds."""
    while True:
        await asyncio.sleep(POWER_UPDATE_INTERVAL)
        for link_num in range(1, 5):
            state = link_states[link_num]
            for key in POWER_KEYS:
                state[key] = random_walk_int(state[key], step=2, low=-200, high=-50)
        log.info(
            "Power walk: L1=[%d,%d,%d,%d] L2=[%d,%d,%d,%d] L3=[%d,%d,%d,%d] L4=[%d,%d,%d,%d]",
            *(link_states[ln][k] for ln in range(1, 5) for k in POWER_KEYS),
        )


hlapi_engine = HlapiEngine()


async def send_state_change_trap(link_num, channel_value):
    """Send StateChange trap for the given link with channel as varbind value."""
    trap_oid = LINK_STATE_CHANGE_OIDS[link_num]
    channel_poll_oid = CHANNEL_POLL_OIDS[link_num]
    target_ips = await resolve_trap_targets(TRAP_TARGET)
    if not target_ips:
        return
    for target_ip in target_ips:
        try:
            target = await UdpTransportTarget.create((target_ip, TRAP_PORT))
            await send_notification(
                hlapi_engine,
                CommunityData(COMMUNITY),
                target,
                ContextData(),
                "trap",
                NotificationType(ObjectIdentity(trap_oid)).add_varbinds(
                    (channel_poll_oid, v2c.Integer32(channel_value)),
                ),
            )
            log.info(
                "StateChange trap link=%d channel=%d (%s) -> %s:%d",
                link_num, channel_value,
                "Primary" if channel_value == 1 else "Bypass",
                target_ip, TRAP_PORT,
            )
        except Exception as exc:
            log.error("Trap send failed link=%d: %s", link_num, exc)


async def per_link_trap_loop(link_num):
    """Independent trap loop for one link. Toggles channel and sends StateChange trap."""
    await asyncio.sleep(STARTUP_DELAY)
    while True:
        await asyncio.sleep(random.uniform(TRAP_INTERVAL_MIN, TRAP_INTERVAL_MAX))

        state = link_states[link_num]
        old_channel = state["channel"]
        new_channel = 0 if old_channel == 1 else 1

        # STATE FIRST: mutate poll state before sending trap
        state["channel"] = new_channel
        log.info("Link %d channel toggle: %d -> %d", link_num, old_channel, new_channel)

        await send_state_change_trap(link_num, new_channel)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------


def main():
    log.info("OBP Bypass Simulator starting (24 OIDs, 4-link StateChange traps)...")
    log.info("PID: %d", os.getpid())
    log.info("Community string: %s", COMMUNITY)
    log.info(
        "Configuration: TRAP_TARGET=%s TRAP_PORT=%d "
        "TRAP_INTERVAL_MIN=%ds TRAP_INTERVAL_MAX=%ds STARTUP_DELAY=%ds "
        "POWER_UPDATE_INTERVAL=%ds",
        TRAP_TARGET, TRAP_PORT, TRAP_INTERVAL_MIN, TRAP_INTERVAL_MAX,
        STARTUP_DELAY, POWER_UPDATE_INTERVAL,
    )
    log.info("Registered %d poll OIDs:", len(registered_oids))
    for oid in registered_oids:
        log.info("  %s", oid)
    log.info("Trap OIDs: %s", list(LINK_STATE_CHANGE_OIDS.values()))
    log.info("Initial link states: %s", link_states)

    loop = asyncio.get_event_loop()

    tasks = [
        loop.create_task(supervised_task("update_power_values", update_power_values)),
    ]
    tasks.extend(
        loop.create_task(supervised_task(f"per_link_trap_loop_{n}", lambda n=n: per_link_trap_loop(n)))
        for n in range(1, 5)
    )

    def _shutdown(sig_name):
        log.info("Received %s -- shutting down gracefully", sig_name)
        for t in tasks:
            t.cancel()
        snmpEngine.close_dispatcher()

    for sig in (signal.SIGTERM, signal.SIGINT):
        try:
            loop.add_signal_handler(sig, _shutdown, sig.name)
        except NotImplementedError:
            log.warning("Signal handler for %s not supported on this platform", sig.name)

    log.info("SNMP agent listening on 0.0.0.0:161 (community: %s)", COMMUNITY)
    snmpEngine.open_dispatcher()


main()
