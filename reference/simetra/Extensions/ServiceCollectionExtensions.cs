using System.Diagnostics;
using k8s;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Quartz;
using Simetra.Configuration;
using Simetra.Configuration.Validators;
using Simetra.Devices;
using Simetra.HealthChecks;
using Simetra.Jobs;
using Simetra.Pipeline;
using Simetra.Pipeline.Middleware;
using Simetra.Services;
using Simetra.Lifecycle;
using Simetra.Telemetry;

namespace Simetra.Extensions;

// 11-Step Startup Sequence (LIFE-01):
// Steps execute via IHostedService registration order and host build sequence.
//  1. Validate configuration        -- ValidateOnStart on all Options (AddSimetraConfiguration)
//  2. Initialize telemetry providers -- AddSimetraTelemetry (MeterProvider, TracerProvider, LoggerProvider)
//  3. Start leader election          -- K8sLeaseElection hosted service (AddSimetraTelemetry)
//  4. Initialize device registry     -- DeviceRegistry singleton (AddSnmpPipeline)
//  5. Initialize device channels     -- DeviceChannelManager singleton (AddSnmpPipeline)
//  6. Build middleware pipeline      -- TrapMiddlewareDelegate singleton (AddSnmpPipeline)
//  7. Start SNMP listener            -- SnmpListenerService hosted service (AddSnmpPipeline)
//  7b. Start channel consumers       -- ChannelConsumerService hosted service (AddSnmpPipeline)
//  8. Merge poll definitions         -- PollDefinitionRegistry in AddScheduling
//  9. Start Quartz scheduler         -- QuartzHostedService (AddScheduling)
// 10. Generate first correlationId   -- Direct call after builder.Build() (Program.cs)
// 11. Map health endpoints + run     -- MapHealthChecks + app.Run() (Program.cs)
//
// Shutdown Sequence (LIFE-05):
// Reverse of startup, orchestrated by GracefulShutdownService (registered LAST, stops FIRST):
//  1. Release lease                  -- K8sLeaseElection.StopAsync (called explicitly)
//  2. Stop SNMP listener             -- SnmpListenerService.StopAsync (called explicitly)
//  3. Scheduler standby              -- IScheduler.Standby() (prevents new job fires)
//  4. Drain device channels          -- CompleteAll() + WaitForDrainAsync()
//  5. Flush telemetry                -- MeterProvider/TracerProvider.ForceFlush (protected budget)
// Then framework calls remaining StopAsync in reverse order (idempotent double-stops).

