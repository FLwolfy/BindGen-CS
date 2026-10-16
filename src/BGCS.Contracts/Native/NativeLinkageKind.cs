namespace BGCS.Contracts.Native;

/// <summary>Describes a declaration's native linker visibility independently of its language linkage.</summary>
public enum NativeLinkageKind
{
    /// <summary>The frontend did not establish linker visibility.</summary>
    Unknown,
    /// <summary>The declaration has no linkage.</summary>
    None,
    /// <summary>The declaration is local to its native translation unit.</summary>
    Internal,
    /// <summary>The declaration has a unique externally linked identity.</summary>
    UniqueExternal,
    /// <summary>The declaration can be linked across translation units.</summary>
    External
}
