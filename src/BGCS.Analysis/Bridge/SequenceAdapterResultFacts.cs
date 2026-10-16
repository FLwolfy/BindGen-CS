using System;
using System.Linq;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Native;

namespace BGCS.Analysis.Bridge;

/// <summary>Limits temporary sequence adapters to scalar results or independently owned default-standard text values.</summary>
public static class SequenceAdapterResultFacts
{
    /// <summary>Requires a complete fresh-value destruction pair before allowing text to outlive temporary input materialization.</summary>
    /// <param name="module">The complete typed bridge and original source facts.</param>
    /// <param name="export">The primary operation or its identical input adapter.</param>
    /// <returns>Whether its result cannot borrow the temporary input under the finite supported protocol.</returns>
    public static bool IsNonEscaping(
        CppBridgeModule module,
        BridgeExport export
    ) {
        ArgumentNullException.ThrowIfNull(module); ArgumentNullException.ThrowIfNull(export);
        if (export.nativeResultType is not StableId result) return export.resultType is null;
        NativeType type = StandardLibraryFactReader.Resolve(module.source, result);
        if (type.category is NativeTypeCategory.Primitive or NativeTypeCategory.Enumeration) return true;
        if (type.category != NativeTypeCategory.Record || export.resultConversion != BridgeConversionKind.OwnedValue || export.resultBorrowsInstance
            || export.resultExpression is not null || export.resultOwnership is not null || export.resultRelease is not StableId releaseId
            || StandardValueReleaseFacts.Read(module.source, type.id) is not { scope: NativeValueReleaseScope.CreatingCall }) return false;
        BridgeExport? release = module.exports.SingleOrDefault(item => item.id == releaseId);
        if (release is null || release.operation.kind != BridgeOperationKind.Destroy || release.operation.ownerType is not StableId owner
            || StandardLibraryFactReader.Resolve(module.source, owner).id != type.id || release.resultType is not null || release.parameters.Length != 1) return false;
        BridgeType? pointer = module.types.SingleOrDefault(item => item.id == export.resultType);
        return pointer is { kind: BridgeTypeKind.Pointer, isConst: false, elementType: StableId element }
            && release.parameters[0].type == pointer.id && module.types.SingleOrDefault(item => item.id == element) is { kind: BridgeTypeKind.Handle, nativeType: StableId native }
            && StandardLibraryFactReader.Resolve(module.source, native).id == type.id;
    }
}
