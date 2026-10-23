namespace BGCS.Core.Native;

/// <summary>Describes a source-language member reference qualification independently of a compiler API.</summary>
public enum NativeReferenceQualifier
{
    /// <summary>The member has no reference qualifier.</summary>
    None,
    /// <summary>The member is qualified for an lvalue object.</summary>
    LValue,
    /// <summary>The member is qualified for an rvalue object.</summary>
    RValue,
    /// <summary>The frontend could not establish the member reference qualification.</summary>
    Unknown
}
