namespace BGCS.Cpp2C.Lowering;

/// <summary>Location in which a C++ type is being lowered.</summary>
public enum CppTypeLoweringUse
{
    /// <summary>
    /// A stored native field.
    /// </summary>
    Field,
    /// <summary>
    /// An argument supplied to a native callable.
    /// </summary>
    Parameter,
    /// <summary>
    /// A value returned by a native callable.
    /// </summary>
    Return
}
