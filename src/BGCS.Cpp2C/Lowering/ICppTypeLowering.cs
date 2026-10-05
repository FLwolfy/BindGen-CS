using BGCS.CppAst.Model.Types;

namespace BGCS.Cpp2C.Lowering;

/// <summary>Extends bridge type lowering without modifying the generator.</summary>
public interface ICppTypeLowering
{
    /// <summary>
    /// Stable registration name, unique within this extension category.
    /// </summary>
    string name { get; }

    /// <summary>
    /// Selection priority; higher values are tried first and equal priorities use ordinal name order.
    /// </summary>
    int priority { get; }

    /// <summary>
    /// Determines whether this extension handles the native declaration in the supplied context.
    /// </summary>
    /// <param name="type">Native declaration borrowed from the current parser compilation.</param>
    /// <param name="context">Current generation and declaration context.</param>
    /// <returns>True when CreatePlan can produce a complete decision; otherwise false.</returns>
    bool CanLower(
        CppType type,
        CppTypeLoweringContext context
    );
    /// <summary>
    /// Creates a complete conversion decision after CanLower has accepted the declaration.
    /// </summary>
    /// <param name="type">Native declaration borrowed from the current parser compilation.</param>
    /// <param name="context">Current generation and declaration context.</param>
    /// <returns>A non-null decision identifying this extension and its required ABI and safety semantics.</returns>
    CppTypeLoweringPlan CreatePlan(
        CppType type,
        CppTypeLoweringContext context
    );
}
