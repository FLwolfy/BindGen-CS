using System;
using System.Collections.Generic;
using System.Linq;

namespace BGCS.Intermediate.Bridges;

/// <summary>
/// Freezes a bridge callable's symbol, ABI and ownership facts after lowering.
/// </summary>
public sealed class CppBridgeFunction
{
    /// <summary>
    /// Copies the callable facts without retaining the mutable analysis model.
    /// </summary>
    /// <param name = "nativeName">
    /// The source callable identity.
    /// </param>
    /// <param name = "exportName">
    /// The lowered C symbol.
    /// </param>
    /// <param name = "kind">
    /// The constructor, destructor, instance, static or free-call role.
    /// </param>
    /// <param name = "returnType">
    /// The ABI return type.
    /// </param>
    /// <param name = "returnMarshalling">
    /// The return conversion and ownership plan.
    /// </param>
    /// <param name = "parameters">
    /// The ordered ABI parameters and their ownership plans.
    /// </param>
    /// <exception cref = "ArgumentNullException">
    /// The return facts or parameter sequence is null.
    /// </exception>
    public CppBridgeFunction(
        string nativeName,
        string exportName,
        BindingFunctionKind kind,
        BindingTypeReference returnType,
        MarshallingPlan returnMarshalling,
        IEnumerable<BindingParameter> parameters
    ) {
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(returnMarshalling);
        ArgumentNullException.ThrowIfNull(parameters);
        this.nativeName = nativeName;
        this.exportName = exportName;
        this.kind = kind;
        this.returnType = returnType;
        this.returnMarshalling = returnMarshalling;
        this.parameters = Array.AsReadOnly(parameters.ToArray());
    }

    /// <summary>
    /// Gets the source callable identity.
    /// </summary>
    public string nativeName { get; }
    /// <summary>
    /// Gets the lowered C symbol.
    /// </summary>
    public string exportName { get; }
    /// <summary>
    /// Gets the callable's lifecycle role.
    /// </summary>
    public BindingFunctionKind kind { get; }
    /// <summary>
    /// Gets the ABI return type.
    /// </summary>
    public BindingTypeReference returnType { get; }
    /// <summary>
    /// Gets return ownership and conversion facts.
    /// </summary>
    public MarshallingPlan returnMarshalling { get; }
    /// <summary>
    /// Gets the frozen ABI parameters.
    /// </summary>
    public IReadOnlyList<BindingParameter> parameters { get; }
}
