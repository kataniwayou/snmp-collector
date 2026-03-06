---
phase: quick-015
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/SnmpCollector/Configuration/HeartbeatJobOptions.cs
  - src/SnmpCollector/Jobs/HeartbeatJob.cs
  - src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
  - tests/SnmpCollector.Tests/Jobs/HeartbeatJobTests.cs
autonomous: true

must_haves:
  truths:
    - "HeartbeatJob sends a loopback SNMPv2c trap to 127.0.0.1 on the configured listener port every 15 seconds"
    - "The trap uses community string Simetra.heartbeat and OID 1.3.6.1.4.1.9999.1.1.1.0"
    - "The trap flows through the full pipeline (listener receives it, extracts device heartbeat, processes varbinds)"
    - "HeartbeatJob stamps the liveness vector after each execution (success or failure)"
    - "HeartbeatJob is registered in Quartz scheduler with interval from HeartbeatJob.IntervalSeconds config"
  artifacts:
    - path: "src/SnmpCollector/Configuration/HeartbeatJobOptions.cs"
      provides: "Options class bound from HeartbeatJob config section"
    - path: "src/SnmpCollector/Jobs/HeartbeatJob.cs"
      provides: "Quartz IJob that sends loopback heartbeat trap"
    - path: "tests/SnmpCollector.Tests/Jobs/HeartbeatJobTests.cs"
      provides: "Unit tests for HeartbeatJob"
  key_links:
    - from: "HeartbeatJob"
      to: "SnmpTrapListenerService"
      via: "UDP loopback trap to 127.0.0.1:configured_port"
    - from: "ServiceCollectionExtensions.AddSnmpScheduling"
      to: "HeartbeatJob"
      via: "Quartz job + trigger registration"
    - from: "HeartbeatJob.Execute finally block"
      to: "ILivenessVectorService.Stamp"
      via: "liveness stamp with job key"
---

<objective>
Add HeartbeatJob -- a Quartz job that sends a loopback SNMP trap to the local listener, proving the scheduler is alive. The trap flows through the full MediatR pipeline like any real device trap and produces a heartbeat metric.

Purpose: Provides end-to-end health proof -- if the heartbeat metric stops appearing, something in the scheduler or pipeline is broken.
Output: HeartbeatJobOptions, HeartbeatJob, DI wiring, unit tests.
</objective>

