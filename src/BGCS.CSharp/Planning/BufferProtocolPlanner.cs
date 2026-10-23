using System;
using BGCS.Analysis.Safety;
using BGCS.Core.Common;
using BGCS.Core.Interop;

namespace BGCS.CSharp.Planning;

/// <summary>Plans exact capacity and written-extent channels for a pinned span.</summary>
public sealed class BufferProtocolPlanner
{
    /// <summary>Plans one checked native span protocol using the backend's actual raw type resolver.</summary>
    /// <param name="module">The complete native ABI.</param>
    /// <param name="function">The native function containing all related indices.</param>
    /// <param name="parameter">The explicitly annotated buffer pointer.</param>
    /// <param name="resolve">The checked raw C# carrier resolver.</param>
    /// <returns>A plan requiring checked bound conversions and post-call written-count range and unit validation.</returns>
    public CSharpBufferProtocolPlan Plan(
        InteropModule module,
        InteropFunction function,
        InteropParameter parameter,
        Func<StableId, string> resolve
    ) {
        ArgumentNullException.ThrowIfNull(resolve);
        BufferProtocolAnalysis protocol = new BufferProtocolAnalyzer().Analyze(module, function, parameter);
        int bound = Index(protocol.boundParameter), written = protocol.writtenCountParameter is StableId count ? Index(count) : -1;
        return new(Index(parameter.id), bound, protocol.boundIsCapacity, written, resolve(protocol.elementType), protocol.elementSize,
            resolve(function.parameters[bound].type), protocol.writtenCountType is StableId type ? resolve(type) : null,
            protocol.writtenCountType is StableId integral && module.GetType(integral).isSigned == true,
            parameter.buffer!.direction, parameter.buffer.lengthUnit, parameter.buffer.allowsNull!.Value, protocol.alignment, protocol.writtenCountAlignment);

        int Index(StableId identity)
        {
            for (int index = 0; index < function.parameters.Length; index++) if (function.parameters[index].id == identity) return index;
            throw new InvalidOperationException("A checked native buffer protocol lost a parameter identity.");
        }
    }
}

/// <summary>Plans a scalar length or capacity and an optional output count using native units.</summary>
/// <param name="parameterIndex">The native buffer pointer index.</param>
/// <param name="boundIndex">The exact native scalar length or capacity index.</param>
/// <param name="boundIsCapacity">Whether the bound is writable capacity.</param>
/// <param name="writtenCountIndex">The output-count pointer index, or minus one.</param>
/// <param name="elementType">The checked raw element carrier.</param>
/// <param name="elementSize">The native element byte size for exact byte-unit conversions.</param>
/// <param name="boundType">The exact native scalar bound carrier.</param>
/// <param name="writtenCountType">The exact native output integral carrier, or null.</param>
/// <param name="writtenCountIsSigned">Whether the native output integral carrier is signed.</param>
/// <param name="direction">The native buffer access direction.</param>
/// <param name="lengthUnit">The common native bound and output-count units.</param>
/// <param name="allowsNull">Whether an empty span may pass a null address.</param>
/// <param name="alignment">The actual pinned-address alignment to prove before calling.</param>
/// <param name="writtenCountAlignment">The output-count address alignment to prove, or zero when absent.</param>
public sealed record CSharpBufferProtocolPlan(
    int parameterIndex,
    int boundIndex,
    bool boundIsCapacity,
    int writtenCountIndex,
    string elementType,
    int elementSize,
    string boundType,
    string? writtenCountType,
    bool writtenCountIsSigned,
    NativeParameterDirection direction,
    NativeLengthUnit lengthUnit,
    bool allowsNull,
    int alignment,
    int writtenCountAlignment
);
