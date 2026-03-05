using Xunit;

namespace SnmpCollector.Tests.Helpers;

/// <summary>
/// xUnit collection definition that prevents parallel execution between test classes
/// that use MeterListener (a global listener with cross-test visibility).
/// Test classes decorated with [Collection(NonParallelCollection.Name)] run sequentially
/// relative to each other, preventing measurement cross-contamination.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NonParallelCollection : ICollectionFixture<object>
{
    public const string Name = "NonParallelMeterTests";
}
