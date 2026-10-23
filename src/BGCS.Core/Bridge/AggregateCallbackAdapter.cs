using BGCS.Core.Common;
using BGCS.Core.Interop;

namespace BGCS.Core.Bridge;

/// <summary>Associates a native callback with a checked pointer-boundary carrier and its explicit context channel.</summary>
/// <param name="sourceFunction">The original native registration or invocation.</param>
/// <param name="sourceParameter">The native callback argument.</param>
/// <param name="sourceCallableType">The exact original callback function type.</param>
/// <param name="carrierType">The normalized bridge function type.</param>
/// <param name="userDataParameter">The outer state pointer shared with the native callback.</param>
/// <param name="callableUserDataIndex">The callback argument carrying that same state.</param>
/// <param name="contract">The independently asserted lifetime, threading and quiescence facts.</param>
public sealed record AggregateCallbackAdapter(
    StableId sourceFunction,
    StableId sourceParameter,
    StableId sourceCallableType,
    StableId carrierType,
    StableId userDataParameter,
    int callableUserDataIndex,
    CallbackContract contract
);
