# Scenario 21: Adding a device to ConfigMap starts polling within 60s
# Applies device-added-configmap.yaml (adds E2E-SIM-2), verifies
# snmp_poll_executed_total increments for the new device, then restores.
SCENARIO_NAME="Device addition starts polling (E2E-SIM-2)"
METRIC="snmp_poll_executed_total"
FILTER='device_name="E2E-SIM-2"'

# Snapshot for safe restoration
snapshot_configmaps

# Apply mutated ConfigMap with E2E-SIM-2 added
log_info "Applying device-added ConfigMap (adding E2E-SIM-2)..."
kubectl apply -f "$SCRIPT_DIR/fixtures/device-added-configmap.yaml" -n simetra

# Wait for DeviceWatcherService to detect change and DynamicPollScheduler to reconcile
log_info "Waiting 5s for watcher detection and scheduler reconciliation..."
sleep 5

# Snapshot counter -- should be 0 for the new device
BEFORE=$(snapshot_counter "$METRIC" "$FILTER")
log_info "E2E-SIM-2 poll_executed before: $BEFORE"

# Poll until counter increments (up to 60s for detection + poll + OTel export)
log_info "Waiting for E2E-SIM-2 poll_executed to increment (up to 60s)..."
poll_until 60 "$POLL_INTERVAL" "$METRIC" "$FILTER" "$BEFORE" || true

AFTER=$(snapshot_counter "$METRIC" "$FILTER")
DELTA=$((AFTER - BEFORE))
EVIDENCE=$(get_evidence "$METRIC" "$FILTER")
assert_delta_gt "$DELTA" 0 "$SCENARIO_NAME" "before=$BEFORE after=$AFTER delta=$DELTA | $EVIDENCE"

# Restore original ConfigMaps
log_info "Restoring original ConfigMaps..."
restore_configmaps
log_info "ConfigMaps restored -- E2E-SIM-2 removed, cluster back to original state"
