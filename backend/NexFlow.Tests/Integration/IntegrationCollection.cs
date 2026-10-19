using Xunit;

namespace NexFlow.Tests.Integration;

// Concurrency is controlled inside each scenario. Avoid unrelated migrations
// consuming the emulator/SQL pool during lock measurements on small runners.
[CollectionDefinition("RC3 isolated stores", DisableParallelization = true)]
public sealed class IntegrationCollection;