/// <summary>
/// Extension methods for registering Simetra configuration and pipeline services with DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers OpenTelemetry MeterProvider, TracerProvider, and LoggerProvider with
    /// OTLP exporters, the ILeaderElection abstraction, and the log enrichment processor.
    /// Must be called FIRST in DI registration (registered first = disposed last,
    /// ensuring ForceFlush during shutdown).
    /// <para>
    /// Leader election: auto-detects Kubernetes in-cluster environment via
    /// <see cref="KubernetesClientConfiguration.IsInCluster"/>. In-cluster uses
    /// <see cref="K8sLeaseElection"/> with coordination.k8s.io/v1 Lease API;
    /// local dev uses <see cref="AlwaysLeaderElection"/> (always reports leader).
    /// </para>
    /// <para>
    /// Logging configuration: default providers (Console, Debug, EventSource) are cleared.
    /// Plain-text console via <see cref="SimetraConsoleFormatter"/> is re-added only when
    /// <see cref="LoggingOptions.EnableConsole"/> is true. Each line is prefixed with
    /// [site] [role] [correlationId] for operational context.
    /// OTLP log exporter is always active (not role-gated -- all pods export logs).
    /// </para>
    /// </summary>
    public static IHostApplicationBuilder AddSimetraTelemetry(
        this IHostApplicationBuilder builder)
    {
        var otlpOptions = new OtlpOptions { Endpoint = "", ServiceName = "" };
        builder.Configuration.GetSection(OtlpOptions.SectionName).Bind(otlpOptions);

        var loggingOptions = new LoggingOptions();
        builder.Configuration.GetSection(LoggingOptions.SectionName).Bind(loggingOptions);

        // --- Leader Election ---
        // Auto-detect Kubernetes in-cluster vs local dev environment.
        // In-cluster: K8sLeaseElection (coordination.k8s.io/v1 Lease API)
        // Local dev: AlwaysLeaderElection (always reports leader)
        if (KubernetesClientConfiguration.IsInCluster())
        {
            // Production: Kubernetes Lease-based leader election
            var kubeConfig = KubernetesClientConfiguration.InClusterConfig();
            builder.Services.AddSingleton<IKubernetes>(new Kubernetes(kubeConfig));

            // Register concrete singleton FIRST, then resolve for both interfaces.
            // This ensures a SINGLE instance serves ILeaderElection and IHostedService
            // (avoids two-instance pitfall where hosted service updates one instance
            // but consumers read from a different one).
            builder.Services.AddSingleton<K8sLeaseElection>();
            builder.Services.AddSingleton<ILeaderElection>(sp =>
                sp.GetRequiredService<K8sLeaseElection>());
            builder.Services.AddHostedService(sp =>
                sp.GetRequiredService<K8sLeaseElection>());
        }
        else
        {
            // Local dev: always leader (single instance, no Kubernetes dependency)
            builder.Services.AddSingleton<ILeaderElection, AlwaysLeaderElection>();
        }

        // --- Metrics + Tracing ---
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: otlpOptions.ServiceName,
                    serviceInstanceId: Environment.GetEnvironmentVariable("HOSTNAME")
                        ?? Environment.MachineName))
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(TelemetryConstants.LeaderMeterName);
                metrics.AddMeter(TelemetryConstants.InstanceMeterName);
                metrics.AddRuntimeInstrumentation();

                // Manual OTLP metric exporter wrapped in MetricRoleGatedExporter.
                // Cannot use AddOtlpExporter() because it creates and registers the exporter
                // internally, preventing wrapping with MetricRoleGatedExporter. (HA-03, HA-04)
                // MetricRoleGatedExporter gates only Simetra.Leader behind leader election;
                // runtime metrics (System.Runtime) are exported by all pods for operational visibility.
                metrics.AddReader(sp =>
                {
                    var leaderElection = sp.GetRequiredService<ILeaderElection>();
                    var otlpExporter = new OtlpMetricExporter(new OtlpExporterOptions
                    {
                        Endpoint = new Uri(otlpOptions.Endpoint)
                    });
                    var roleGated = new MetricRoleGatedExporter(otlpExporter, leaderElection, TelemetryConstants.LeaderMeterName);
                    return new PeriodicExportingMetricReader(roleGated);
                });
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(TelemetryConstants.TracingSourceName);

                // Manual OTLP trace exporter wrapped in RoleGatedExporter.
                // Same rationale as metrics -- AddOtlpExporter() prevents wrapping. (HA-03, HA-04)
                tracing.AddProcessor(sp =>
                {
                    var leaderElection = sp.GetRequiredService<ILeaderElection>();
                    var otlpExporter = new OtlpTraceExporter(new OtlpExporterOptions
                    {
                        Endpoint = new Uri(otlpOptions.Endpoint)
                    });
                    var roleGated = new RoleGatedExporter<Activity>(otlpExporter, leaderElection);
                    return new BatchActivityExportProcessor(roleGated);
                });
            });

        // --- Logging ---
        // Clear default providers (Console, Debug, EventSource) so that
        // EnableConsole=false produces zero stdout output.
        builder.Logging.ClearProviders();

        // Conditionally add custom SimetraConsoleFormatter for plain-text output.
        // Every log line is prefixed with [site] [role] [correlationId] for operational
        // context without the verbosity of JSON structured output.
        if (loggingOptions.EnableConsole)
        {
            builder.Logging.AddConsole(options =>
                options.FormatterName = SimetraConsoleFormatter.FormatterName);
            builder.Logging.AddConsoleFormatter<SimetraConsoleFormatter, SimetraConsoleFormatterOptions>();

            builder.Services.AddSingleton<IPostConfigureOptions<SimetraConsoleFormatterOptions>>(sp =>
            {
                return new PostConfigureSimetraFormatterOptions(sp);
            });
        }

        // OTLP log exporter: active on ALL pods (not role-gated -- TELEM-04).
        // Enrichment processor adds site/role/correlationId to every log record.
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
            logging.SetResourceBuilder(
                ResourceBuilder.CreateDefault()
                    .AddService(
                        serviceName: otlpOptions.ServiceName,
                        serviceInstanceId: Environment.GetEnvironmentVariable("HOSTNAME")
                            ?? Environment.MachineName));
            logging.AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(otlpOptions.Endpoint);
            });
            logging.AddProcessor(sp =>
            {
                var siteOptions = sp.GetRequiredService<IOptions<SiteOptions>>().Value;
                var correlationService = sp.GetRequiredService<ICorrelationService>();
                var leaderElection = sp.GetRequiredService<ILeaderElection>();
                return new SimetraLogEnrichmentProcessor(
                    correlationService,
                    siteOptions.Name,
                    () => leaderElection.CurrentRole);
            });
        });

        return builder;
    }

    /// <summary>
    /// Registers all Simetra configuration Options classes, validators, and PostConfigure
    /// callbacks. All options use ValidateOnStart for fail-fast behavior.
    /// </summary>
    public static IServiceCollection AddSimetraConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- Flat options with DataAnnotations validation ---

        services.AddOptions<SiteOptions>()
            .Bind(configuration.GetSection(SiteOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LeaseOptions>()
            .Bind(configuration.GetSection(LeaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SnmpListenerOptions>()
            .Bind(configuration.GetSection(SnmpListenerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<HeartbeatJobOptions>()
            .Bind(configuration.GetSection(HeartbeatJobOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CorrelationJobOptions>()
            .Bind(configuration.GetSection(CorrelationJobOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LivenessOptions>()
            .Bind(configuration.GetSection(LivenessOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ChannelsOptions>()
            .Bind(configuration.GetSection(ChannelsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OtlpOptions>()
            .Bind(configuration.GetSection(OtlpOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LoggingOptions>()
            .Bind(configuration.GetSection(LoggingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // --- DevicesOptions: custom binding for top-level JSON array ---

        services.AddOptions<DevicesOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                config.GetSection(DevicesOptions.SectionName).Bind(options.Devices);
            })
            .ValidateOnStart();

        // --- IValidateOptions validators (singleton) ---

        services.AddSingleton<IValidateOptions<SiteOptions>, SiteOptionsValidator>();
        services.AddSingleton<IValidateOptions<LeaseOptions>, LeaseOptionsValidator>();
        services.AddSingleton<IValidateOptions<SnmpListenerOptions>, SnmpListenerOptionsValidator>();
        services.AddSingleton<IValidateOptions<DevicesOptions>, DevicesOptionsValidator>();
        services.AddSingleton<IValidateOptions<OtlpOptions>, OtlpOptionsValidator>();

        // --- PostConfigure: SiteOptions PodIdentity default ---

        services.PostConfigure<SiteOptions>(options =>
        {
            options.PodIdentity ??= Environment.GetEnvironmentVariable("HOSTNAME")
                                    ?? Environment.MachineName;
        });

        // --- PostConfigure: Set Source = Configuration on all config-loaded MetricPolls ---

        services.PostConfigure<DevicesOptions>(options =>
        {
            foreach (var device in options.Devices)
            {
                foreach (var poll in device.MetricPolls)
                {
                    poll.Source = MetricPollSource.Configuration;
                }
            }
        });

        return services;
    }

    /// <summary>
    /// Registers all device module implementations as <see cref="IDeviceModule"/> singletons.
    /// Must be called before <see cref="AddSnmpPipeline"/> so that
    /// <c>IEnumerable&lt;IDeviceModule&gt;</c> is available when DeviceRegistry and
    /// DeviceChannelManager resolve.
    /// </summary>
    public static IServiceCollection AddDeviceModules(this IServiceCollection services)
    {
        services.AddSingleton<IDeviceModule, SimetraModule>();
        services.AddSingleton<IDeviceModule, NpbModule>();
        services.AddSingleton<IDeviceModule, ObpModule>();

        return services;
    }

    /// <summary>
    /// Registers all Phase 3 SNMP pipeline services: device registry, trap filter,
    /// channel manager, extractor, middleware pipeline, and the listener hosted service.
    /// Must be called after <see cref="AddSimetraConfiguration"/> (services depend on IOptions).
    /// </summary>
    public static IServiceCollection AddSnmpPipeline(this IServiceCollection services)
    {
        // Pipeline infrastructure (singletons -- live for app lifetime)
        services.AddSingleton<ICorrelationService, RotatingCorrelationService>();
        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
        services.AddSingleton<ITrapFilter, TrapFilter>();
        services.AddSingleton<IDeviceChannelManager, DeviceChannelManager>();
        services.AddSingleton<ISnmpExtractor, SnmpExtractorService>();

        // Middleware (singletons -- stateless)
        services.AddSingleton<ErrorHandlingMiddleware>();
        services.AddSingleton<CorrelationIdMiddleware>();
        services.AddSingleton<LoggingMiddleware>();

        // Build the middleware pipeline as a singleton delegate
        services.AddSingleton<TrapMiddlewareDelegate>(sp =>
        {
            var builder = new TrapPipelineBuilder();
            // Order matters: error handling outermost, then correlationId, then logging
            builder.Use(sp.GetRequiredService<ErrorHandlingMiddleware>());
            builder.Use(sp.GetRequiredService<CorrelationIdMiddleware>());
            builder.Use(sp.GetRequiredService<LoggingMiddleware>());
            return builder.Build();
        });

        // Hosted service (the listener)
        services.AddHostedService<SnmpListenerService>();

        // Channel consumer service (reads from channels written by listener)
        // Registered AFTER listener (starts after), BEFORE GracefulShutdownService
        // (stops after shutdown orchestrator completes channels).
        services.AddHostedService<ChannelConsumerService>();

        return services;
    }

    /// <summary>
    /// Registers all Phase 4 processing pipeline services: metric factory, state vector,
    /// and processing coordinator. Must be called after <see cref="AddSimetraConfiguration"/>
    /// (MetricFactory depends on <c>IOptions&lt;SiteOptions&gt;</c>).
    /// </summary>
    public static IServiceCollection AddProcessingPipeline(this IServiceCollection services)
    {
        services.AddSingleton<IMetricFactory, MetricFactory>();
        services.AddSingleton<IStateVectorService, StateVectorService>();
        services.AddSingleton<IProcessingCoordinator, ProcessingCoordinator>();
        services.AddSingleton<PipelineMetricService>();

        return services;
    }

    /// <summary>
    /// Registers the Quartz.NET scheduler, all job types, liveness vector service,
    /// and poll definition registry. Configures static jobs (heartbeat, correlation) and
    /// dynamic jobs (state polls from modules, metric polls from configuration) with
    /// appropriate triggers and misfire handling.
    /// Must be called after <see cref="AddSnmpPipeline"/> and <see cref="AddDeviceModules"/>.
    /// </summary>
    public static IServiceCollection AddScheduling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- Phase 6 services ---
        services.AddSingleton<ILivenessVectorService, LivenessVectorService>();
        services.AddSingleton<IPollDefinitionRegistry, PollDefinitionRegistry>();

        // --- Bind options for trigger intervals ---
        var heartbeatOptions = new HeartbeatJobOptions();
        configuration.GetSection(HeartbeatJobOptions.SectionName).Bind(heartbeatOptions);

        var correlationOptions = new CorrelationJobOptions();
        configuration.GetSection(CorrelationJobOptions.SectionName).Bind(correlationOptions);

        // --- Read device configuration for dynamic poll job registration ---
        var devicesOptions = new DevicesOptions();
        configuration.GetSection(DevicesOptions.SectionName).Bind(devicesOptions.Devices);

        // --- Job interval registry (Phase 9) ---
        // Populated during registration so LivenessHealthCheck can compute per-job
        // staleness thresholds. Created inline because interval values are only
        // available here in AddScheduling, not during DI resolution.
        var intervalRegistry = new JobIntervalRegistry();

        // Module dictionary hoisted for job counting and scheduling
        var simetraModule = new SimetraModule();
        var npbModule = new NpbModule();
        var obpModule = new ObpModule();
        var modulesByType = new Dictionary<string, IDeviceModule>(StringComparer.OrdinalIgnoreCase)
        {
            [simetraModule.DeviceType] = simetraModule,
            [npbModule.DeviceType] = npbModule,
            [obpModule.DeviceType] = obpModule
        };

        // Auto-scale Quartz thread pool to total job count
        var jobCount = 2; // heartbeat + correlation
        foreach (var device in devicesOptions.Devices)
        {
            if (modulesByType.TryGetValue(device.DeviceType, out var mod))
                jobCount += mod.StatePollDefinitions.Count;
            jobCount += device.MetricPolls.Count;
        }

        services.AddQuartz(q =>
        {
            q.UseInMemoryStore();
            q.UseDefaultThreadPool(maxConcurrency: jobCount);

            // --- Static jobs: Heartbeat ---
            // NOTE on misfire handling (SCHED-10): All triggers use
            // WithMisfireHandlingInstructionNextWithRemainingCount(). This is the
            // correct SimpleTrigger instruction for "skip stale fires, wait for next."
            // The "DoNothing" instruction ONLY exists on CronTrigger and is NOT available
            // on SimpleTrigger. For indefinite RepeatForever triggers, NextWithRemainingCount
            // provides identical semantics. See 06-RESEARCH.md Pitfall 3 for details.
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

            // --- Static jobs: Correlation ---
            var correlationKey = new JobKey("correlation");
            q.AddJob<CorrelationJob>(j => j.WithIdentity(correlationKey));
            q.AddTrigger(t => t
                .ForJob(correlationKey)
                .WithIdentity("correlation-trigger")
                .StartNow()
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(correlationOptions.IntervalSeconds)
                    .RepeatForever()
                    .WithMisfireHandlingInstructionNextWithRemainingCount()));
            intervalRegistry.Register("correlation", correlationOptions.IntervalSeconds);

            // --- Dynamic jobs: State polls (Source=Module) ---
            // Modules are type-level: iterate config devices, look up module by DeviceType,
            // and schedule state polls for each matching device.
            // Uses PollKey (MetricName + StaticLabels) for unique Quartz job identities --
            // multiple polls share a MetricName but differ by StaticLabels (e.g., fan_status
            // for fans 1-4 produces keys fan_status-1, fan_status-2, etc.).

            foreach (var device in devicesOptions.Devices)
            {
                if (!modulesByType.TryGetValue(device.DeviceType, out var module))
                    continue;

                foreach (var poll in module.StatePollDefinitions)
                {
                    var pollKey = poll.PollKey;
                    var jobKey = new JobKey($"state-poll-{device.Name}-{pollKey}");
                    q.AddJob<StatePollJob>(j => j
                        .WithIdentity(jobKey)
                        .UsingJobData("deviceName", device.Name)
                        .UsingJobData("pollKey", pollKey)
                        .UsingJobData("intervalSeconds", poll.IntervalSeconds));

                    q.AddTrigger(t => t
                        .ForJob(jobKey)
                        .WithIdentity($"state-poll-{device.Name}-{pollKey}-trigger")
                        .StartNow()
                        .WithSimpleSchedule(s => s
                            .WithIntervalInSeconds(poll.IntervalSeconds)
                            .RepeatForever()
                            .WithMisfireHandlingInstructionNextWithRemainingCount()));
                    intervalRegistry.Register($"state-poll-{device.Name}-{pollKey}", poll.IntervalSeconds);
                }
            }

            // --- Dynamic jobs: Metric polls (Source=Configuration) ---
            // Config polls have unique MetricNames per device, so PollKey == MetricName.
            // Module definitions win on key collision; duplicate config MetricNames keep first only.
            var seenConfigPollKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in devicesOptions.Devices)
            {
                foreach (var poll in device.MetricPolls)
                {
                    // Skip if module already defines a state poll with this PollKey
                    if (modulesByType.TryGetValue(device.DeviceType, out var mod) &&
                        mod.StatePollDefinitions.Any(sp => sp.PollKey == poll.MetricName))
                        continue;

                    // Skip duplicate MetricName within same device (first wins)
                    if (!seenConfigPollKeys.Add($"{device.Name}::{poll.MetricName}"))
                        continue;

                    var jobKey = new JobKey($"metric-poll-{device.Name}-{poll.MetricName}");
                    q.AddJob<MetricPollJob>(j => j
                        .WithIdentity(jobKey)
                        .UsingJobData("deviceName", device.Name)
                        .UsingJobData("pollKey", poll.MetricName)
                        .UsingJobData("intervalSeconds", poll.IntervalSeconds));

                    q.AddTrigger(t => t
                        .ForJob(jobKey)
                        .WithIdentity($"metric-poll-{device.Name}-{poll.MetricName}-trigger")
                        .StartNow()
                        .WithSimpleSchedule(s => s
                            .WithIntervalInSeconds(poll.IntervalSeconds)
                            .RepeatForever()
                            .WithMisfireHandlingInstructionNextWithRemainingCount()));
                    intervalRegistry.Register($"metric-poll-{device.Name}-{poll.MetricName}", poll.IntervalSeconds);
                }
            }
        });

        services.AddSingleton<IJobIntervalRegistry>(intervalRegistry);

        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
        });

        return services;
    }

    /// <summary>
    /// Registers the three Kubernetes health probe checks (startup, readiness, liveness)
    /// with tag-based filtering. Must be called after <see cref="AddScheduling"/> so that
    /// <see cref="IJobIntervalRegistry"/> is already registered.
    /// DI order: Telemetry -> Configuration -> DeviceModules -> SnmpPipeline -> ProcessingPipeline -> Scheduling -> HealthChecks -> Lifecycle.
    /// </summary>
    public static IServiceCollection AddSimetraHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<StartupHealthCheck>("startup", tags: new[] { "startup" })
            .AddCheck<ReadinessHealthCheck>("readiness", tags: new[] { "ready" })
            .AddCheck<LivenessHealthCheck>("liveness", tags: new[] { "live" });

        return services;
    }

    /// <summary>
    /// Registers <see cref="GracefulShutdownService"/> and configures <see cref="HostOptions.ShutdownTimeout"/>.
    /// <para>
    /// MUST be called LAST in DI registration order. The .NET Generic Host stops
    /// <see cref="IHostedService"/> instances in REVERSE registration order, so the
    /// last-registered service stops first. <see cref="GracefulShutdownService"/> is the
    /// SINGLE orchestrator of ALL 5 LIFE-05 shutdown steps:
    /// </para>
    /// <list type="number">
    /// <item><description>Release lease (K8sLeaseElection.StopAsync -- near-instant HA failover)</description></item>
    /// <item><description>Stop SNMP listener (SnmpListenerService.StopAsync -- no new traps)</description></item>
    /// <item><description>Scheduler standby (IScheduler.Standby -- no new job fires)</description></item>
    /// <item><description>Drain device channels (CompleteAll + WaitForDrainAsync)</description></item>
    /// <item><description>Flush telemetry (MeterProvider/TracerProvider.ForceFlush -- protected budget)</description></item>
    /// </list>
    /// <para>
    /// DI order: Telemetry -> Configuration -> DeviceModules -> SnmpPipeline -> ProcessingPipeline -> Scheduling -> HealthChecks -> Lifecycle.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSimetraLifecycle(this IServiceCollection services)
    {
        services.Configure<HostOptions>(opts =>
            opts.ShutdownTimeout = TimeSpan.FromSeconds(30));

        services.AddHostedService<GracefulShutdownService>();

        return services;
    }
}
