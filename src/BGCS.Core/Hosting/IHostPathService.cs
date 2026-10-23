using System;
using System.Collections.Generic;

namespace BGCS.Core.Hosting;

/// <summary>Supplies observed host path identities without imposing target or SDK rules.</summary>
public interface IHostPathService
{
    /// <summary>Gets an equality comparer that honors each path component's observed directory semantics.</summary>
    IEqualityComparer<string> comparer { get; }
    /// <summary>Captures the logical path, resolved physical path and comparison identity.</summary>
    /// <param name="path">An absolute or current-process-relative host path.</param>
    /// <returns>The current host path identity.</returns>
    HostPathIdentity GetIdentity(string path);
    /// <summary>Observes name comparison semantics for a directory or its nearest existing ancestor.</summary>
    /// <param name="directory">The directory whose child names will be compared.</param>
    /// <returns>Observed directory name semantics.</returns>
    HostPathSemantics GetSemantics(string directory);
    /// <summary>Gets a stable comparison key while preserving case-sensitive components.</summary>
    /// <param name="path">The host path.</param>
    /// <returns>A component-aware ordinal comparison key.</returns>
    string GetIdentityKey(string path);
    /// <summary>Compares host paths using observed component semantics.</summary>
    /// <param name="left">The first path.</param>
    /// <param name="right">The second path.</param>
    /// <returns>Whether both paths have the same host name identity.</returns>
    bool Equals(
        string left,
        string right
    );
    /// <summary>Checks a component boundary without treating a similarly prefixed sibling as a child.</summary>
    /// <param name="candidate">The candidate host path.</param>
    /// <param name="root">The containing host path.</param>
    /// <param name="includeRoot">Whether the containing path itself qualifies.</param>
    /// <returns>Whether the candidate belongs to the root's name tree.</returns>
    bool IsWithin(
        string candidate,
        string root,
        bool includeRoot = true
    );
    /// <summary>Computes a relative host path using the comparison semantics of each owning directory.</summary>
    /// <param name="baseDirectory">The directory to start from.</param>
    /// <param name="path">The destination host path.</param>
    /// <returns>A relative path, or the absolute destination when the roots differ.</returns>
    string GetRelativePath(
        string baseDirectory,
        string path
    );
    /// <summary>Classifies a filesystem access failure using the selected host's operation rules.</summary>
    /// <param name="failure">The access or IO failure.</param>
    /// <returns>The host-established retry category.</returns>
    HostIoFailureKind ClassifyIoFailure(Exception failure);
    /// <summary>Independently rechecks captured comparison facts for surviving directories before an input or publication boundary completes; input snapshots separately enforce existence and physical identity.</summary>
    void Verify();
}
