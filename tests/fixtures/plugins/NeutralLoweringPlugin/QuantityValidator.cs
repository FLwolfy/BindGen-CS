using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Contracts;
using BGCS.Contracts.Ports;
using BGCS.Contracts.Requests;

namespace BGCS.Tests.NeutralPlugin;

/// <summary>Checks a lowered domain export and its metadata using only neutral Contracts.</summary>
public sealed class QuantityValidator : IGenerationValidator, IDisposable
{
    /// <inheritdoc/>
    public string id => "fixture.quantity-validation";
    /// <inheritdoc/>
    public int priority => 0;
    /// <summary>Gets whether the validator has released its session resources.</summary>
    public bool disposed { get; private set; }
    /// <inheritdoc/>
    public async ValueTask<ImmutableArray<BindingDiagnostic>> ValidateAsync(ValidationRequest request, CancellationToken cancellationToken)
    {
        if (!request.interop.functions.Any(function => function.sourceQualifiedName == "Sample::add_quantity"))
            return [new(BindingDiagnosticSeverity.Error, "The selected domain operation was omitted from the final ABI.", "QUANTITY-EXPORT")];
        string path = request.artifacts.Single(artifact => artifact.relativePath.EndsWith("/metadata/abi.manifest.json", StringComparison.Ordinal)).relativePath;
        string metadata = await request.content.ReadTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (!metadata.Contains("Sample::add_quantity", StringComparison.Ordinal))
            return [new(BindingDiagnosticSeverity.Error, "The emitted domain metadata lost its source identity.", "QUANTITY-METADATA")];
        return [];
    }
    /// <inheritdoc/>
    public void Dispose() => disposed = true;
}
