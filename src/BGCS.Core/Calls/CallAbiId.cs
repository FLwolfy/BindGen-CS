namespace BGCS.Core.Calls;

/// <summary>Identifies a registered native/managed calling-ABI implementation.</summary>
/// <param name="value">The open stable provider identity.</param>
public readonly record struct CallAbiId(string value)
{
    /// <summary>Returns the provider identity.</summary>
    /// <returns>The identity text.</returns>
    public override string ToString() => value;
}
