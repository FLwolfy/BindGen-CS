using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Native;

namespace BGCS.CSharp.Planning;

/// <summary>Checks scalar widths and exact value representations before emitting managed constant fields.</summary>
public sealed class CSharpConstantPlanner
{
    /// <summary>Plans selected native constants with no source-expression reinterpretation.</summary>
    /// <param name="model">The detached managed presentation and originating native values.</param>
    /// <param name="cancellationToken">Cancels constant planning.</param>
    /// <returns>The exact C# value declarations.</returns>
    public ImmutableArray<CSharpConstantPlan> Plan(
        ManagedApiModel model,
        CancellationToken cancellationToken = default
    ) {
        ArgumentNullException.ThrowIfNull(model);
        if (model.constants.IsDefault || model.interop.constants.IsDefault) throw new ArgumentException("Native and managed constant lists must be initialized.");
        if (!model.constants.IsEmpty && model.name.TrimStart('@') == "Constants")
            throw new ArgumentException("The API context name conflicts with its native constant container.");
        Dictionary<StableId, InteropConstant> source = model.interop.constants.ToDictionary(constant => constant.sourceDeclaration);
        HashSet<StableId> stringIdentities = model.interop.stringConstants.Select(constant => constant.sourceDeclaration).ToHashSet();
        if (source.Keys.Any(stringIdentities.Contains)) throw new ArgumentException("A source constant cannot have scalar and string representations.");
        HashSet<StableId> identities = [];
        HashSet<string> names = new(StringComparer.Ordinal);
        ImmutableArray<CSharpConstantPlan>.Builder plans = ImmutableArray.CreateBuilder<CSharpConstantPlan>();
        foreach (ManagedConstant member in model.constants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = CSharpNames.Identifier(member.name);
            if (name.TrimStart('@') is "Constants" or "GetType" or "GetHashCode" or "ToString" or "Equals" or "ReferenceEquals"
                || !names.Add(name.TrimStart('@')) || !identities.Add(member.sourceDeclaration))
                throw new ArgumentException("A managed constant is missing, duplicated or collides with its containing type.");
            if (stringIdentities.Contains(member.sourceDeclaration)) continue;
            if (!source.TryGetValue(member.sourceDeclaration, out InteropConstant? native))
                throw new ArgumentException("A managed constant references no checked scalar or string source identity.");
            NativeScalarConstant value = native.value;
            if (value is null || !Enum.IsDefined(value.kind) || value.evidence is null
                || value.evidence.level is not (EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified)
                || string.IsNullOrWhiteSpace(value.evidence.origin))
                throw new ArgumentException("A native scalar constant requires an established representation and evidence.");
            switch (value.kind)
            {
                case NativeScalarConstantKind.Integer:
                    if (value.width is not (1 or 2 or 4 or 8) || value.isSigned is not bool signed
                        || value.value is null || !Regex.IsMatch(value.value, "^-?(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)
                        || !BigInteger.TryParse(value.value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger integral))
                        throw new ArgumentException("A native integer constant requires canonical decimal data, width and signedness.");
                    BigInteger limit = BigInteger.One << (value.width * 8 - (signed ? 1 : 0));
                    if (integral < (signed ? -limit : BigInteger.Zero) || integral >= limit)
                        throw new ArgumentException("A native constant exceeds its established integer representation.");
                    string type = (value.width, signed) switch
                    { (1, true) => "sbyte", (1, false) => "byte", (2, true) => "short", (2, false) => "ushort", (4, true) => "int", (4, false) => "uint", (8, true) => "long", _ => "ulong" };
                    string suffix = value.width == 8 ? signed ? "L" : "UL" : value.width == 4 && !signed ? "U" : "";
                    plans.Add(new(member.sourceDeclaration, name, type, value.value + suffix, true) { documentation = native.documentation });
                    break;
                case NativeScalarConstantKind.Boolean:
                    if (value.width is not (1 or 2 or 4 or 8) || value.isSigned is not null || value.value is not ("0" or "1"))
                        throw new ArgumentException("A native Boolean constant must have established zero/one data.");
                    plans.Add(new(member.sourceDeclaration, name, "bool", value.value == "1" ? "true" : "false", true) { documentation = native.documentation });
                    break;
                case NativeScalarConstantKind.Binary32:
                    if (value.width != 4 || value.isSigned is not null || value.value is null
                        || !Regex.IsMatch(value.value, "^[0-9A-F]{8}$", RegexOptions.CultureInvariant))
                        throw new ArgumentException("A binary32 constant must retain exactly eight hexadecimal value bits.");
                    plans.Add(new(member.sourceDeclaration, name, "float", "global::System.BitConverter.UInt32BitsToSingle(0x" + value.value + "U)", false) { documentation = native.documentation });
                    break;
                case NativeScalarConstantKind.Binary64:
                    if (value.width != 8 || value.isSigned is not null || value.value is null
                        || !Regex.IsMatch(value.value, "^[0-9A-F]{16}$", RegexOptions.CultureInvariant))
                        throw new ArgumentException("A binary64 constant must retain exactly sixteen hexadecimal value bits.");
                    plans.Add(new(member.sourceDeclaration, name, "double", "global::System.BitConverter.UInt64BitsToDouble(0x" + value.value + "UL)", false) { documentation = native.documentation });
                    break;
            }
        }
        return plans.ToImmutable();
    }
}

/// <summary>Plans one checked scalar field in the generated constant container.</summary>
/// <param name="sourceDeclaration">The originating native identity.</param>
/// <param name="name">The checked managed identifier.</param>
/// <param name="type">The exact C# scalar representation.</param>
/// <param name="expression">The verified C# value expression.</param>
/// <param name="isCompileTimeConstant">Whether the representation uses a C# const field.</param>
public sealed record CSharpConstantPlan(
    StableId sourceDeclaration,
    string name,
    string type,
    string expression,
    bool isCompileTimeConstant
)
{
    /// <summary>Gets optional asserted managed presentation provenance, separate from the native value's evidence.</summary>
    public Evidence? presentationEvidence { get; init; }
    /// <summary>Gets detached documentation for the native source constant.</summary>
    public string? documentation { get; init; }
}
