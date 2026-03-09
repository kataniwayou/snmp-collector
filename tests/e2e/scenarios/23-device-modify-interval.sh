# Scenario 23: Modifying device poll interval changes collection frequency
# Measures poll_executed delta at 10s interval (baseline), then applies
# device-modified-interval-configmap.yaml (5s interval), measures again,
# and verifies the faster interval produces more polls.
SCENARIO_NAME="Device interval modification changes poll frequency (E2E-SIM)"
METRIC="snmp_poll_executed_total"
FILTER='device_name="E2E-SIM"'

# Snapshot for safe restoration
snapshot_configmaps

# --- Baseline measurement (10s interval) ---
log_info "Measuring baseline poll rate at 10s interval over 30s window..."
BASELINE_START=$(snapshot_counter "$METRIC" "$FILTER")
sleep 30
BASELINE_END=$(snapshot_counter "$METRIC" "$FILTER")
DELTA_BEFORE=$((BASELINE_END - BASELINE_START))
log_info "Baseline delta (10s interval, 30s window): $DELTA_BEFORE"

# Apply mutated ConfigMap with 5s interval for E2E-SIM
log_info "Applying device-modified-interval ConfigMap (E2E-SIM interval 10s -> 5s)..."
kubectl apply -f "$SCRIPT_DIR/fixtures/device-modified-interval-configmap.yaml" -n simetra

# Wait for reconciliation + OTel buffer flush
log_info "Waiting 20s for watcher detection and OTel buffer flush..."
sleep 20

# --- Fast measurement (5s interval) ---
log_info "Measuring modified poll rate at 5s interval over 30s window..."
FAST_START=$(snapshot_counter "$METRIC" "$FILTER")
sleep 30
FAST_END=$(snapshot_counter "$METRIC" "$FILTER")
DELTA_AFTER=$((FAST_END - FAST_START))
log_info "Modified delta (5s interval, 30s window): $DELTA_AFTER"

# Verify faster interval produces more polls
assert_delta_gt "$DELTA_AFTER" "$DELTA_BEFORE" "$SCENARIO_NAME" "delta_10s=$DELTA_BEFORE delta_5s=$DELTA_AFTER"

# Restore original ConfigMaps
log_info "Restoring original ConfigMaps..."
restore_configmaps
log_info "ConfigMaps restored -- E2E-SIM interval back to 10s"
