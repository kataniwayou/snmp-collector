---
phase: quick-018
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Configuration/HeartbeatJobOptions.cs
  - src/SnmpCollector/Pipeline/SnmpOidReceived.cs
  - src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs
  - src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs
  - src/SnmpCollector/Jobs/HeartbeatJob.cs
  - src/SnmpCollector/Services/ChannelConsumerService.cs
  - tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs
  - tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs
  - tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs
autonomous: true

must_haves:
  truths:
    - "Heartbeat device name string 'heartbeat' is defined once in HeartbeatJobOptions as a const"
    - "OidResolutionBehavior skips OID resolution entirely for heartbeat messages (no 'not found' log)"
    - "OtelMetricHandler uses IsHeartbeat bool flag instead of string comparison"
    - "HeartbeatJob uses HeartbeatJobOptions.HeartbeatDeviceName instead of hardcoded string"
    - "All existing tests pass, new tests cover heartbeat skip and flag behavior"
  artifacts:
    - path: "src/SnmpCollector/Configuration/HeartbeatJobOptions.cs"
      provides: "HeartbeatDeviceName const"
      contains: "HeartbeatDeviceName"
    - path: "src/SnmpCollector/Pipeline/SnmpOidReceived.cs"
      provides: "IsHeartbeat property"
      contains: "IsHeartbeat"
    - path: "src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs"
      provides: "Heartbeat skip logic"
      contains: "IsHeartbeat"
    - path: "src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs"
      provides: "IsHeartbeat-based suppression"
      contains: "IsHeartbeat"
  key_links:
    - from: "src/SnmpCollector/Services/ChannelConsumerService.cs"
      to: "HeartbeatJobOptions.HeartbeatDeviceName"
      via: "string.Equals comparison when constructing SnmpOidReceived"
      pattern: "IsHeartbeat.*HeartbeatDeviceName"
    - from: "src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs"
      to: "SnmpOidReceived.IsHeartbeat"
      via: "early return before Resolve call"
      pattern: "IsHeartbeat"
    - from: "src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs"
      to: "SnmpOidReceived.IsHeartbeat"
      via: "notification.IsHeartbeat check replacing string comparison"
      pattern: "notification\\.IsHeartbeat"
---

<objective>
Add IsHeartbeat boolean flag to SnmpOidReceived so pipeline behaviors can identify heartbeat
traffic without string comparisons. Consolidate the "heartbeat" device name string into a
single const in HeartbeatJobOptions. Update OidResolutionBehavior to skip resolution for
heartbeats (eliminating misleading "not found" log), and OtelMetricHandler to use the flag
instead of string comparison.

Purpose: Eliminates magic string comparisons scattered across the pipeline; heartbeat detection
becomes a first-class boolean property set once at the ingestion boundary.

Output: Modified source files and updated tests; all tests green.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/STATE.md
@src/SnmpCollector/Configuration/HeartbeatJobOptions.cs
@src/SnmpCollector/Pipeline/SnmpOidReceived.cs
@src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs
@src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs
@src/SnmpCollector/Jobs/HeartbeatJob.cs
@src/SnmpCollector/Services/ChannelConsumerService.cs
@tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs
@tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add HeartbeatDeviceName const and IsHeartbeat property</name>
  <files>
    src/SnmpCollector/Configuration/HeartbeatJobOptions.cs
    src/SnmpCollector/Pipeline/SnmpOidReceived.cs
    src/SnmpCollector/Services/ChannelConsumerService.cs
    src/SnmpCollector/Jobs/HeartbeatJob.cs
  </files>
  <action>
    1. In `HeartbeatJobOptions.cs`, add a new const below the existing `HeartbeatOid` const:
       ```csharp
       /// <summary>
       /// Device name used by HeartbeatJob's loopback trap. Single source of truth for the
       /// "heartbeat" string — all comparisons reference this const.
       /// </summary>
       public const string HeartbeatDeviceName = "heartbeat";
       ```

    2. In `SnmpOidReceived.cs`, add a new property after `MetricName`:
       ```csharp
       /// <summary>
       /// True when this message originated from the internal HeartbeatJob loopback trap.
       /// Set at the ingestion boundary (ChannelConsumerService) based on DeviceName matching
       /// <see cref="Configuration.HeartbeatJobOptions.HeartbeatDeviceName"/>.
       /// Behaviors and handlers check this flag instead of comparing DeviceName strings.
       /// </summary>
       public bool IsHeartbeat { get; init; }
       ```
       Use `init` (not `set`) since this is set once at construction and never mutated.

    3. In `ChannelConsumerService.cs`, in the `ExecuteAsync` method where `SnmpOidReceived` is constructed
       (around line 58-66), add `IsHeartbeat` to the object initializer:
       ```csharp
       var msg = new SnmpOidReceived
       {
           Oid        = envelope.Oid,
           AgentIp    = envelope.AgentIp,
           DeviceName = envelope.DeviceName,
           Value      = envelope.Value,
           Source     = SnmpSource.Trap,
           TypeCode   = envelope.TypeCode,
           IsHeartbeat = string.Equals(envelope.DeviceName, HeartbeatJobOptions.HeartbeatDeviceName, StringComparison.OrdinalIgnoreCase),
       };
       ```
       Add `using SnmpCollector.Configuration;` if not already present.

    4. In `HeartbeatJob.cs`, replace the hardcoded `"heartbeat"` string on line 36:
       ```csharp
       // Before:
       _communityString = CommunityStringHelper.DeriveFromDeviceName("heartbeat");
       // After:
       _communityString = CommunityStringHelper.DeriveFromDeviceName(HeartbeatJobOptions.HeartbeatDeviceName);
       ```
  </action>
  <verify>Run `dotnet build src/SnmpCollector/SnmpCollector.csproj` — must compile with zero errors and zero warnings.</verify>
  <done>HeartbeatDeviceName const exists in HeartbeatJobOptions. IsHeartbeat property exists on SnmpOidReceived. ChannelConsumerService sets IsHeartbeat from DeviceName comparison. HeartbeatJob uses the const. No hardcoded "heartbeat" strings remain outside HeartbeatJobOptions.</done>
