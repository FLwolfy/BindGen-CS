using Xunit;

// Several independent backend fixtures invoke a real SDK compilation. Keep
// this suite within the same bounded compiler/process budget as Host tests.
[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]
