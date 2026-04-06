namespace BGCS
{
    using System;

    [Flags]
    /// <summary>
    /// Defines values for <c>NumberParseOptions</c>.
    /// </summary>
    public enum NumberParseOptions
    {
        None = 0,
        AllowBrackets = 1,
        AllowHex = 2,
        AllowMinus = 4,
        AllowExponent = 8,
        AllowSuffix = 16,

        All = AllowBrackets | AllowHex | AllowMinus | AllowExponent | AllowSuffix,
    }
}
