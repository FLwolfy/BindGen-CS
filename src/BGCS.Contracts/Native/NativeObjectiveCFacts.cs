using System;
using System.Collections.Immutable;
using BGCS.Contracts.Common;

namespace BGCS.Contracts.Native;

/// <summary>Identifies an Objective-C declaration container without implying a message dispatch ABI.</summary>
public enum NativeObjectiveCContainerKind
{
    /// <summary>The compiler did not establish a container category.</summary>
    Unknown,
    /// <summary>An Objective-C interface declaration.</summary>
    Interface,
    /// <summary>An Objective-C protocol declaration.</summary>
    Protocol,
    /// <summary>A named or anonymous interface category.</summary>
    Category
}

/// <summary>Identifies the receiver domain of an Objective-C selector.</summary>
public enum NativeObjectiveCDispatchKind
{
    /// <summary>The receiver domain is unknown.</summary>
    Unknown,
    /// <summary>The selector is an instance method.</summary>
    Instance,
    /// <summary>The selector is a class method.</summary>
    Class
}

/// <summary>Preserves compiler-declared property attributes without inferring managed ownership or ARC behavior.</summary>
[Flags]
public enum NativeObjectiveCPropertyAttributes
{
    /// <summary>No property attributes were reported.</summary>
    None = 0,
    /// <summary>The property is declared readonly.</summary>
    ReadOnly = 1,
    /// <summary>An explicit getter selector is declared.</summary>
    Getter = 2,
    /// <summary>The assign attribute is declared.</summary>
    Assign = 4,
    /// <summary>The property is declared readwrite.</summary>
    ReadWrite = 8,
    /// <summary>The retain attribute is declared.</summary>
    Retain = 16,
    /// <summary>The copy attribute is declared.</summary>
    Copy = 32,
    /// <summary>The nonatomic attribute is declared.</summary>
    Nonatomic = 64,
    /// <summary>An explicit setter selector is declared.</summary>
    Setter = 128,
    /// <summary>The atomic attribute is declared.</summary>
    Atomic = 256,
    /// <summary>The weak attribute is declared.</summary>
    Weak = 512,
    /// <summary>The strong attribute is declared.</summary>
    Strong = 1024,
    /// <summary>The unsafe_unretained attribute is declared.</summary>
    UnsafeUnretained = 2048,
    /// <summary>The class attribute is declared.</summary>
    Class = 4096
}

/// <summary>Stores detached Objective-C container relationships independently of C++ inheritance and templates.</summary>
/// <param name="kind">The compiler-established declaration category.</param>
/// <param name="evidence">The origin of these declaration facts, without runtime certification.</param>
public sealed record NativeObjectiveCContainer(
    NativeObjectiveCContainerKind kind,
    Evidence evidence
)
{
    /// <summary>Gets implemented or inherited protocol declaration identities in source order.</summary>
    public ImmutableArray<StableId> protocols { get; init; } = [];
    /// <summary>Gets Objective-C generic parameter type identities in source order.</summary>
    public ImmutableArray<StableId> genericParameters { get; init; } = [];
    /// <summary>Gets the base interface declaration, or null when absent.</summary>
    public StableId? baseInterface { get; init; }
    /// <summary>Gets the interface extended by a category, or null for another container kind.</summary>
    public StableId? categoryTarget { get; init; }
    /// <summary>Gets the category name, including an empty name for a class extension.</summary>
    public string? categoryName { get; init; }
}

/// <summary>Stores a source selector and its receiver domain without treating it as a C export.</summary>
/// <param name="selector">The complete native selector, including colons.</param>
/// <param name="dispatch">The compiler-established receiver domain.</param>
/// <param name="evidence">The origin of these declaration facts.</param>
public sealed record NativeObjectiveCMethod(
    string selector,
    NativeObjectiveCDispatchKind dispatch,
    Evidence evidence
);

/// <summary>Stores an Objective-C property's exact accessor relationships; its value type remains NativeDeclaration.type.</summary>
/// <param name="getterSelector">The compiler-provided getter selector.</param>
/// <param name="setterSelector">The compiler-provided setter selector, or null when absent.</param>
/// <param name="evidence">The origin of these declaration facts.</param>
public sealed record NativeObjectiveCProperty(
    string getterSelector,
    string? setterSelector,
    Evidence evidence
)
{
    /// <summary>Gets compiler-declared property flags; null means unavailable or containing an unrecognized attribute.</summary>
    public NativeObjectiveCPropertyAttributes? attributes { get; init; }
    /// <summary>Gets the detached getter declaration, or null if no declaration was available.</summary>
    public StableId? getter { get; init; }
    /// <summary>Gets the detached setter declaration, or null if no declaration was available.</summary>
    public StableId? setter { get; init; }
}
