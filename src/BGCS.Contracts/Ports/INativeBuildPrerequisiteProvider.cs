using System.Collections.Immutable;
using BGCS.Contracts.Requests;

namespace BGCS.Contracts.Ports;

/// <summary>Optionally exposes a native provider's actual frozen prerequisite tools for environment inspection.</summary>
public interface INativeBuildPrerequisiteProvider
{
    /// <summary>Lists exact prerequisites without executing tools or creating files and plans.</summary>
    /// <returns>Complete immutable tool facts, including unresolved requirements.</returns>
    ImmutableArray<NativeToolPrerequisite> DescribePrerequisites();
}