<execution_context>
@C:\Users\UserL\.claude/get-shit-done/workflows/execute-plan.md
@C:\Users\UserL\.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@src/SnmpCollector/Jobs/CorrelationJob.cs                          (reference pattern: Quartz job with liveness stamp)
@src/Simetra/Jobs/HeartbeatJob.cs                                   (reference implementation: Simetra's HeartbeatJob -- adapt this pattern)
@src/SnmpCollector/Services/SnmpTrapListenerService.cs              (the listener that receives the loopback trap)
@src/SnmpCollector/Pipeline/CommunityStringHelper.cs                (DeriveFromDeviceName("heartbeat") -> "Simetra.heartbeat")
@src/SnmpCollector/Pipeline/ILivenessVectorService.cs               (Stamp interface)
@src/SnmpCollector/Pipeline/ICorrelationService.cs                  (OperationCorrelationId scoping)
@src/SnmpCollector/Configuration/CorrelationJobOptions.cs           (options pattern to follow)
@src/SnmpCollector/Configuration/SnmpListenerOptions.cs             (Port property for loopback target)
@src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs        (AddSnmpScheduling + AddSnmpConfiguration for DI wiring)
@src/SnmpCollector/appsettings.json                                 (HeartbeatJob.IntervalSeconds: 15 already configured)
@src/SnmpCollector/appsettings.Development.json                     (OidMap has 1.3.6.1.4.1.9999.1.1.1.0: simetraHeartbeat)
</context>

<tasks>

<task type="auto">
  <name>Task 1: Create HeartbeatJobOptions and HeartbeatJob</name>
  <files>
    src/SnmpCollector/Configuration/HeartbeatJobOptions.cs
    src/SnmpCollector/Jobs/HeartbeatJob.cs
  </files>
  <action>
    1. Create HeartbeatJobOptions following CorrelationJobOptions pattern exactly:
       - Namespace: SnmpCollector.Configuration
       - sealed class with SectionName = "HeartbeatJob"
       - Property: int IntervalSeconds with [Range(1, int.MaxValue)] and default 15
       - Add a const string HeartbeatOid = "1.3.6.1.4.1.9999.1.1.1.0" (single source of truth for the heartbeat OID, avoiding magic strings in the job)

    2. Create HeartbeatJob following the Simetra reference implementation (src/Simetra/Jobs/HeartbeatJob.cs) adapted for SnmpCollector conventions:
       - Namespace: SnmpCollector.Jobs
       - [DisallowConcurrentExecution] sealed class implementing IJob
       - Constructor injects: ICorrelationService, ILivenessVectorService, IOptions<SnmpListenerOptions>, ILogger<HeartbeatJob>
       - Store _listenerPort from SnmpListenerOptions.Port
       - Derive community string using CommunityStringHelper.DeriveFromDeviceName("heartbeat") -- do NOT hardcode "Simetra.heartbeat". Store as readonly field set in constructor.

    3. Execute method (async Task, matches Simetra reference):
       - Capture correlationId and scope it: _correlation.OperationCorrelationId = _correlation.CurrentCorrelationId
       - Get jobKey from context.JobDetail.Key.Name
       - In try block:
         - Create variable list with one Variable: new Variable(new ObjectIdentifier(HeartbeatJobOptions.HeartbeatOid), new Integer32(1))
         - Create receiver endpoint: new IPEndPoint(IPAddress.Loopback, _listenerPort)
         - Send trap: await Task.Run(() => Messenger.SendTrapV2(requestId: 0, version: VersionCode.V2, receiver: receiver, community: new OctetString(_communityString), enterprise: new ObjectIdentifier(HeartbeatJobOptions.HeartbeatOid), timestamp: 0, variables: variables))
         - Log at Debug level: "Heartbeat trap sent to 127.0.0.1:{ListenerPort}"
       - Catch OperationCanceledException: rethrow (shutdown signal)
       - Catch Exception: log error "Heartbeat job {JobKey} failed"
       - Finally block: clear OperationCorrelationId to null, then stamp liveness: _liveness.Stamp(jobKey)

    Using directives needed: System.Net, Lextm.SharpSnmpLib, Lextm.SharpSnmpLib.Messaging, Microsoft.Extensions.Logging, Microsoft.Extensions.Options, Quartz, SnmpCollector.Configuration, SnmpCollector.Pipeline.
  </action>
  <verify>
    Both files compile: `dotnet build src/SnmpCollector/SnmpCollector.csproj --no-restore 2>&1 | tail -5`
  </verify>
  <done>
    HeartbeatJobOptions binds from "HeartbeatJob" config section with IntervalSeconds and HeartbeatOid const. HeartbeatJob sends loopback SNMPv2c trap via Messenger.SendTrapV2 and stamps liveness vector.
  </done>
</task>

<task type="auto">
  <name>Task 2: Wire HeartbeatJob into DI and Quartz scheduler</name>
  <files>
    src/SnmpCollector/Extensions/ServiceCollectionExtensions.cs
  </files>
  <action>
    1. In AddSnmpConfiguration method, add HeartbeatJobOptions binding AFTER CorrelationJobOptions block (around line 190):
       ```
       services.AddOptions<HeartbeatJobOptions>()
           .Bind(configuration.GetSection(HeartbeatJobOptions.SectionName))
           .ValidateDataAnnotations()
           .ValidateOnStart();
       ```

    2. In AddSnmpScheduling method:
       a. Bind HeartbeatJobOptions for trigger interval (after correlationOptions binding, around line 363):
          ```
          var heartbeatOptions = new HeartbeatJobOptions();
          configuration.GetSection(HeartbeatJobOptions.SectionName).Bind(heartbeatOptions);
          ```

       b. Increment jobCount by 1 for HeartbeatJob:
          Change `var jobCount = 1;` to `var jobCount = 2;` (CorrelationJob + HeartbeatJob)
          Add a comment: `// 2 = CorrelationJob + HeartbeatJob`

       c. Inside AddQuartz lambda, AFTER the CorrelationJob block and BEFORE the MetricPollJob loop, add HeartbeatJob registration:
          ```
          // HeartbeatJob: sends loopback SNMP trap to prove scheduler + pipeline alive.
          var heartbeatKey = new JobKey("heartbeat");
          q.AddJob<HeartbeatJob>(j => j.WithIdentity(heartbeatKey));
          q.AddTrigger(t => t
              .ForJob(heartbeatKey)
              .WithIdentity("heartbeat-trigger")
              .StartNow()
              .WithSimpleSchedule(s => s
                  .WithIntervalInSeconds(heartbeatOptions.IntervalSeconds)
                  .RepeatForever()
                  .WithMisfireHandlingInstructionNextWithRemainingCount()));

          intervalRegistry.Register("heartbeat", heartbeatOptions.IntervalSeconds);
          ```

    3. Update the AddSnmpScheduling XML doc comment to mention HeartbeatJob alongside CorrelationJob and MetricPollJob.
  </action>
  <verify>
    Full solution builds: `dotnet build src/SnmpCollector/SnmpCollector.csproj --no-restore 2>&1 | tail -5`
  </verify>
  <done>
    HeartbeatJob is registered in Quartz with interval from config. Thread pool sized to include it. Liveness interval registry knows about "heartbeat" job key.
  </done>
</task>

<task type="auto">
  <name>Task 3: Add HeartbeatJob unit tests</name>
  <files>
    tests/SnmpCollector.Tests/Jobs/HeartbeatJobTests.cs
  </files>
  <action>
    Create HeartbeatJobTests.cs following the same test patterns used in the project (xUnit, NSubstitute/Moq -- check existing test files for the mocking library in use).

    Tests to write:

    1. Execute_SendsTrapAndStampsLiveness:
       - Mock ICorrelationService, ILivenessVectorService, IOptions<SnmpListenerOptions>, IJobExecutionContext
       - Set up SnmpListenerOptions with Port = some test port (e.g., 9162)
       - Set up job context to return JobKey with name "heartbeat"
       - Execute the job
       - Verify _liveness.Stamp("heartbeat") was called exactly once
       - Verify OperationCorrelationId was set then cleared to null

    2. Execute_OnException_StillStampsLiveness:
       - Create a scenario where the trap send might fail (use a port that would cause issues, or verify the finally block behavior)
       - The key assertion: _liveness.Stamp is called even when the try block throws
       - Verify OperationCorrelationId is cleared to null

    3. Execute_UsesDerivedCommunityString:
       - Verify the job constructs the community string via CommunityStringHelper.DeriveFromDeviceName("heartbeat")
       - Since CommunityStringHelper is static, verify the stored community string field equals "Simetra.heartbeat"
       - This can be done by checking the job was constructed without error (community string derived in constructor)

    Note: The actual UDP send (Messenger.SendTrapV2) is a static call and cannot be easily mocked. Tests should focus on:
    - Liveness stamp behavior (always stamped in finally)
    - Correlation ID scoping (set at start, cleared in finally)
    - Constructor derives correct community string
    - Job key is passed correctly to Stamp

    Use NullLogger<HeartbeatJob> for the logger dependency.
  </action>
  <verify>
    Tests pass: `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj --filter "FullyQualifiedName~HeartbeatJob" --no-restore 2>&1 | tail -10`
  </verify>
  <done>
    HeartbeatJob has unit tests covering liveness stamping (success and failure paths), correlation ID scoping, and community string derivation. All tests pass.
  </done>
</task>

</tasks>

<verification>
1. `dotnet build src/SnmpCollector/SnmpCollector.csproj` -- full build succeeds
2. `dotnet test tests/SnmpCollector.Tests/SnmpCollector.Tests.csproj` -- all tests pass (including new HeartbeatJob tests)
3. Verify HeartbeatJob appears in Quartz registration by checking ServiceCollectionExtensions has heartbeat job key and trigger
4. Verify appsettings.json already has HeartbeatJob.IntervalSeconds: 15 (no config changes needed)
5. Verify OidMap in appsettings.Development.json has 1.3.6.1.4.1.9999.1.1.1.0: simetraHeartbeat (no config changes needed)
</verification>

<success_criteria>
- HeartbeatJob sends SNMPv2c trap to 127.0.0.1 on configured listener port with community "Simetra.heartbeat" and heartbeat OID varbind
- Job stamps liveness vector on every execution (success or failure)
- Job registered in Quartz with configurable interval (default 15s)
- Thread pool sized to include heartbeat job
- Liveness interval registry includes "heartbeat" key
- All existing tests continue to pass
- New HeartbeatJob tests pass
</success_criteria>

<output>
After completion, create `.planning/quick/015-add-heartbeat-job/015-SUMMARY.md`
</output>
