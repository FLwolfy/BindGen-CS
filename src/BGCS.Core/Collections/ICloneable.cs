namespace BGCS.Core.Collections;

/// <summary>
/// Creates independent values for metadata transformations that must preserve their inputs.
/// </summary>
/// <typeparam name="T">The value produced by a clone.</typeparam>
public interface ICloneable<T>
{
    /// <summary>
    /// Creates an independent copy of this value.
    /// </summary>
    /// <returns>The cloned value; mutation of the original does not mutate the clone.</returns>
    T Clone();
}
