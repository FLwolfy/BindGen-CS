using System.Collections.Immutable;

namespace BGCS.Contracts.Requests;

/// <summary>Identifies a source declaration family without depending on a frontend's AST kinds.</summary>
public enum NativeDeclarationCategory
{
    /// <summary>Functions, members and aliases to functions.</summary>
    Callables,
    /// <summary>Structs, unions and classes.</summary>
    Records,
    /// <summary>Native enumerations.</summary>
    Enumerations,
    /// <summary>Scalar or string constants, including object-like macros.</summary>
    Constants,
    /// <summary>Type aliases other than callable pointer typedefs.</summary>
    TypeAliases,
    /// <summary>Typedefs naming native function pointers.</summary>
    Callbacks
}

/// <summary>Restricts one public declaration family independently of all other families.</summary>
/// <param name="category">The exact native declaration family.</param>
public sealed record DeclarationCategorySelection(NativeDeclarationCategory category)
{
    /// <summary>Gets exact eligible public identities within this family; empty leaves its ordinary roots unrestricted.</summary>
    public ImmutableArray<string> includeDeclarations { get; init; } = [];
    /// <summary>Gets exact excluded identities within this family; mandatory storage and cleanup conflicts still fail.</summary>
    public ImmutableArray<string> excludeDeclarations { get; init; } = [];
}

/// <summary>Restricts public declarations while preserving all facts required by their native storage and cleanup.</summary>
public sealed record DeclarationSelectionRequest
{
    /// <summary>Gets independent family allowlists and exclusions, combined with the global positive filters; each family occurs at most once.</summary>
    public ImmutableArray<DeclarationCategorySelection> categories { get; init; } = [];
    /// <summary>Gets exact declaring header paths eligible to supply public roots; an empty list uses the source header policy.</summary>
    public ImmutableArray<string> includeHeaders { get; init; } = [];
    /// <summary>Gets exact declaring header paths excluded from public roots; required storage or cleanup from them causes a conflict.</summary>
    public ImmutableArray<string> excludeHeaders { get; init; } = [];
    /// <summary>Gets exact stable IDs, qualified declarations or unambiguous source callable signatures eligible as public roots.</summary>
    public ImmutableArray<string> includeDeclarations { get; init; } = [];
    /// <summary>Gets exact public exclusions; required storage or cleanup causes a conflict while nonexported behavior metadata remains available.</summary>
    public ImmutableArray<string> excludeDeclarations { get; init; } = [];
    /// <summary>Gets ordinary public root families; empty includes every family. Required dependencies and explicit adapter requests remain available.</summary>
    public ImmutableArray<NativeDeclarationCategory> includeCategories { get; init; } = [];
    /// <summary>Gets strict family exclusions; a selected operation requiring excluded storage or cleanup fails with its dependency chain.</summary>
    public ImmutableArray<NativeDeclarationCategory> excludeCategories { get; init; } = [];
}
