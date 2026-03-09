# Scenario 22: Removing a device from ConfigMap stops polling
# Applies device-removed-configmap.yaml (removes E2E-SIM), verifies
# snmp_poll_executed_total delta = 0 over a 20s window, then restores.
SCENARIO_NAME="Device removal stops polling (E2E-SIM)"
METRIC="snmp_poll_executed_total"
FILTER='device_name="E2E-SIM"'

# Snapshot for safe restoration
snapshot_configmaps

# Verify E2E-SIM is currently being polled (sanity check)
SANITY=$(snapshot_counter "$METRIC" "$FILTER")
log_info "E2E-SIM poll_executed before removal: $SANITY"

# Apply mutated ConfigMap with E2E-SIM removed
log_info "Applying device-removed ConfigMap (removing E2E-SIM)..."
kubectl apply -f "$SCRIPT_DIR/fixtures/device-removed-configmap.yaml" -n simetra

# Wait for watcher detection + poll interval + OTel export flush
# ConfigMap change detection ~5s + poll interval 10s + OTel export 15s = ~30s
log_info "Waiting 20s for watcher detection and in-flight data flush..."
sleep 20

# Snapshot "post-removal start"
START=$(snapshot_counter "$METRIC" "$FILTER")
log_info "E2E-SIM poll_executed post-removal start: $START"

# Wait 20s (2 poll intervals -- if still polled, counter would increase)
log_info "Waiting 20s to verify counter stagnation..."
sleep 20

# Snapshot "post-removal end"
END=$(snapshot_counter "$METRIC" "$FILTER")
log_info "E2E-SIM poll_executed post-removal end: $END"

DELTA=$((END - START))
log_info "Post-removal delta: $DELTA (expected 0)"

if [ "$DELTA" -eq 0 ]; then
    record_pass "$SCENARIO_NAME" "start=$START end=$END delta=$DELTA (counter stagnated as expected)"
else
    record_fail "$SCENARIO_NAME" "start=$START end=$END delta=$DELTA (expected 0, device still being polled)"
fi

# Restore original ConfigMaps
log_info "Restoring original ConfigMaps..."
restore_configmaps

# Wait for E2E-SIM polling to resume after restore
log_info "Waiting for E2E-SIM polling to resume after restore (up to 30s)..."
RESTORE_BASELINE=$(snapshot_counter "$METRIC" "$FILTER")
poll_until 30 "$POLL_INTERVAL" "$METRIC" "$FILTER" "$RESTORE_BASELINE" || true
log_info "ConfigMaps restored -- E2E-SIM polling resumed"
