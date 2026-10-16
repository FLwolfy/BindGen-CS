using System;

namespace BGCS.Runtime;

/// <summary>Captures source location metadata for generated symbols.</summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
public class SourceLocationAttribute : Attribute
{
    /// <summary>Initializes source location metadata.</summary>
    /// <param name="file">Source file path.</param>
    /// <param name="start">Start location marker.</param>
    /// <param name="end">End location marker.</param>
    public SourceLocationAttribute(
        string file,
        string start,
        string end
    ) {
        this.file = file;
        this.start = start;
        this.end = end;
    }
    /// <summary>Gets or sets the source file path.</summary>
    public string file { get; set; }
    /// <summary>Gets the start location marker.</summary>
    public string start { get; }
    /// <summary>Gets the end location marker.</summary>
    public string end { get; }
}
