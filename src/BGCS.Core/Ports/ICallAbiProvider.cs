using BGCS.Core.Calls;

namespace BGCS.Core.Ports;

/// <summary>Classifies complete forward and reverse calls for an explicit ABI implementation.</summary>
public interface ICallAbiProvider
{
    /// <summary>Returns a bound direct plan, required bridge, rejection or unowned identity.</summary>
    /// <param name="request">The immutable signature and capability facts.</param>
    /// <returns>The explicit decision, without an execution or certification claim.</returns>
    CallAbiAssessment Assess(CallAbiRequest request);
}
