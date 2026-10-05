using BGCS.CppAst.Model.Declarations;

namespace BGCS.Cpp2C.Lowering;

/// <summary>Extends callable selection, exported naming, and invocation lowering.</summary>
public interface ICppCallableLowering
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
    /// <param name="function">Native declaration borrowed from the current parser compilation.</param>
    /// <param name="context">Current generation and declaration context.</param>
    /// <returns>True when CreatePlan can produce a complete decision; otherwise false.</returns>
    bool CanLower(
        CppFunction function,
        CppCallableLoweringContext context
    );
    /// <summary>
    /// Creates a complete conversion decision after CanLower has accepted the declaration.
    /// </summary>
    /// <param name="function">Native declaration borrowed from the current parser compilation.</param>
    /// <param name="context">Current generation and declaration context.</param>
    /// <returns>A non-null decision identifying this extension and its required ABI and safety semantics.</returns>
    CppCallableLoweringPlan CreatePlan(
        CppFunction function,
        CppCallableLoweringContext context
    );
}
