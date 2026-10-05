namespace BGCS.Runtime
{
    using System;

    /// <summary>
    /// Identifies the kind of native element represented by <see cref = "NativeNameAttribute"/>.
    /// </summary>
    public enum NativeNameType
    {
        /// <summary>
        /// A native type declaration.
        /// </summary>
        Type,
        /// <summary>
        /// A native record field.
        /// </summary>
        Field,
        /// <summary>
        /// A native record or object declaration.
        /// </summary>
        StructOrClass,
        /// <summary>
        /// A native type alias.
        /// </summary>
        Typedef,
        /// <summary>
        /// A native enumeration.
        /// </summary>
        Enum,
        /// <summary>
        /// A named native enumerator.
        /// </summary>
        EnumItem,
        /// <summary>
        /// A native callable declaration.
        /// </summary>
        Func,
        /// <summary>
        /// A native callable parameter.
        /// </summary>
        Param,
        /// <summary>
        /// A native constant.
        /// </summary>
        Const,
        /// <summary>
        /// A native callback type.
        /// </summary>
        Delegate,
        /// <summary>
        /// A native literal or initialized value.
        /// </summary>
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
        /// <param name = "name">Original native identifier.</param>
        public NativeNameAttribute(string name)
        {
            this.name = name;
        }

        /// <summary>
        /// Initializes an attribute instance with explicit native element type and name.
        /// </summary>
        /// <param name = "type">Category of native element.</param>
        /// <param name = "name">Original native identifier.</param>
        public NativeNameAttribute(
            NativeNameType type,
            string name
        ) {
            this.type = type;
            this.name = name;
        }

        /// <summary>
        /// Gets the category of native element represented by this attribute.
        /// </summary>
        public NativeNameType type { get; }
        /// <summary>
        /// Gets or sets the original native identifier.
        /// </summary>
        public string name { get; set; }
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
        /// <param name = "file">Source file path.</param>
        /// <param name = "start">Start location marker.</param>
        /// <param name = "end">End location marker.</param>
        public SourceLocationAttribute(
            string file,
            string start,
            string end
        ) {
            this.file = file;
            this.start = start;
            this.end = end;
        }

        /// <summary>
        /// Gets or sets the source file path.
        /// </summary>
        public string file { get; set; }
        /// <summary>
        /// Gets the start location marker.
        /// </summary>
        public string start { get; }
        /// <summary>
        /// Gets the end location marker.
        /// </summary>
        public string end { get; }
    }
}
