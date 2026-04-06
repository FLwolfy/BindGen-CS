namespace BGCS.Runtime
{
    using System;

    /// <summary>
    /// Identifies the kind of native element represented by <see cref="NativeNameAttribute"/>.
    /// </summary>
    public enum NativeNameType
    {
        Type,
        Field,
        StructOrClass,
        Typedef,
        Enum,
        EnumItem,
        Func,
        Param,
        Const,
        Delegate,
        Value
    }

    /// <summary>
    /// Associates generated managed members with their original native names.
    /// </summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class NativeNameAttribute : Attribute
    {
        /// <summary>
        /// Initializes an attribute instance with a native name and default type.
        /// </summary>
        /// <param name="name">Original native identifier.</param>
        public NativeNameAttribute(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Initializes an attribute instance with explicit native element type and name.
        /// </summary>
        /// <param name="type">Category of native element.</param>
        /// <param name="name">Original native identifier.</param>
        public NativeNameAttribute(NativeNameType type, string name)
        {
            Type = type;
            Name = name;
        }

        /// <summary>
        /// Gets the category of native element represented by this attribute.
        /// </summary>
        public NativeNameType Type { get; }

        /// <summary>
        /// Gets or sets the original native identifier.
        /// </summary>
        public string Name { get; set; }
    }

    /// <summary>
    /// Captures source location metadata for generated symbols.
    /// </summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class SourceLocationAttribute : Attribute
    {
        /// <summary>
        /// Initializes a source location attribute.
        /// </summary>
        /// <param name="file">Source file path.</param>
        /// <param name="start">Start location marker.</param>
        /// <param name="end">End location marker.</param>
        public SourceLocationAttribute(string file, string start, string end)
        {
            File = file;
            Start = start;
            End = end;
        }

        /// <summary>
        /// Gets or sets the source file path.
        /// </summary>
        public string File { get; set; }

        /// <summary>
        /// Gets the start location marker.
        /// </summary>
        public string Start { get; }

        /// <summary>
        /// Gets the end location marker.
        /// </summary>
        public string End { get; }
    }
}
