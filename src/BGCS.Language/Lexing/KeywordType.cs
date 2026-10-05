namespace BGCS.Language.Lexing
{
    /// <summary>
    /// Classifies reserved spellings recognized by the supported authoring syntax analyzers.
    /// </summary>
    public enum KeywordType : ushort
    {
        /// <summary>
        /// A keyword outside the recognized syntax subset.
        /// </summary>
        Unknown = 0,
        /// <summary>
        /// Public access modifier.
        /// </summary>
        Public,
        /// <summary>
        /// Assembly access modifier.
        /// </summary>
        Internal,
        /// <summary>
        /// Derived type access modifier.
        /// </summary>
        Protected,
        /// <summary>
        /// Declaring type access modifier.
        /// </summary>
        Private,
        /// <summary>
        /// Static member or type modifier.
        /// </summary>
        Static,
        /// <summary>
        /// Read-only field or value type modifier.
        /// </summary>
        Readonly,
        /// <summary>
        /// Unsafe code context modifier.
        /// </summary>
        Unsafe,
        /// <summary>
        /// Compile-time constant modifier.
        /// </summary>
        Const,
        /// <summary>
        /// Reference type declaration keyword.
        /// </summary>
        Class,
        /// <summary>
        /// Value type declaration keyword.
        /// </summary>
        Struct,
        /// <summary>
        /// Namespace import or resource scope keyword.
        /// </summary>
        Using,
        /// <summary>
        /// Namespace declaration keyword.
        /// </summary>
        Namespace,
        /// <summary>
        /// Conditional branch keyword.
        /// </summary>
        If,
        /// <summary>
        /// Alternative branch keyword.
        /// </summary>
        Else,
        /// <summary>
        /// Condition-controlled loop keyword.
        /// </summary>
        While,
        /// <summary>
        /// Function return keyword.
        /// </summary>
        Return,
        /// <summary>
        /// Indexed loop keyword.
        /// </summary>
        For,
        /// <summary>
        /// Sequence iteration keyword.
        /// </summary>
        Foreach,
        /// <summary>
        /// No-value return type keyword.
        /// </summary>
        Void,
        /// <summary>
        /// Inferred local type keyword.
        /// </summary>
        Var,
        /// <summary>
        /// Boolean true literal keyword.
        /// </summary>
        True,
        /// <summary>
        /// Boolean false literal keyword.
        /// </summary>
        False,
    }
}
