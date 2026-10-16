using BGCS.Contracts.Common;
using BGCS.Contracts.Interop;

namespace BGCS.Contracts.Bridge;

/// <summary>Selects a finite variant-access protocol for explicitly configured native families.</summary>
public enum CppVariantAccess
{
    /// <summary>No custom variant-access protocol is selected.</summary>
    None,
    /// <summary>Uses a const member template get&lt;I&gt; after checking the native active index.</summary>
    MemberGet
}

/// <summary>Selects the actual native string member protocol without assuming standard container members.</summary>
public enum CppStringAccess
{
    /// <summary>Uses counted size and indexed read/write operations.</summary>
    Indexed,
    /// <summary>Reads c_str with a bounded scan and independently declared validity.</summary>
    CStrNullTerminated
}

/// <summary>Declares behavioral facts that a native c_str signature cannot prove.</summary>
public sealed record CppCStrProtocol
{
    /// <summary>Gets the explicit readable lifetime during each native access call.</summary>
    public required LifetimeContract readableLifetime { get; init; }
    /// <summary>Gets the user assertion that c_str returns a readable zero-terminated sequence within the bound.</summary>
    public required Evidence nullTerminated { get; init; }
    /// <summary>Gets the maximum number of units that may be inspected, including the terminator.</summary>
    public int maximumCodeUnits { get; init; } = 1024 * 1024;
    /// <summary>Gets the optional assertion that the native pointer constructor copies its argument before returning.</summary>
    public Evidence? constructorCopiesInput { get; init; }
}

/// <summary>Opts one exact native template head or named String/Path type into a checked value protocol without changing storage identity.</summary>
public sealed record CppFamilyBinding
{
    /// <summary>Gets the exact qualified class-template name without template arguments, globs or alias reinterpretation.</summary>
    public string? nativeTemplate { get; init; }
    /// <summary>Gets one exact qualified non-template String/Path class or named standard basic_string alias; mutually exclusive with nativeTemplate.</summary>
    public string? nativeType { get; init; }
    /// <summary>Gets the selected finite value protocol; the native type retains its original storage identity.</summary>
    public required StandardValueKind family { get; init; }
    /// <summary>Gets the explicit user assertion that the native operations satisfy the selected protocol.</summary>
    public required Evidence evidence { get; init; }
    /// <summary>Gets the explicit string encoding; required for String and forbidden for other families.</summary>
    public NativeTextEncoding stringEncoding { get; init; }
    /// <summary>Gets the finite string access protocol; unrelated families use Indexed and no cStr contract.</summary>
    public CppStringAccess stringAccess { get; init; }
    /// <summary>Gets the explicit null-terminated read and optional copying-constructor protocol.</summary>
    public CppCStrProtocol? cStr { get; init; }
    /// <summary>Gets the explicit member-access protocol; required only for a configured Variant.</summary>
    public CppVariantAccess variantAccess { get; init; }
    /// <summary>Gets the explicit path-byte conversion; required only for a configured Path.</summary>
    public NativePathFormat? pathFormat { get; init; }
}
