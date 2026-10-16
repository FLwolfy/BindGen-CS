using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Threading;
using BGCS.Contracts.Managed;

namespace BGCS.CSharp.Planning;

/// <summary>Checks managed enum declarations independently of native raw carriers and source formatting.</summary>
public sealed class ManagedEnumPlanner
{
    /// <summary>Checks complete fixed-width constants and allocates portable enum names.</summary>
    /// <param name="definitions">All explicit managed enum declarations, including unused declarations.</param>
    /// <param name="cancellationToken">Cancels declaration validation.</param>
    /// <returns>Checked enum representations ready for a source backend.</returns>
    public ImmutableArray<CSharpManagedEnumPlan> Plan(
        ImmutableArray<ManagedEnumDefinition> definitions,
        CancellationToken cancellationToken = default
    ) {
        if (definitions.IsDefault) throw new ArgumentException("Managed enum declarations cannot be an uninitialized collection.");
        HashSet<string> names = new(StringComparer.Ordinal);
        ImmutableArray<CSharpManagedEnumPlan>.Builder plans = ImmutableArray.CreateBuilder<CSharpManagedEnumPlan>();
        foreach (ManagedEnumDefinition definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(definition);
            string name = CSharpNames.Identifier(definition.name);
            if (!names.Add(name.TrimStart('@'))) throw new ArgumentException("A managed enum name is declared more than once.");
            (string carrier, int bytes, bool signed) = Integral(definition.underlyingType);
            if (definition.values.IsDefault) throw new ArgumentException("Managed enum values cannot be an uninitialized collection.");
            BigInteger minimum = signed ? -(BigInteger.One << (bytes * 8 - 1)) : BigInteger.Zero;
            BigInteger maximum = (BigInteger.One << (bytes * 8 - (signed ? 1 : 0))) - 1;
            HashSet<string> members = new(StringComparer.Ordinal) { name.TrimStart('@') };
            ImmutableArray<CSharpEnumValuePlan>.Builder values = ImmutableArray.CreateBuilder<CSharpEnumValuePlan>();
            foreach (ManagedEnumValue value in definition.values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(value);
                string member = CSharpNames.Identifier(value.name);
                if (!members.Add(member.TrimStart('@'))) throw new ArgumentException("A managed enum member name is duplicated or conflicts with its type.");
                if (value.value is null || value.value.Length > 128
                    || !BigInteger.TryParse(value.value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger number)
                    || number < minimum || number > maximum)
                    throw new ArgumentException("A managed enum value is not a decimal integer within its declared fixed-width range.");
                string literal = number.ToString(CultureInfo.InvariantCulture) + (carrier == "ulong" ? "UL" : carrier == "long" ? "L" : carrier == "uint" ? "U" : string.Empty);
                values.Add(new(member, literal) { documentation = value.documentation });
            }
            plans.Add(new(name, carrier, bytes, signed, values.ToImmutable(), definition.isFlags) { documentation = definition.documentation });
        }
        return plans.ToImmutable();
    }

    internal static (string Carrier, int Bytes, bool Signed) Integral(ManagedIntegralKind kind) => kind switch
    {
        ManagedIntegralKind.Int8 => ("sbyte", 1, true),
        ManagedIntegralKind.UInt8 => ("byte", 1, false),
        ManagedIntegralKind.Int16 => ("short", 2, true),
        ManagedIntegralKind.UInt16 => ("ushort", 2, false),
        ManagedIntegralKind.Int32 => ("int", 4, true),
        ManagedIntegralKind.UInt32 => ("uint", 4, false),
        ManagedIntegralKind.Int64 => ("long", 8, true),
        ManagedIntegralKind.UInt64 => ("ulong", 8, false),
        _ => throw new ArgumentException("A managed enum has an unknown integral representation.")
    };
}
