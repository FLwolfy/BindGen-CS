using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BGCS.Contracts.Common;
using BGCS.Contracts.Managed;
using BGCS.Contracts.Requests;
using BGCS.CSharp.Emission;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BGCS.CSharp.Planning;

/// <summary>Checks asserted managed constant values independently of unchanged native facts and ABI carriers.</summary>
internal static class ConstantPresentationPlanner
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> m_frameworkReferences = new(FrameworkReferences);
    internal static ImmutableArray<MetadataReference> frameworkReferences => m_frameworkReferences.Value;

    internal static CSharpEmissionPlan Apply(
        CSharpEmissionPlan plan,
        EmissionRequest request,
        CancellationToken cancellationToken
    ) {
        var constants = plan.constants.ToBuilder();
        var strings = plan.stringConstants.ToBuilder();
        var storage = plan.storage.ToBuilder();
        HashSet<StableId> valueAssertions = [];
        HashSet<StableId> carrierAssertions = [];
        HashSet<(StableId Type, string Member)> enumAssertions = [];
        bool compile = false;
        foreach (ManagedConstant constant in plan.api.constants)
        {
            if (constant.presentation is not { } presentation) continue;
            Validate(presentation);
            int scalarIndex = Find(constants, item => item.sourceDeclaration == constant.sourceDeclaration);
            int stringIndex = Find(strings, item => item.sourceDeclaration == constant.sourceDeclaration);
            if ((scalarIndex >= 0) == (stringIndex >= 0)) throw new ArgumentException("A constant presentation requires exactly one checked native source representation.");
            if (presentation.managedType is null && presentation.value is null && presentation.expression is null)
            {
                if (scalarIndex >= 0) constants[scalarIndex] = constants[scalarIndex] with { documentation = presentation.documentation ?? constants[scalarIndex].documentation };
                else strings[stringIndex] = strings[stringIndex] with { documentation = presentation.documentation ?? strings[stringIndex].documentation };
                continue;
            }
            compile = true;
            carrierAssertions.Add(constant.sourceDeclaration);
            CSharpConstantPlan source = scalarIndex >= 0 ? constants[scalarIndex]
                : new(constant.sourceDeclaration, strings[stringIndex].name, strings[stringIndex].type, strings[stringIndex].expression, strings[stringIndex].isUnicodeString)
                { documentation = strings[stringIndex].documentation };
            string type = presentation.managedType ?? source.type;
            TypeSyntax syntax = SyntaxFactory.ParseTypeName(type);
            if (syntax.ContainsDiagnostics || syntax.ToFullString() != type || syntax.ContainsDirectives
                || syntax.DescendantTrivia(descendIntoTrivia: true).Any(trivia => trivia.IsKind(SyntaxKind.SkippedTokensTrivia)))
                throw new ArgumentException("A managed constant carrier requires one complete type, not source statements.");
            string expression = Value(presentation, type, source.expression, plan);
            bool changedValue = presentation.value is not null || presentation.expression is not null;
            CSharpConstantPlan updated = source with
            {
                type = type,
                expression = expression,
                isCompileTimeConstant = changedValue || source.isCompileTimeConstant,
                documentation = presentation.documentation ?? source.documentation,
                presentationEvidence = presentation.evidence
            };
            if (scalarIndex >= 0) constants[scalarIndex] = updated;
            else { strings.RemoveAt(stringIndex); constants.Add(updated); }
            if (changedValue || updated.isCompileTimeConstant) valueAssertions.Add(constant.sourceDeclaration);
        }
        foreach (ManagedType type in plan.api.types)
        {
            if (type.enumPresentation is not { } presentation) continue;
            if (presentation.values.IsDefault) throw new ArgumentException("Native enum presentations must contain initialized member rules.");
            int index = Find(storage, item => item.nativeType == type.nativeType);
            if (index < 0 || storage[index].enumUnderlyingType is not string underlying)
                throw new ArgumentException("An enum presentation cannot replace a non-enum native storage carrier.");
            var native = plan.api.interop.GetType(type.nativeType);
            var values = storage[index].enumValues.ToBuilder();
            HashSet<StableId> identities = [];
            foreach (ManagedNativeEnumValuePresentation member in presentation.values)
            {
                if (!identities.Add(member.nativeMember)) throw new ArgumentException("A native enum member has duplicate asserted presentation.");
                Validate(member.presentation);
                int position = Enumerable.Range(0, native.enumValues.Length).SingleOrDefault(i => native.enumValues[i].id == member.nativeMember, -1);
                if (position < 0) throw new ArgumentException("An enum presentation references an absent native member.");
                CSharpEnumValuePlan previous = values[position];
                bool changed = member.presentation.value is not null || member.presentation.expression is not null;
                values[position] = previous with
                {
                    value = Value(member.presentation, underlying, previous.value, plan),
                    documentation = member.presentation.documentation ?? previous.documentation,
                    presentationEvidence = member.presentation.evidence
                };
                if (changed) { compile = true; enumAssertions.Add((type.nativeType, previous.name.TrimStart('@'))); }
            }
            storage[index] = storage[index] with { documentation = presentation.documentation ?? storage[index].documentation, enumValues = values.ToImmutable() };
        }
        CSharpEmissionPlan result = plan with { constants = constants.ToImmutable(), stringConstants = strings.ToImmutable(), storage = storage.ToImmutable() };
        if (!compile) return result;
        CSharpCompilation first = Compile(result, request, cancellationToken);
        Dictionary<string, (string Type, object? Value)> expected = [];
        Dictionary<StableId, string> expectedCarriers = [];
        constants = result.constants.ToBuilder();
        for (int index = 0; index < constants.Count; ++index)
        {
            CSharpConstantPlan constant = constants[index];
            if (!carrierAssertions.Contains(constant.sourceDeclaration)) continue;
            IFieldSymbol symbol = Field(first, MetadataNamespace(result) + "." + result.contextName.TrimStart('@') + "+Constants", constant.name,
                requireConstant: valueAssertions.Contains(constant.sourceDeclaration));
            string carrier = CanonicalType(symbol.Type);
            expectedCarriers[constant.sourceDeclaration] = carrier;
            constants[index] = constant with { type = carrier };
            if (!valueAssertions.Contains(constant.sourceDeclaration)) continue;
            expected["constant:" + constant.sourceDeclaration.value] = (symbol.Type.ToDisplayString(), symbol.ConstantValue);
            constants[index] = constants[index] with { expression = Literal(symbol.ConstantValue, symbol.Type) };
        }
        storage = result.storage.ToBuilder();
        for (int index = 0; index < storage.Count; ++index)
        {
            if (storage[index].enumUnderlyingType is null) continue;
            var values = storage[index].enumValues.ToBuilder();
            for (int position = 0; position < values.Count; ++position)
            {
                var member = values[position];
                if (!enumAssertions.Contains((storage[index].nativeType, member.name.TrimStart('@')))) continue;
                IFieldSymbol symbol = EnumMember(first, result, storage[index], member.name);
                expected["enum:" + storage[index].nativeType.value + ":" + member.name] = (symbol.Type.ToDisplayString(), symbol.ConstantValue);
                values[position] = member with { value = Integral(symbol.ConstantValue) };
            }
            storage[index] = storage[index] with { enumValues = values.ToImmutable() };
        }
        result = result with { constants = constants.ToImmutable(), storage = storage.ToImmutable() };
        CSharpCompilation canonical = Compile(result, request, cancellationToken);
        foreach (CSharpConstantPlan constant in result.constants.Where(constant => carrierAssertions.Contains(constant.sourceDeclaration)))
            if (CanonicalType(Field(canonical, MetadataNamespace(result) + "." + result.contextName.TrimStart('@') + "+Constants", constant.name, requireConstant: false).Type)
                != expectedCarriers[constant.sourceDeclaration]) throw new ArgumentException("Canonical constant emission changed its asserted managed carrier.");
        foreach (CSharpConstantPlan constant in result.constants.Where(constant => valueAssertions.Contains(constant.sourceDeclaration)))
            Verify(expected["constant:" + constant.sourceDeclaration.value], Constant(canonical, result, constant.name));
        foreach (CSharpStoragePlan enumeration in result.storage.Where(item => item.enumUnderlyingType is not null))
            foreach (CSharpEnumValuePlan member in enumeration.enumValues.Where(member => enumAssertions.Contains((enumeration.nativeType, member.name.TrimStart('@')))))
                Verify(expected["enum:" + enumeration.nativeType.value + ":" + member.name], EnumMember(canonical, result, enumeration, member.name));
        return result;
    }

    private static string Value(
        ManagedConstantPresentation presentation,
        string type,
        string fallback,
        CSharpEmissionPlan plan
    ) {
        string expression = presentation.value is not null ? ManagedDefaultValuePlanner.Expression(presentation.value, type, plan) : presentation.expression ?? fallback;
        ExpressionSyntax syntax = SyntaxFactory.ParseExpression(expression, options: new CSharpParseOptions(LanguageVersion.CSharp14), consumeFullText: true);
        if (expression.Length > 16 * 1024 || syntax.ContainsDiagnostics || syntax.ContainsDirectives || syntax.ToFullString() != expression
            || syntax.DescendantTrivia(descendIntoTrivia: true).Any(trivia => trivia.IsKind(SyntaxKind.SkippedTokensTrivia))
            || syntax.DescendantNodesAndSelf().Take(4097).Count() > 4096)
            throw new ArgumentException("A constant assertion requires one bounded expression without directives or statements.");
        return expression;
    }

    private static void Validate(ManagedConstantPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (presentation.evidence?.level != EvidenceLevel.UserAsserted || string.IsNullOrWhiteSpace(presentation.evidence.origin))
            throw new ArgumentException("Managed constant presentation must retain explicit UserAsserted provenance, never Verified native evidence.");
        if (presentation.value is not null && presentation.expression is not null)
            throw new ArgumentException("A typed constant value and expression are mutually exclusive.");
    }

    private static CSharpCompilation Compile(
        CSharpEmissionPlan plan,
        EmissionRequest request,
        CancellationToken cancellationToken
    ) {
        StringBuilder source = new("#nullable enable\n");
        ManagedConstantCompilerInputs inputs = ManagedConstantCompilerInputs.Create(request.managedCompilation, m_frameworkReferences.Value, cancellationToken);
        CSharpCompilation framework = CSharpCompilation.Create("BGCS.ConstantPresentation.Framework", inputs.sources, inputs.references);
        foreach (string ns in request.additionalUsings.Where(ns => KnownNamespace(framework.GlobalNamespace, ns)
            || plan.api.@namespace == ns || plan.api.@namespace.StartsWith(ns + ".", StringComparison.Ordinal)))
            source.Append("using global::").Append(CSharpNames.Namespace(ns)).AppendLine(";");
        source.Append("namespace ").Append(CSharpNames.Namespace(plan.api.@namespace)).AppendLine(";");
        StringBuilder declarations = new();
        foreach (CSharpStoragePlan storage in plan.storage.Where(storage => storage.enumUnderlyingType is not null)) declarations.Append(AbiStorageEmitter.Emit(storage));
        foreach (CSharpManagedEnumPlan enumeration in plan.managedEnums) declarations.Append(ManagedEnumEmitter.Emit(enumeration));
        foreach (CSharpStoragePlan storage in plan.storage.Where(storage => storage.presentation?.emitSizeConstant == true))
            declarations.Append("public struct ").Append(storage.name).Append(" { public const int NativeSize = ").Append(storage.size).AppendLine("; }");
        source.Append(CSharpGeneratedTypeScope.WrapDeclarations(plan.api, declarations.ToString()));
        source.Append("public sealed unsafe partial class ").Append(plan.contextName).AppendLine(" {").Append(LibraryNameEmitter.Emit(plan, request.libraryName)).AppendLine("}");
        source.Append(ConstantEmitter.Emit(plan));
        if (plan.api.style == ManagedApiStyle.Static)
        {
            source.Append("public static partial class ").Append(CSharpNames.Identifier(plan.api.name)).AppendLine(" {").Append(LibraryNameEmitter.Emit(plan, request.libraryName))
                .AppendLine("public static class Constants {");
            foreach (CSharpConstantPlan constant in plan.constants) source.AppendLine(ConstantEmitter.Member(constant));
            foreach (CSharpStringConstantPlan constant in plan.stringConstants) source.AppendLine(ConstantEmitter.Member(constant));
            source.AppendLine("} }");
        }
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source.ToString(), inputs.parseOptions, "bgcs-internal:/constant-assertions.g.cs", cancellationToken: cancellationToken);
        CSharpCompilation compilation = CSharpCompilation.Create("BGCS.ConstantPresentation.Validation", inputs.sources.Add(tree), inputs.references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, generalDiagnosticOption: ReportDiagnostic.Error,
                nullableContextOptions: NullableContextOptions.Enable));
        inputs.VerifyAssemblyClosure(compilation, cancellationToken);
        Diagnostic[] failures = ManagedConstantCompilerInputs.ConstantFailures(compilation, tree, plan, cancellationToken);
        if (failures.Length != 0) throw new ArgumentException("Asserted constant/enum presentation failed warnings-as-errors compile-time validation: "
            + string.Join("; ", failures.Select(diagnostic => diagnostic.ToString())));
        return compilation;
    }

    private static IFieldSymbol Constant(
        CSharpCompilation compilation,
        CSharpEmissionPlan plan,
        string name
    )
        => Field(compilation, MetadataNamespace(plan) + "." + plan.contextName.TrimStart('@') + "+Constants", name);
    private static IFieldSymbol EnumMember(
        CSharpCompilation compilation,
        CSharpEmissionPlan plan,
        CSharpStoragePlan storage,
        string name
    )
        => Field(compilation, MetadataNamespace(plan) + "." + (plan.api.nestGeneratedTypesInApi ? plan.api.name.TrimStart('@') + "+" : string.Empty) + storage.name.TrimStart('@'), name);
    private static string MetadataNamespace(CSharpEmissionPlan plan) => string.Join(".", plan.api.@namespace.Split('.').Select(part => part.TrimStart('@')));
    private static IFieldSymbol Field(
        CSharpCompilation compilation,
        string type,
        string name,
        bool requireConstant = true
    ) {
        IFieldSymbol? field = compilation.GetTypeByMetadataName(type)?.GetMembers(name.TrimStart('@')).OfType<IFieldSymbol>().SingleOrDefault();
        if (field is null || HasError(field.Type) || requireConstant && (!field.IsConst || !field.HasConstantValue))
            throw new ArgumentException("An asserted presentation requires a resolved carrier and compiler-established managed constant value: " + type + "." + name);
        return field;
    }
    private static bool HasError(ITypeSymbol type) => type.TypeKind == TypeKind.Error
        || type is INamedTypeSymbol named && named.TypeArguments.Any(HasError)
        || type is IArrayTypeSymbol array && HasError(array.ElementType)
        || type is IPointerTypeSymbol pointer && HasError(pointer.PointedAtType);
    private static string CanonicalType(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
        SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
    private static void Verify(
        (string Type, object? Value) expected,
        IFieldSymbol actual
    ) {
        bool equal = expected.Value is float first && actual.ConstantValue is float second ? BitConverter.SingleToInt32Bits(first) == BitConverter.SingleToInt32Bits(second)
            : expected.Value is double left && actual.ConstantValue is double right ? BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right)
            : Equals(expected.Value, actual.ConstantValue);
        if (actual.Type.ToDisplayString() != expected.Type || !equal) throw new ArgumentException("Canonical constant emission changed its asserted managed value or carrier.");
    }
    private static string Literal(
        object? value,
        ITypeSymbol type
    ) {
        if (value is null) return "null";
        if (type.TypeKind == TypeKind.Enum) return "(" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")(" + Integral(value) + ")";
        return value switch
        {
            string text => SymbolDisplay.FormatLiteral(text, true),
            char character => SymbolDisplay.FormatLiteral(character, true),
            bool flag => flag ? "true" : "false",
            float number => float.IsNaN(number) ? "(0.0F / 0.0F)" : float.IsPositiveInfinity(number) ? "(1.0F / 0.0F)" : float.IsNegativeInfinity(number) ? "(-1.0F / 0.0F)" : number.ToString("R", CultureInfo.InvariantCulture) + "F",
            double number => double.IsNaN(number) ? "(0.0D / 0.0D)" : double.IsPositiveInfinity(number) ? "(1.0D / 0.0D)" : double.IsNegativeInfinity(number) ? "(-1.0D / 0.0D)" : number.ToString("R", CultureInfo.InvariantCulture) + "D",
            decimal number => number.ToString(CultureInfo.InvariantCulture) + "M",
            _ => Integral(value)
        };
    }
    private static string Integral(object? value) => value switch
    {
        sbyte number => number.ToString(CultureInfo.InvariantCulture),
        byte number => number.ToString(CultureInfo.InvariantCulture),
        short number => number.ToString(CultureInfo.InvariantCulture),
        ushort number => number.ToString(CultureInfo.InvariantCulture),
        int number => number.ToString(CultureInfo.InvariantCulture),
        uint number => number.ToString(CultureInfo.InvariantCulture) + "U",
        long number => number.ToString(CultureInfo.InvariantCulture) + "L",
        ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
        _ => throw new ArgumentException("An enum assertion requires an exact fixed-width integral value.")
    };
    private static int Find<T>(
        IList<T> values,
        Func<T, bool> predicate
    ) { for (int index = 0; index < values.Count; ++index) if (predicate(values[index])) return index; return -1; }
    private static ImmutableArray<MetadataReference> FrameworkReferences()
    {
        string directory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string[] assemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Framework metadata for constant validation is unavailable."))
            .Split(Path.PathSeparator).Where(path => string.Equals(Path.GetDirectoryName(path), directory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).ToArray();
        return assemblies.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();
    }

    private static bool KnownNamespace(
        INamespaceSymbol root,
        string name
    ) {
        INamespaceSymbol? current = root;
        foreach (string part in name.Split('.'))
            current = current?.GetNamespaceMembers().SingleOrDefault(item => item.Name == part.TrimStart('@'));
        return current is not null;
    }
}
