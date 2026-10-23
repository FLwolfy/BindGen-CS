using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
using BGCS.Analysis.Safety;

namespace BGCS.CSharp.Projection;

/// <summary>Chooses managed names and framework scalar identities independently of import syntax.</summary>
public sealed class DefaultManagedProjection : IManagedProjection
{
    /// <inheritdoc/>
    public ManagedApiModel Project(
        InteropModule module,
        ManagedProjectionRequest request,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.@namespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.name);
        ManagedNamingResolver.Validate(request.naming);
        FlatIdentifierWordIndex? namingWords = FlatIdentifierWordIndex.Create(request.naming);
        new BGCS.Analysis.Interop.NativeFunctionAliasVerifier().Verify(module);
        if (request.functionAliases.IsDefault) throw new ArgumentException("Managed function aliases must be initialized.");
        if (request.functionContainers.IsDefault) throw new ArgumentException("Managed function containers must be initialized.");
        if (request.enums.IsDefault) throw new ArgumentException("Managed enum declarations cannot be an uninitialized collection.");
        ArgumentNullException.ThrowIfNull(request.enumMappings);
        HashSet<string> names = new(StringComparer.Ordinal);
        Func<InteropType, string> typeSource = ManagedNameResolver.CreateTypeSourceResolver(module);
        ImmutableArray<ManagedType>.Builder types = ImmutableArray.CreateBuilder<ManagedType>();
        foreach (InteropType type in module.types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? framework = FrameworkType(type);
            ManagedNameRule typeRule = NativeCallableAliasFacts.Resolve(module, type) is not null ? request.naming.callbacks : type.category switch
            {
                NativeTypeCategory.Enumeration => request.naming.enumerations,
                NativeTypeCategory.Function => request.naming.callbacks,
                _ => request.naming.types
            };
            string sourceName = typeSource(type);
            string name = framework ?? Name(sourceName, type.id.value, typeRule);
            if (type.category is NativeTypeCategory.Record or NativeTypeCategory.Enumeration)
                if (!names.Add(name))
                    throw new InvalidOperationException($"Managed type name '{name}' is selected more than once.");
            types.Add(new(type.id, name, framework)
            {
                isNameExplicit = request.names.ContainsKey(type.id.value) || request.names.ContainsKey(sourceName),
                memberNames = type.fields.Select(field => (field.id, field.name)).Concat(type.enumValues.Select(value => (value.id, value.name)))
                    .Where(member => !string.IsNullOrEmpty(member.name)).Select(member => new ManagedMemberName(member.id, MemberName(type, member.name))).ToImmutableArray()
            });
        }
        ImmutableArray<ManagedMember> methods = module.functions.Select(function => new ManagedMember(function.id,
            ManagedNameResolver.Callable(module, function, request, namingWords), function.parameters.Select((
                parameter,
                index
            ) =>
                ParameterName(function, parameter, index)).ToImmutableArray())
        {
            stringInputs = function.parameters.Where(parameter => parameter.text is not null && new FriendlyApiEligibilityAnalyzer().GetTextInputRestriction(module, function, parameter) is null)
                .Select(parameter => parameter.id).ToImmutableArray(),
            bufferInputs = function.parameters.Where(parameter => parameter.buffer is not null && new BufferProtocolAnalyzer().GetRestriction(module, function, parameter) is null)
                .Select(parameter => parameter.id).ToImmutableArray(),
            valuePointers = function.parameters.Where(parameter => parameter.singleValue is not null && new ValueParameterAnalyzer().GetRestriction(module, function, parameter) is null
                && !function.parameters.Any(buffer => buffer.buffer?.writtenCountParameter == parameter.id))
                .Select(parameter => parameter.id).ToImmutableArray()
        }).ToImmutableArray();
        methods = ManagedFunctionCustomization.Apply(methods, request.functions, ResolveAlias);
        ManagedApiModel model = new(request.@namespace, request.name, request.style, module, types.ToImmutable(), methods)
        {
            nestGeneratedTypesInApi = request.nestGeneratedTypesInApi,
            emitNativeMetadata = request.emitNativeMetadata,
            emitCallbackDelegates = request.emitCallbackDelegates,
            emitPlaceholderComments = request.emitPlaceholderComments,
            constants = module.constants.Select(constant => new ManagedConstant(constant.sourceDeclaration,
            Name(constant.qualifiedName, constant.sourceDeclaration.value, request.naming.constants))).Concat(module.stringConstants.Select(constant =>
                new ManagedConstant(constant.sourceDeclaration, Name(constant.qualifiedName, constant.sourceDeclaration.value, request.naming.constants)))).ToImmutableArray(),
            aliases = module.functionAliases.Select(alias => new ManagedFunctionAlias(alias.nativeFunction, Name(alias.qualifiedName, alias.sourceDeclaration.value, request.naming.aliases))
            { documentation = alias.documentation, documentationContent = alias.documentationContent })
                .Concat(request.functionAliases.Select(alias => new ManagedFunctionAlias(ResolveAlias(alias.function).id, alias.name)
                { documentation = alias.documentation, documentationContent = alias.documentationContent })).ToImmutableArray(),
            defaultOverloadLimit = request.defaultOverloadLimit,
            functionContainers = request.functionContainers.Select(container => new ManagedFunctionContainer(container.name,
                container.functions.Select(function => new ManagedFunctionAlias(ResolveAlias(function.function).id, function.name)
                { documentation = function.documentation, documentationContent = function.documentationContent }).ToImmutableArray())).ToImmutableArray()
        };
        model = ManagedRecordCustomization.Apply(model, request.records);
        model = ManagedConstantCustomization.Apply(model, request);
        model = ManagedBooleanProjection.Apply(model);
        model = ManagedReceiverProjection.Apply(model, request.memberFunctions, ResolveAlias);
        model = ManagedPointerProjection.Apply(model, request, ResolveAlias);
        return request.enums.IsDefaultOrEmpty && request.enumMappings.IsEmpty ? model
            : new ManagedEnumProjection().Apply(model, request, cancellationToken);

