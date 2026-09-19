using Xunit;

// The localisation tests change the interface language, which is process-wide state. Running the test
// classes one at a time keeps that deterministic.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
