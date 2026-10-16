using System.Threading;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;

namespace BGCS.Contracts.Ports;

/// <summary>Extends C++ type representation using only detached native facts.</summary>
public interface ICppTypeLowering
{
    /// <summary>Gets the stable extension identity.</summary>
    string id { get; }
    /// <summary>Gets deterministic matching priority.</summary>
    int priority { get; }
    /// <summary>Tests whether this extension claims an exclusive type representation.</summary>
    /// <param name="type">The immutable native type view.</param>
    /// <param name="context">Target and lowering inputs.</param>
    /// <returns>Whether the extension supports this type.</returns>
    bool CanLower(
        NativeTypeView type,
        LoweringContext context
    );
    /// <summary>Produces a detached C graph and typed conversion rules, including all representation dependencies.</summary>
    /// <param name="type">The immutable native type view.</param>
    /// <param name="context">Target and lowering inputs.</param>
    /// <param name="cancellationToken">Cancels cooperative planning.</param>
    /// <returns>The structured C representation.</returns>
    CppTypeLoweringPlan Lower(
        NativeTypeView type,
        LoweringContext context,
        CancellationToken cancellationToken
    );
}