        string Name(
            string source,
            string identity,
            ManagedNameRule rule
        ) {
            if (request.names.TryGetValue(identity, out string? configured) || request.names.TryGetValue(source, out configured))
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(configured);
                return configured;
            }
            string fallback = ManagedNameResolver.Identifier(source, identity);
            return ManagedNamingResolver.Apply(string.IsNullOrWhiteSpace(source) ? fallback : source, fallback, rule, request.naming, wordIndex: namingWords);
        }

        InteropFunction ResolveAlias(string selector)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(selector);
            InteropFunction[] exports = module.functions.Where(function => function.exportName == selector || function.id.value == selector).ToArray();
            if (exports.Length == 1) return exports[0];
            var sourceAliases = module.functionAliases.Where(alias => alias.qualifiedName == selector || alias.name == selector)
                .Select(alias => alias.nativeFunction).Distinct().ToArray();
            if (sourceAliases.Length == 1) return module.functions.Single(function => function.id == sourceAliases[0]);
            if (sourceAliases.Length > 1) throw new ArgumentException("A source alias selector identifies multiple native operations: " + selector);
            InteropFunction[] selected = module.functions.Where(function => function.inputAdapterOf is null
                && (function.sourceSignature == selector || function.sourceQualifiedName == selector)).ToArray();
            if (selected.Length != 1) throw new ArgumentException("A function alias must select exactly one existing export or primary source signature: " + selector);
            return selected[0];
        }

        string MemberName(
            InteropType owner,
            string sourceName
        ) {
            string qualified = (owner.sourceQualifiedName ?? owner.declarationName ?? owner.nativeName).TrimStart(':') + "::" + sourceName;
            if (request.names.TryGetValue(qualified, out string? configured) || request.names.TryGetValue(sourceName, out configured))
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(configured);
                return configured;
            }
            ManagedNameRule rule = request.naming.fields;
            if (owner.category == NativeTypeCategory.Enumeration)
                rule = request.naming.enumerationValues.TryGetValue(owner.sourceQualifiedName ?? owner.declarationName ?? owner.nativeName, out ManagedNameRule? enumerationRule)
                    ? enumerationRule : request.naming.enumerators;
            return ManagedNamingResolver.Apply(sourceName, sourceName, rule, request.naming, preserveSourceByDefault: true, wordIndex: namingWords);
        }

        string ParameterName(
            InteropFunction owner,
            InteropParameter parameter,
            int index
        ) {
            string sourceName = parameter.sourceName ?? parameter.name;
            foreach (string ownerName in new[] { owner.sourceSignature, owner.sourceQualifiedName, owner.exportName }.Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Distinct(StringComparer.Ordinal))
                if (request.names.TryGetValue(ownerName.TrimStart(':') + "::" + sourceName, out string? configured))
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(configured);
                    return configured;
                }
            string ordinary = string.IsNullOrEmpty(sourceName) ? "arg" + index : sourceName;
            return ManagedNamingResolver.Apply(ordinary, ordinary, request.naming.parameters, request.naming, preserveSourceByDefault: true, wordIndex: namingWords);
        }
    }

    private static string? FrameworkType(InteropType type)
    {
        if (type.category != NativeTypeCategory.Primitive)
            return null;
        if (type.primitiveKind == "Void") return "System.Void";
        if (type.primitiveKind == "Float" && type.size == 4) return "System.Single";
        if (type.primitiveKind == "Double" && type.size == 8) return "System.Double";
        if (type.primitiveKind == "Bool" && type.size == 1) return "System.Byte";
        if (type.isSigned is bool signed)
            return (type.size, signed) switch
            {
                (1, true) => "System.SByte",
                (1, false) => "System.Byte",
                (2, true) => "System.Int16",
                (2, false) => "System.UInt16",
                (4, true) => "System.Int32",
                (4, false) => "System.UInt32",
                (8, true) => "System.Int64",
                (8, false) => "System.UInt64",
                _ => null
            };
        return null;
    }
}
