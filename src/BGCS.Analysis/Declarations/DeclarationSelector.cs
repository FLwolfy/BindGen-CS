using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using BGCS.Analysis.Interop;
using BGCS.Analysis.Bridge;
using BGCS.Analysis.Types;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;

namespace BGCS.Analysis.Declarations;

/// <summary>Separates public selection from the native facts required by selected declarations.</summary>
public sealed class DeclarationSelector
{
    private readonly IEqualityComparer<string> m_paths;
    /// <summary>Captures explicitly established header path identity comparisons without selecting host rules.</summary>
    /// <param name="headerPathComparer">The caller's comparer for normalized source path identities.</param>
    public DeclarationSelector(IEqualityComparer<string> headerPathComparer)
        => m_paths = headerPathComparer ?? throw new ArgumentNullException(nameof(headerPathComparer));
    /// <summary>Validates typed family choices before any source is parsed.</summary>
    /// <param name="request">The immutable source selection.</param>
    /// <exception cref="ArgumentException">A list is uninitialized, duplicated, undefined or contradictory.</exception>
    public static void ValidateCategories(DeclarationSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (ImmutableArray<NativeDeclarationCategory> categories in new[] { request.includeCategories, request.excludeCategories })
            if (categories.IsDefault || categories.Any(category => !Enum.IsDefined(category)) || categories.Distinct().Count() != categories.Length)
                throw new ArgumentException("Source category lists must be initialized, defined and unique.", nameof(request));
        if (request.includeCategories.Intersect(request.excludeCategories).Any())
            throw new ArgumentException("A source category cannot be both included and excluded.", nameof(request));
        if (request.categories.IsDefault) throw new ArgumentException("Source family rules must be initialized.", nameof(request));
        HashSet<NativeDeclarationCategory> families = [];
        foreach (DeclarationCategorySelection rule in request.categories)
        {
            if (rule is null || !Enum.IsDefined(rule.category) || !families.Add(rule.category))
                throw new ArgumentException("Source family rules must be nonnull, defined and unique.", nameof(request));
            foreach (ImmutableArray<string> selectors in new[] { rule.includeDeclarations, rule.excludeDeclarations })
            {
                if (selectors.IsDefault) throw new ArgumentException("Source family selector lists must be initialized.", nameof(request));
                HashSet<string> exact = new(StringComparer.Ordinal);
                foreach (string selector in selectors)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(selector);
                    if (!exact.Add(selector)) throw new ArgumentException("A source family selector is repeated.", nameof(request));
                }
            }
            if (rule.includeDeclarations.Intersect(rule.excludeDeclarations, StringComparer.Ordinal).Any())
                throw new ArgumentException("A source family declaration cannot be both included and excluded.", nameof(request));
            if (!rule.includeDeclarations.IsEmpty && request.excludeCategories.Contains(rule.category))
                throw new ArgumentException("An excluded source family cannot also declare an allowlist.", nameof(request));
        }
    }

    /// <summary>Selects entry-header APIs while preserving their complete detached type and storage closure.</summary>
    /// <param name="module">The detached native graph.</param>
    /// <param name="entryHeaders">The exact source entry paths.</param>
    /// <param name="baseDirectory">The captured logical path base.</param>
    /// <param name="cancellationToken">Cancels dependency traversal.</param>
    /// <returns>The selected graph and exact export/root identities.</returns>
    public DeclarationSelection SelectEntries(
        NativeModule module,
        IEnumerable<string> entryHeaders,
        string baseDirectory,
        CancellationToken cancellationToken = default
    ) => Select(module, new(), entryHeaders, baseDirectory,
            includeTransitivelyReferencedHeaders: false, cancellationToken: cancellationToken);

    /// <summary>Applies exact selectors and refuses exclusion of required native storage or cleanup.</summary>
    /// <param name="module">The complete detached native graph.</param>
    /// <param name="request">The immutable exact header/declaration choices.</param>
    /// <param name="entryHeaders">The source entry paths used by entry-only policy.</param>
    /// <param name="baseDirectory">The captured base for header paths and source locations.</param>
    /// <param name="includeTransitivelyReferencedHeaders">Whether dependency headers may supply ordinary public roots.</param>
    /// <param name="includeSystemDeclarations">Whether SDK declarations may supply public roots.</param>
    /// <param name="requiredDeclarations">Explicit cleanup/dependency selectors that must remain available.</param>
    /// <param name="cancellationToken">Cancels selection and closure traversal.</param>
    /// <param name="additionalPublicDeclarations">Explicit template, shim or adapter requests, still subject to every exclusion.</param>
    /// <param name="ordinaryDeclarations">The identities eligible for ordinary selection when a reparse also contains private generator helpers.</param>
    /// <param name="requiredMetadataDeclarations">Required adapter facts that must survive exclusions without being promoted to callable exports.</param>
    /// <param name="includeCHeaderHelpers">Whether ordinary C source helpers and their aliases may request adapters compiled into the configured native module; explicitly generated adapters remain enabled.</param>
    /// <param name="requiredNativeTypes">Exact compiler-established metadata types needed by explicitly requested adapters, without promoting their private probe aliases to public roots.</param>
    /// <returns>The closed native facts, public roots and required callable identities.</returns>
    public DeclarationSelection Select(
        NativeModule module,
        DeclarationSelectionRequest request,
        IEnumerable<string> entryHeaders,
        string baseDirectory,
        bool includeTransitivelyReferencedHeaders = true,
        bool includeSystemDeclarations = false,
        IEnumerable<string>? requiredDeclarations = null,
        CancellationToken cancellationToken = default,
        IEnumerable<string>? additionalPublicDeclarations = null,
        IEnumerable<StableId>? ordinaryDeclarations = null,
        IEnumerable<string>? requiredMetadataDeclarations = null,
        bool includeCHeaderHelpers = false,
        IEnumerable<StableId>? requiredNativeTypes = null
    ) {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(request);
        ValidateCategories(request);
        ArgumentNullException.ThrowIfNull(entryHeaders);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        foreach (ImmutableArray<string> values in new[] { request.includeHeaders, request.excludeHeaders, request.includeDeclarations, request.excludeDeclarations })
            if (values.IsDefault) throw new ArgumentException("Source selection lists must be initialized immutable arrays.", nameof(request));
        baseDirectory = Path.GetFullPath(baseDirectory);
        IEqualityComparer<string> paths = m_paths;
        HashSet<string> entries = HeaderPaths(entryHeaders, "entry headers");
        if (entries.Count == 0) throw new ArgumentException("At least one entry header is required.", nameof(entryHeaders));
        HashSet<string> includedHeaders = HeaderPaths(request.includeHeaders, "includeHeaders");
        HashSet<string> excludedHeaders = HeaderPaths(request.excludeHeaders, "excludeHeaders");
        if (includedHeaders.Overlaps(excludedHeaders)) throw new ArgumentException("An exact header cannot be both included and excluded.");
        Dictionary<string, NativeDeclaration> byId = module.declarations.ToDictionary(declaration => declaration.id.value, StringComparer.Ordinal);
        Dictionary<string, NativeDeclaration[]> byName = module.declarations.SelectMany(declaration => Names(declaration).Select(name => (Name: name, Declaration: declaration)))
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Declaration).DistinctBy(declaration => declaration.id).ToArray(), StringComparer.Ordinal);
        Dictionary<string, NativeDeclaration[]>? bySignature = null;
        HashSet<StableId> included = ResolveList(request.includeDeclarations, "includeDeclarations");
        HashSet<StableId> excluded = ResolveList(request.excludeDeclarations, "excludeDeclarations");
        if (included.Overlaps(excluded)) throw new ArgumentException("An exact declaration cannot be both included and excluded.");
        Dictionary<NativeDeclarationCategory, HashSet<StableId>> familyIncludes = [];
        Dictionary<NativeDeclarationCategory, HashSet<StableId>> familyExcludes = [];
        foreach (DeclarationCategorySelection rule in request.categories)
        {
            HashSet<StableId> allowed = ResolveList(rule.includeDeclarations, rule.category + " includeDeclarations", rule.category);
            HashSet<StableId> denied = ResolveList(rule.excludeDeclarations, rule.category + " excludeDeclarations", rule.category);
            foreach (StableId id in allowed.Concat(denied))
                if (Category(module.GetDeclaration(id)) != rule.category)
                    throw new ArgumentException($"Source family '{rule.category}' selected a different declaration family: '{Display(id)}'.");
            if (allowed.Overlaps(denied) || allowed.Overlaps(excluded) || denied.Overlaps(included))
                throw new ArgumentException("Global and family source selectors cannot both include and exclude the same declaration.");
            familyIncludes.Add(rule.category, allowed);
            familyExcludes.Add(rule.category, denied);
        }
        HashSet<StableId> required = requiredDeclarations is null ? [] : ResolveList(requiredDeclarations.Distinct(StringComparer.Ordinal), "required declarations");
        HashSet<StableId> explicitRoots = additionalPublicDeclarations is null ? [] : ResolveList(additionalPublicDeclarations.Distinct(StringComparer.Ordinal), "explicit generation requests");
        foreach (StableId id in included.Concat(familyIncludes.Values.SelectMany(values => values)).Concat(required))
            if (OmittedHeaderHelper(module.GetDeclaration(id))) throw HeaderHelperOptIn(module.GetDeclaration(id));
        HashSet<StableId>? ordinary = ordinaryDeclarations?.ToHashSet();
        if (ordinary is not null && ordinary.Any(id => !byId.ContainsKey(id.value)))
            throw new InvalidOperationException("A native reparse lost an original declaration identity before source selection.");
        Dictionary<StableId, string?> sourcePaths = [];
        NativeDeclaration[] roots = module.declarations.Where(CanBePublic).Where(OrdinaryAliasRoot).Where(declaration => !OmittedHeaderHelper(declaration)).Where(declaration =>
            (request.includeCategories.IsEmpty || Category(declaration) is NativeDeclarationCategory category && request.includeCategories.Contains(category))
            && (ordinary is null || ordinary.Contains(declaration.id)) &&
            (includeSystemDeclarations || !declaration.isSystemDeclaration)
            && (includedHeaders.Count != 0 ? SourcePath(declaration) is string file && includedHeaders.Contains(file)
                : includeTransitivelyReferencedHeaders || SourcePath(declaration) is string entry && entries.Contains(entry))
            && (included.Count == 0 || Included(declaration))
            && (Category(declaration) is not NativeDeclarationCategory family || !familyIncludes.TryGetValue(family, out HashSet<StableId>? allowed)
                || allowed.Count == 0 || allowed.Contains(declaration.id))
            && Exclusion(declaration) is null).ToArray();
        foreach (StableId id in included.Concat(explicitRoots))
            if (!CanBePublic(module.GetDeclaration(id)) && module.GetDeclaration(id).kind != "Namespace")
                throw new ArgumentException($"Source selection '{Display(id)}' cannot be a public root; select its containing callable, enum or record.");
        foreach (StableId id in explicitRoots)
            if (Exclusion(module.GetDeclaration(id)) is string excludedBy)
                throw new InvalidOperationException($"Source selection conflicts with an explicit generation request '{Display(id)}': {excludedBy}. Remove the request or its exclusion.");
        NativeDeclaration[] explicitPublic = module.declarations.Where(CanBePublic)
            .Where(declaration => Within(declaration, explicitRoots) && Exclusion(declaration) is null).ToArray();
        roots = roots.Concat(explicitPublic).DistinctBy(declaration => declaration.id).ToArray();
        HashSet<StableId> callableIds = roots.Where(declaration => declaration.kind == "Function").Select(declaration => declaration.id).ToHashSet();
        foreach (StableId id in required)
        {
            NativeDeclaration declaration = module.GetDeclaration(id);
            if (declaration.kind == "Function")
            {
                if (!CanBePublic(declaration)) throw new InvalidOperationException($"Required cleanup '{Display(id)}' is deleted, inaccessible or has internal linkage.");
                callableIds.Add(id);
            }
        }
        if (requiredMetadataDeclarations is not null)
            required.UnionWith(ResolveList(requiredMetadataDeclarations.Distinct(StringComparer.Ordinal), "required adapter facts"));
        Dictionary<(bool Type, StableId Id), bool> processed = [];
        Dictionary<(bool Type, StableId Id), (bool Type, StableId Id)?> trace = [];
        Stack<(bool Type, StableId Id, bool Required)> pending = [];
        foreach (NativeDeclaration declaration in roots) Push(false, declaration.id, true, null);
        foreach (StableId id in required) Push(false, id, true, null);
        if (requiredNativeTypes is not null)
            foreach (StableId id in requiredNativeTypes.Distinct())
            {
                _ = module.GetType(id);
                Push(true, id, true, null);
            }
        while (pending.TryPop(out var next))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = (next.Type, next.Id);
            if (processed.TryGetValue(key, out bool wasRequired) && (wasRequired || !next.Required)) continue;
            processed[key] = next.Required;
            if (next.Type)
            {
                NativeType type = module.GetType(next.Id);
                AddType(type.elementType);
                AddType(type.canonicalType);
                AddType(type.returnType);
                foreach (StableId parameter in type.parameterTypes) AddType(parameter);
                if (type.declaration is StableId declaration) Push(false, declaration, next.Required, key);
            }
            else
            {
                NativeDeclaration declaration = module.GetDeclaration(next.Id);
                if (next.Required && Exclusion(declaration) is string excludedBy)
                    throw new InvalidOperationException($"Source selection conflicts with required native storage or cleanup: {excludedBy}; dependency chain: {Chain(key)}. Remove the exclusion or supply an explicit external/native shim contract.");
                AddType(declaration.type);
                AddType(declaration.sourceType);
                AddType(declaration.returnType);
                if (declaration.macro?.functionAlias is StableId macroFunction)
                {
                    NativeDeclaration function = module.GetDeclaration(macroFunction);
                    if (OmittedHeaderHelper(declaration)) throw HeaderHelperOptIn(declaration);
                    if (!CanBePublic(function)) throw new InvalidOperationException($"Macro alias '{declaration.qualifiedName}' targets an inaccessible native function.");
                    Push(false, macroFunction, true, key);
                    callableIds.Add(macroFunction);
                }
                if (declaration.functionReference is StableId functionReference) Push(false, functionReference, true, key);
                if (declaration.parent is StableId parent) Push(false, parent, next.Required, key);
                foreach (StableId childId in declaration.children)
                {
                    NativeDeclaration child = module.GetDeclaration(childId);
                    if (child.kind is "Field" or "Parameter" or "EnumItem") Push(false, childId, next.Required, key);
                    // Keep callable metadata needed to distinguish a DTO, declared
                    // constructors and destructor access. Ordinary members are not
                    // promoted to exports by this metadata-only dependency edge.
                    else if (declaration.kind == "Class" && child.kind == "Function")
                        Push(false, childId, next.Required && child.role == "Destructor", key);
                    // Public member aliases are neutral protocol facts (for example
                    // a non-template string's value_type), not additional API roots.
                    // Keep the edge bounded to required project-owned class facts;
                    // do not traverse every dependent alias in a system SDK.
                    else if (next.Required && declaration.kind == "Class" && !declaration.isSystemDeclaration
                        && child.kind == "Typedef" && child.visibility == "Public")
                        Push(false, childId, true, key);
                }
                foreach (NativeBaseClass baseClass in declaration.baseClasses) AddType(baseClass.type);
                foreach (NativeTemplateArgument argument in declaration.templateArguments) AddType(argument.type);
                foreach (StableId parameter in declaration.templateParameters) AddType(parameter);
            }
            void AddType(StableId? id) { if (id is StableId value) Push(true, value, next.Required, key); }
        }
        NativeModule selected = new(module.targetId, module.triple, module.pointerSize,
            module.types.Where(type => processed.ContainsKey((true, type.id))).ToImmutableArray(),
            module.declarations.Where(declaration => processed.ContainsKey((false, declaration.id))).Select(declaration => declaration with
            { children = declaration.children.Where(child => processed.ContainsKey((false, child))).ToImmutableArray() }).ToImmutableArray(), module.diagnostics)
        { language = module.language, byteOrder = module.byteOrder };
        var failures = new NativeModuleVerifier().Validate(selected).Where(diagnostic => diagnostic.isFailure).ToArray();
        if (failures.Length != 0) throw new InvalidOperationException("Source selection lost a required native fact: " + string.Join("; ", failures.Select(error => error.message)));
        return new(selected, callableIds.OrderBy(id => id.value, StringComparer.Ordinal).ToImmutableArray())
        { publicDeclarations = roots.Select(declaration => declaration.id).OrderBy(id => id.value, StringComparer.Ordinal).ToImmutableArray() };

        void Push(
            bool type,
            StableId id,
            bool requiredFact,
            (bool Type, StableId Id)? from
        ) {
            var key = (type, id);
            if (!trace.ContainsKey(key) || requiredFact && processed.TryGetValue(key, out bool previous) && !previous) trace[key] = from;
            pending.Push((type, id, requiredFact));
        }
        HashSet<string> HeaderPaths(
            IEnumerable<string> inputs,
            string label
        ) {
            HashSet<string> result = new(paths);
            foreach (string input in inputs)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(input);
                if (!result.Add(Path.GetFullPath(input, baseDirectory))) throw new ArgumentException($"Source selection {label} repeats an exact normalized header path.");
            }
            return result;
        }
        HashSet<StableId> ResolveList(
            IEnumerable<string> selectors,
            string label,
            NativeDeclarationCategory? family = null
        ) {
            HashSet<StableId> result = [];
            foreach (string selector in selectors)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(selector);
                string identity = selector.TrimStart(':');
                NativeDeclaration[] candidates;
                bool exactIdentity = false;
                if (byId.TryGetValue(selector, out NativeDeclaration? exact))
                {
                    candidates = [exact];
                    exactIdentity = true;
                }
                else if (byName.TryGetValue(identity, out NativeDeclaration[]? named)) candidates = named;
                else
                {
                    if (bySignature is null)
                    {
                        List<(string Signature, NativeDeclaration Declaration)> signatures = [];
                        foreach (NativeDeclaration callable in module.declarations.Where(declaration => declaration.kind == "Function"))
                        {
                            try { signatures.Add((new NativeTypeFormatter().CallableSignature(module, callable), callable)); }
                            catch (NotSupportedException) { } // Unselected dependent signatures cannot be invented.
                        }
                        bySignature = signatures.GroupBy(item => item.Signature, StringComparer.Ordinal)
                            .ToDictionary(group => group.Key, group => group.Select(item => item.Declaration).ToArray(), StringComparer.Ordinal);
                    }
                    candidates = bySignature.GetValueOrDefault(identity) ?? [];
                }
                // C commonly gives a tag and its typedef the same name. A typed
                // family rule resolves only within that family; stable IDs still
                // identify their exact declaration and retain the wrong-family check.
                if (family is NativeDeclarationCategory expected && !exactIdentity && candidates.Length != 0)
                {
                    NativeDeclaration[] scoped = candidates.Where(candidate => Category(candidate) == expected).ToArray();
                    if (scoped.Length == 0)
                        throw new ArgumentException($"Source family '{expected}' selected a different declaration family: '{selector}'.");
                    candidates = scoped;
                }
                if (candidates.Length != 1)
                    throw new ArgumentException(candidates.Length == 0 ? $"Source selection {label} '{selector}' matches no native declaration. Use inspect to copy an exact stable ID or sourceSignature."
                        : $"Source selection {label} '{selector}' is ambiguous across {candidates.Length} declarations. Use an exact stable ID or sourceSignature from inspect.");
                if (!result.Add(candidates[0].id)) throw new ArgumentException($"Source selection {label} selects one declaration more than once.");
            }
            return result;
        }
        IEnumerable<string> Names(NativeDeclaration declaration)
        {
            string name = declaration.qualifiedName.TrimStart(':');
            yield return name;
            if (declaration.kind == "Function" && declaration.parent is StableId parent && module.GetDeclaration(parent) is { kind: "Class" } owner)
            {
                string concrete = owner.qualifiedName.TrimStart(':') + "::" + declaration.name;
                if (concrete != name) yield return concrete;
            }
        }
        bool CanBePublic(NativeDeclaration declaration) => declaration.kind == "Macro" ? declaration.macro?.isActive != false && Accessible(declaration)
            : declaration.kind == "Field" && declaration.role == "Variable" && (declaration.constantValue is not null || declaration.stringConstant is not null) ? Accessible(declaration)
            : declaration.kind is "Class" or "Enum" or "Typedef"
            ? Accessible(declaration)
            : declaration.kind == "Function" && !declaration.isDeleted && declaration.templateParameters.IsEmpty && declaration.templateKind != "FunctionTemplate"
                && Accessible(declaration) && ConcreteOwner(declaration) && (module.language != SourceLanguage.C || !declaration.isStatic || declaration.isDefinition);
        bool ConcreteOwner(NativeDeclaration declaration)
        {
            HashSet<StableId> visited = [];
            while (declaration.parent is StableId parent && visited.Add(parent))
            {
                declaration = module.GetDeclaration(parent);
                if (!declaration.templateParameters.IsEmpty) return false;
            }
            return true;
        }
        bool Included(NativeDeclaration declaration) => Within(declaration, included);
        bool OmittedHeaderHelper(NativeDeclaration declaration)
        {
            if (module.language != SourceLanguage.C || includeCHeaderHelpers || Within(declaration, explicitRoots)) return false;
            if (CHeaderFunctionPolicy.RequiresAdapter(declaration)) return true;
            StableId? target = declaration.macro?.functionAlias ?? declaration.functionReference;
            return target is StableId id && CHeaderFunctionPolicy.RequiresAdapter(module.GetDeclaration(id)) && !Within(module.GetDeclaration(id), explicitRoots);
        }
        static InvalidOperationException HeaderHelperOptIn(NativeDeclaration declaration) => new(
            $"C source helper '{declaration.qualifiedName}' was explicitly selected or required for cleanup. Enable source.includeHeaderHelpers and compile the emitted helper source into the configured native module, or select an independently exported native function.");
        NativeDeclarationCategory? Category(NativeDeclaration declaration) => declaration.kind switch
        {
            "Function" => NativeDeclarationCategory.Callables,
            "Class" => NativeDeclarationCategory.Records,
            "Enum" => NativeDeclarationCategory.Enumerations,
            "Macro" when declaration.macro?.functionAlias is not null => NativeDeclarationCategory.Callables,
            "Macro" => NativeDeclarationCategory.Constants,
            "Field" when declaration.role == "Variable" && declaration.functionReference is not null => NativeDeclarationCategory.Callables,
            "Field" when declaration.role == "Variable" => NativeDeclarationCategory.Constants,
            "Typedef" => CallbackAlias(declaration) ? NativeDeclarationCategory.Callbacks : NativeDeclarationCategory.TypeAliases,
            _ => null
        };
        bool CallbackAlias(NativeDeclaration declaration)
        {
            if (declaration.type is not StableId identity) return false;
            HashSet<StableId> visited = [];
            NativeType type = module.GetType(identity);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified && type.elementType is StableId aliasElement && visited.Add(type.id))
                type = module.GetType(aliasElement);
            if (type.category != NativeTypeCategory.Pointer || type.elementType is not StableId pointee) return false;
            type = module.GetType(pointee);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified && type.elementType is StableId callbackElement && visited.Add(type.id))
                type = module.GetType(callbackElement);
            return type.category == NativeTypeCategory.Function;
        }
        bool OrdinaryAliasRoot(NativeDeclaration declaration)
        {
            if (declaration.kind != "Typedef" || declaration.type is not StableId id) return true;
            // An explicitly requested alias keeps its own dependency contract.
            if (included.Contains(declaration.id) || request.includeCategories.Contains(NativeDeclarationCategory.TypeAliases)
                || familyIncludes.TryGetValue(NativeDeclarationCategory.TypeAliases, out HashSet<StableId>? aliases) && aliases.Contains(declaration.id))
                return true;
            NativeType type = module.GetType(id);
            HashSet<StableId> visited = [];
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified
                && type.elementType is StableId underlying && visited.Add(type.id))
                type = module.GetType(underlying);
            if (type.category is not (NativeTypeCategory.Record or NativeTypeCategory.Enumeration)
                || type.declaration is not StableId tagId) return true;
            NativeDeclaration tag = module.GetDeclaration(tagId);
            if (tag.kind is not ("Class" or "Enum")) return true;
            if (!string.Equals(declaration.qualifiedName.TrimStart(':'), tag.qualifiedName.TrimStart(':'), StringComparison.Ordinal)) return true;
            // `typedef struct Name {...} Name` cannot undo a tag-family filter.
            // Without such a filter the alias remains an ordinary root, including
            // a public-header typedef whose definition came from another header.
            // Actual uses and explicit aliases still enter required-fact closure.
            NativeDeclarationCategory family = type.category == NativeTypeCategory.Record ? NativeDeclarationCategory.Records : NativeDeclarationCategory.Enumerations;
            return (request.includeCategories.IsEmpty || request.includeCategories.Contains(family))
                && !request.excludeCategories.Contains(family) && !Within(tag, excluded)
                && (!familyIncludes.TryGetValue(family, out HashSet<StableId>? allowed) || allowed.Count == 0 || allowed.Contains(tag.id))
                && (!familyExcludes.TryGetValue(family, out HashSet<StableId>? denied) || !denied.Contains(tag.id));
        }
        bool Within(
            NativeDeclaration declaration,
            HashSet<StableId> choices
        ) {
            HashSet<StableId> visited = [];
            while (true)
            {
                if (choices.Contains(declaration.id)) return true;
                if (declaration.parent is not StableId parent || !visited.Add(parent)) return false;
                NativeDeclaration owner = module.GetDeclaration(parent);
                if (owner.kind is not ("Class" or "Namespace")) return false;
                declaration = owner;
            }
        }
        string? Exclusion(NativeDeclaration declaration)
        {
            HashSet<StableId> visited = [];
            if (declaration.kind != "Namespace" && SourcePath(declaration) is string path && excludedHeaders.Contains(path))
                return "excluded header '" + path + "'";
            while (true)
            {
                if (excluded.Contains(declaration.id)) return "excluded declaration '" + Display(declaration.id) + "'";
                if (Category(declaration) is NativeDeclarationCategory family && familyExcludes.TryGetValue(family, out HashSet<StableId>? denied)
                    && denied.Contains(declaration.id))
                    return "excluded family declaration '" + Display(declaration.id) + "' in '" + family + "'";
                if (Category(declaration) is NativeDeclarationCategory category && request.excludeCategories.Contains(category))
                    return "excluded category '" + category + "' at '" + Display(declaration.id) + "'";
                if (declaration.parent is not StableId parent || !visited.Add(parent)) return null;
                declaration = module.GetDeclaration(parent);
            }
        }
        string? SourcePath(NativeDeclaration declaration)
        {
            if (!sourcePaths.TryGetValue(declaration.id, out string? path))
                sourcePaths.Add(declaration.id, path = string.IsNullOrEmpty(declaration.source.file) ? null : Path.GetFullPath(declaration.source.file, baseDirectory));
            return path;
        }
        bool Accessible(NativeDeclaration declaration)
        {
            HashSet<StableId> visited = [];
            while (true)
            {
                if (!visited.Add(declaration.id)) throw new InvalidOperationException("Source selection encountered a cyclic declaration parent graph.");
                if (declaration.visibility is "Private" or "Protected") return false;
                if (declaration.parent is not StableId parent) return true;
                declaration = module.GetDeclaration(parent);
            }
        }
        string Display(StableId id)
        {
            NativeDeclaration declaration = module.GetDeclaration(id);
            return declaration.kind == "Function" ? new NativeTypeFormatter().CallableSignature(module, declaration) : declaration.qualifiedName;
        }
        string Chain((bool Type, StableId Id) key)
        {
            List<string> chain = [];
            HashSet<(bool Type, StableId Id)> visited = [];
            while (visited.Add(key))
            {
                chain.Add(key.Type ? "type '" + module.GetType(key.Id).spelling + "'" : "declaration '" + Display(key.Id) + "'");
                if (!trace.TryGetValue(key, out var parent) || parent is null) break;
                key = parent.Value;
            }
            chain.Reverse();
            return string.Join(" -> ", chain);
        }
    }
}

/// <summary>Preserves required facts and cleanup without promoting metadata-only dependency callables.</summary>
/// <param name="module">The closed selected graph.</param>
/// <param name="callables">The exact selected native exports, including explicitly required cleanup.</param>
public sealed record DeclarationSelection(
    NativeModule module,
    ImmutableArray<StableId> callables
)
{
    /// <summary>Gets public roots independently from dependency-only declarations and cleanup.</summary>
    public ImmutableArray<StableId> publicDeclarations { get; init; } = [];
}
