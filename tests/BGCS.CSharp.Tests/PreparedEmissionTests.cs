using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Artifacts;
using BGCS.Core.Managed;
using BGCS.Core.Requests;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class PreparedEmissionTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false, false)]
    [InlineData(BindingImportMode.FunctionTable, false, true)]
    [InlineData(BindingImportMode.FunctionTable, true, false)]
    [InlineData(BindingImportMode.FunctionTable, true, true)]
    [InlineData(BindingImportMode.DllImport, false, false)]
    [InlineData(BindingImportMode.DllImport, false, true)]
    [InlineData(BindingImportMode.DllImport, true, false)]
    [InlineData(BindingImportMode.DllImport, true, true)]
    [InlineData(BindingImportMode.LibraryImport, false, false)]
    [InlineData(BindingImportMode.LibraryImport, false, true)]
    [InlineData(BindingImportMode.LibraryImport, true, false)]
    [InlineData(BindingImportMode.LibraryImport, true, true)]
    public async Task PreparationKeepsTheExactPlanAndChoicesWhileOnlyTheDestinationChanges(
        BindingImportMode importMode,
        bool runtimeSource,
        bool singleFile
    ) {
        ManagedApiModel model = NeutralBackendTests.ModelWithStorage() with { emitNativeMetadata = true };
        NeverWritten originalSink = new();
        EmissionRequest request = PlanningFixture.Prepare(model, new("prepared_fixture", originalSink)
        {
            importMode = importMode,
            includeRuntimeSource = runtimeSource,
            runtimeNamespace = runtimeSource ? "Generated.Example.Runtime" : "BGCS.Runtime",
            singleFile = singleFile
        });
        CSharpBackend backend = new();
        CSharpEmission prepared = backend.Prepare(model, request);
        Assert.Same(model, prepared.plan.api);
        Assert.Equal(importMode, prepared.plan.importMode);
        Assert.False(originalSink.written);

        NeutralBackendTests.MemorySink first = new(), second = new(), direct = new();
        ImmutableArray<ArtifactDescriptor> firstArtifacts = await prepared.EmitAsync(first);
        ImmutableArray<ArtifactDescriptor> directArtifacts = await backend.EmitAsync(model, request with { sink = direct }, CancellationToken.None);
        EmissionRequest changedCopy = request with { libraryName = "different_fixture", singleFile = !singleFile };
        Assert.NotEqual(changedCopy.libraryName, request.libraryName);
        ImmutableArray<ArtifactDescriptor> secondArtifacts = await prepared.EmitAsync(second);

        Assert.Equal(directArtifacts.ToArray(), firstArtifacts.ToArray());
        Assert.Equal(firstArtifacts.ToArray(), secondArtifacts.ToArray());
        Assert.Equal(direct.sources.OrderBy(pair => pair.Key, StringComparer.Ordinal), first.sources.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(first.sources.OrderBy(pair => pair.Key, StringComparer.Ordinal), second.sources.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.False(originalSink.written);
    }

    [Fact]
    public async Task PreparationAndEmissionRetainRejectionCancellationAndHealthyRetryBoundaries()
    {
        ManagedApiModel healthy = NeutralBackendTests.Model();
        ManagedApiModel malformed = healthy with { methods = healthy.methods.SetItem(0, healthy.methods[0] with { defaults = default }) };
        NeverWritten originalSink = new();
        CSharpBackend backend = new();
        EmissionRequest healthyRequest = PlanningFixture.Prepare(healthy, new("prepared_fixture", originalSink));
        ArgumentException rejected = Assert.Throws<ArgumentException>(() => backend.Prepare(malformed, PlanningFixture.Prepare(malformed, healthyRequest)));
        Assert.Contains("Managed default arguments must be initialized.", rejected.Message, StringComparison.Ordinal);
        var originalCall = healthyRequest.callPlans[0];
        EmissionRequest changedCall = healthyRequest with
        {
            callPlans = healthyRequest.callPlans.SetItem(0, originalCall with { signature = originalCall.signature with { isVariadic = true } })
        };
        InvalidOperationException abi = Assert.Throws<InvalidOperationException>(() => backend.Prepare(healthy, changedCall));
        Assert.Contains("physical callable signature", abi.Message, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => backend.Prepare(healthy,
            healthyRequest with { managedRuntime = healthyRequest.managedRuntime! with { pointerSize = 4 } }));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => backend.Prepare(healthy, healthyRequest, cancellation.Token));

        CSharpEmission prepared = backend.Prepare(healthy, healthyRequest);
        NeutralBackendTests.MemorySink cancelled = new();
        OperationCanceledException failure = await Assert.ThrowsAsync<OperationCanceledException>(async () => await prepared.EmitAsync(cancelled, cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Empty(cancelled.sources);
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await prepared.EmitAsync(null!));
        NeutralBackendTests.MemorySink successful = new();
        Assert.NotEmpty(await prepared.EmitAsync(successful));
        Assert.NotEmpty(successful.sources);
        Assert.False(originalSink.written);
    }

    private sealed class NeverWritten : IArtifactSink
    {
        internal bool written { get; private set; }
        public string WriteText(
            string relativePath,
            string content
        ) {
            written = true;
            throw new InvalidOperationException("The preparation's original sink must never receive an emission.");
        }
    }
}