</task>

<task type="auto">
  <name>Task 2: Update pipeline behaviors to use IsHeartbeat</name>
  <files>
    src/SnmpCollector/Pipeline/Behaviors/OidResolutionBehavior.cs
    src/SnmpCollector/Pipeline/Handlers/OtelMetricHandler.cs
  </files>
  <action>
    1. In `OidResolutionBehavior.cs`, modify the `Handle` method to skip OID resolution for heartbeat
       messages. The behavior must still call `next()` — it never short-circuits. Replace the existing
       `if (notification is SnmpOidReceived msg)` block (lines 33-41) with:
       ```csharp
       if (notification is SnmpOidReceived msg)
       {
           if (msg.IsHeartbeat)
           {
               _logger.LogDebug("Heartbeat message — skipping OID resolution");
           }
           else
           {
               msg.MetricName = _oidMapService.Resolve(msg.Oid);

               if (msg.MetricName == OidMapService.Unknown)
                   _logger.LogDebug("OID {Oid} not found in OidMap", msg.Oid);
               else
                   _logger.LogDebug("OID {Oid} resolved to {MetricName}", msg.Oid, msg.MetricName);
           }
       }
       ```
       This eliminates the misleading "OID not found" log for heartbeat OIDs.

    2. In `OtelMetricHandler.cs`:
       a. Remove the `HeartbeatDeviceName` const (line 37). It is now in HeartbeatJobOptions.
       b. Replace the heartbeat detection logic (lines 43-48) to use the flag:
          ```csharp
          // Internal heartbeat: count as handled for pipeline liveness evidence, skip metric export.
          if (notification.IsHeartbeat)
          {
              _pipelineMetrics.IncrementHandled();
              return Task.FromResult(Unit.Value);
          }
          ```
          This replaces the `string.Equals(deviceName, HeartbeatDeviceName, ...)` check.
  </action>
  <verify>Run `dotnet build src/SnmpCollector/SnmpCollector.csproj` — must compile with zero errors.</verify>
  <done>OidResolutionBehavior skips Resolve() call when IsHeartbeat is true. OtelMetricHandler uses notification.IsHeartbeat instead of string comparison. HeartbeatDeviceName const removed from OtelMetricHandler.</done>
</task>

