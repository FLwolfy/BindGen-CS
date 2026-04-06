namespace BGCS.FunctionGeneration.ParameterWriters
{
    /// <summary>
    /// Defines the public struct <c>ParameterPriorityComparer</c>.
    /// </summary>
    public readonly struct ParameterPriorityComparer : IComparer<IParameterWriter>
    {
        /// <summary>
        /// Executes public operation <c>Compare</c>.
        /// </summary>
        public int Compare(IParameterWriter? x, IParameterWriter? y)
        {
            if (x == null || y == null)
            {
                return 0;
            }

            return y.Priority.CompareTo(x.Priority);
        }
    }
}
