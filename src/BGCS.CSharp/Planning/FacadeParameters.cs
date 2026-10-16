using System;

namespace BGCS.CSharp.Planning;

/// <summary>Shares exact friendly reference modifiers across every forwarding surface.</summary>
internal static class FacadeParameters
{
    internal static string Modifier(
        CSharpFacadeMethodPlan signature,
        int index
    ) {
        if (index < 0 || index >= signature.parameterTypes.Length || signature.parameterNames.Length != signature.parameterTypes.Length)
            throw new InvalidOperationException("A friendly forwarding signature has inconsistent parameter metadata.");
        if (signature.parameterModifiers.IsDefaultOrEmpty) return string.Empty;
        if (signature.parameterModifiers.Length != signature.parameterTypes.Length)
            throw new InvalidOperationException("A friendly forwarding signature lost its exact reference modifiers.");
        string modifier = signature.parameterModifiers[index];
        return modifier is "" or "in" or "ref" or "out" ? modifier : throw new InvalidOperationException("A friendly reference modifier is unsupported.");
    }

    internal static string Declaration(
        CSharpFacadeMethodPlan signature,
        int index
    )
        => Prefix(Modifier(signature, index)) + signature.parameterTypes[index] + " " + signature.parameterNames[index];

    internal static string Argument(
        CSharpFacadeMethodPlan signature,
        int index
    )
        => Prefix(Modifier(signature, index)) + signature.parameterNames[index];

    internal static string Argument(
        CSharpFacadeMethodPlan signature,
        int index,
        string expression
    )
        => Prefix(Modifier(signature, index)) + expression;

    private static string Prefix(string modifier) => modifier.Length == 0 ? string.Empty : modifier + " ";
}
