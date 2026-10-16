using Xunit;

// CLI fixtures launch real worker and SDK processes. Keep their aggregate
// compiler and artifact usage within the same budget as Host tests.
[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]
