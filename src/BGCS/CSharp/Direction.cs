namespace BGCS.CSharp
{
    /// <summary>
    /// Describes the inferred direction of data crossing a native parameter boundary.
    /// </summary>
    public enum Direction
    {
        /// <summary>
        /// The parameter carries an input value.
        /// </summary>
        In = 0,
        /// <summary>
        /// The parameter receives an output value.
        /// </summary>
        Out = 1,
        /// <summary>
        /// The parameter supplies and receives a value.
        /// </summary>
        InOut = 2,
    }
}
