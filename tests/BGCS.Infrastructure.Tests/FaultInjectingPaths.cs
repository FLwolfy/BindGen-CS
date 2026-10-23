using System;
using System.Collections.Generic;
using BGCS.Core.Hosting;

namespace BGCS.Infrastructure.Tests;

internal sealed class FaultInjectingPaths(IHostPathService inner) : IHostPathService
{
    internal Action<string, string>? beforeEquality { get; set; }
    public IEqualityComparer<string> comparer => inner.comparer;
    public HostPathIdentity GetIdentity(string path) => inner.GetIdentity(path);
    public HostPathSemantics GetSemantics(string directory) => inner.GetSemantics(directory);
    public string GetIdentityKey(string path) => inner.GetIdentityKey(path);
    public bool Equals(
        string left,
        string right
    ) {
        beforeEquality?.Invoke(left, right);
        return inner.Equals(left, right);
    }
    public bool IsWithin(
        string candidate,
        string root,
        bool includeRoot = true
    ) => inner.IsWithin(candidate, root, includeRoot);
    public string GetRelativePath(
        string baseDirectory,
        string path
    ) => inner.GetRelativePath(baseDirectory, path);
    public HostIoFailureKind ClassifyIoFailure(Exception failure) => inner.ClassifyIoFailure(failure);
    public void Verify() => inner.Verify();
}