<task type="auto">
  <name>Task 3: Update tests for IsHeartbeat behavior</name>
  <files>
    tests/SnmpCollector.Tests/Pipeline/Behaviors/OidResolutionBehaviorTests.cs
    tests/SnmpCollector.Tests/Pipeline/Handlers/OtelMetricHandlerTests.cs
    tests/SnmpCollector.Tests/Pipeline/PipelineIntegrationTests.cs
  </files>
  <action>
    1. In `OidResolutionBehaviorTests.cs`, add a new test:
       ```csharp
       [Fact]
       public async Task SkipsResolution_WhenIsHeartbeat()
       {
           // Heartbeat messages should NOT call Resolve() — MetricName stays null
           var oidMapService = new StubOidMapService(knownOid: null, metricName: null);
           var behavior = new OidResolutionBehavior<SnmpOidReceived, Unit>(
               oidMapService, NullLogger<OidResolutionBehavior<SnmpOidReceived, Unit>>.Instance);
           var notification = new SnmpOidReceived
           {
               Oid = HeartbeatJobOptions.HeartbeatOid,
               AgentIp = IPAddress.Parse("127.0.0.1"),
               Value = new Integer32(1),
               Source = SnmpSource.Trap,
               TypeCode = SnmpType.Integer32,
               DeviceName = HeartbeatJobOptions.HeartbeatDeviceName,
               IsHeartbeat = true
           };
           var nextCalled = false;

           await behavior.Handle(notification, ct =>
           {
               nextCalled = true;
               return Task.FromResult(Unit.Value);
           }, CancellationToken.None);

           Assert.Null(notification.MetricName);   // Resolve was never called
           Assert.True(nextCalled);                 // Pipeline still continues
       }
       ```
       Add `using SnmpCollector.Configuration;` at the top if not present.

    2. In `OtelMetricHandlerTests.cs`:
       a. Update the `Heartbeat_SkipsMetricRecording_ButIncrementsHandled` test to use `IsHeartbeat = true`
          instead of referencing `OtelMetricHandler.HeartbeatDeviceName` (which is removed). Change:
          ```csharp
          // Before:
          var notification = MakeNotification(
              new Integer32(1),
              SnmpType.Integer32,
              deviceName: OtelMetricHandler.HeartbeatDeviceName);
          // After:
          var notification = MakeNotification(
              new Integer32(1),
              SnmpType.Integer32,
              deviceName: HeartbeatJobOptions.HeartbeatDeviceName);
          // Then set IsHeartbeat on the notification object:
          ```
          Actually, since MakeNotification is a static helper, the cleanest approach: construct the
          notification inline for this test with `IsHeartbeat = true`:
          ```csharp
          var notification = new SnmpOidReceived
          {
              Oid = "1.3.6.1.2.1.25.3.3.1.2",
              AgentIp = IPAddress.Parse("10.0.0.1"),
              Value = new Integer32(1),
              Source = SnmpSource.Trap,
              TypeCode = SnmpType.Integer32,
              DeviceName = HeartbeatJobOptions.HeartbeatDeviceName,
              IsHeartbeat = true
          };
          ```
          Add `using SnmpCollector.Configuration;` if not present.

       b. Optionally add a second test to confirm that `IsHeartbeat = false` with deviceName "heartbeat"
          does NOT suppress (proves the flag is what matters, not the string):
          ```csharp
          [Fact]
          public async Task HeartbeatDeviceName_WithoutFlag_StillRecordsMetric()
          {
              var notification = MakeNotification(
                  new Integer32(1),
                  SnmpType.Integer32,
                  deviceName: "heartbeat");
              // IsHeartbeat defaults to false — should record normally

              await _handler.Handle(notification, CancellationToken.None);

              Assert.Single(_testFactory.GaugeRecords);
          }
          ```

    3. In `PipelineIntegrationTests.cs`, no changes required unless compilation breaks from the
       `OtelMetricHandler.HeartbeatDeviceName` removal. Grep the file for any reference to
       `OtelMetricHandler.HeartbeatDeviceName` — if found, replace with
       `HeartbeatJobOptions.HeartbeatDeviceName`.

    4. Run `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` — all tests must pass.

    5. Grep the entire solution for remaining hardcoded `"heartbeat"` strings outside HeartbeatJobOptions.cs.
       There should be none in production code (test code referencing the string literal inline is acceptable
       but prefer using the const).
  </action>
  <verify>Run `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` — all tests pass (0 failures). Run `grep -rn '"heartbeat"' src/` to confirm no hardcoded strings remain in production code outside HeartbeatJobOptions.cs.</verify>
  <done>New test confirms OidResolutionBehavior skips resolution for heartbeat. Existing heartbeat suppression test updated to use IsHeartbeat flag. All tests green. No hardcoded "heartbeat" strings in production code outside HeartbeatJobOptions.</done>
</task>

</tasks>

<verification>
1. `dotnet build src/SnmpCollector/SnmpCollector.csproj` compiles cleanly
2. `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` — all tests pass
3. `grep -rn '"heartbeat"' src/` — only HeartbeatJobOptions.cs contains the literal string
4. `grep -rn 'IsHeartbeat' src/` — property used in SnmpOidReceived, OidResolutionBehavior, OtelMetricHandler, ChannelConsumerService
</verification>

<success_criteria>
- HeartbeatDeviceName const is single source of truth in HeartbeatJobOptions
- IsHeartbeat property on SnmpOidReceived is set at ingestion boundary (ChannelConsumerService)
- OidResolutionBehavior skips Resolve() for heartbeat messages (no misleading log)
- OtelMetricHandler uses notification.IsHeartbeat (no string comparison)
- HeartbeatJob uses HeartbeatJobOptions.HeartbeatDeviceName (no hardcoded string)
- All tests pass including new heartbeat-specific tests
</success_criteria>

<output>
After completion, create `.planning/quick/018-add-isheartbeat-flag-to-pipeline/018-SUMMARY.md`
</output>
