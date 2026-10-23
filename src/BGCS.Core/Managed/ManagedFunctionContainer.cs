using System.Collections.Immutable;

namespace BGCS.Core.Managed;

/// <summary>Names a context-bound view of existing checked friendly operations.</summary>
/// <param name="name">The public view property name.</param>
/// <param name="methods">Existing native operations with names local to this view.</param>
public sealed record ManagedFunctionContainer(
    string name,
    ImmutableArray<ManagedFunctionAlias> methods
);

/// <summary>Selects a finite named view without introducing additional native imports or module state.</summary>
/// <param name="name">The public view property name.</param>
/// <param name="functions">Exact source function selectors and view-local method names.</param>
public sealed record ManagedFunctionContainerRequest(
    string name,
    ImmutableArray<ManagedFunctionAliasRequest> functions
);
