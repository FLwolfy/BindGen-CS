namespace BGCS.Runtime
{
    using System;

    /*
     * MIT License
     *
     * Copyright (c) 2023 Juna Meinhold
     *
     * Permission is hereby granted, free of charge, to any person obtaining a copy
     * of this software and associated documentation files (the "Software"), to deal
     * in the Software without restriction, including without limitation the rights
     * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
     * copies of the Software, and to permit persons to whom the Software is
     * furnished to do so, subject to the following conditions:
     *
     * The above copyright notice and this permission notice shall be included in all
     * copies or substantial portions of the Software.
     *
     * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
     * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
     * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
     * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
     * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
     * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
     * SOFTWARE.
     */
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

}
