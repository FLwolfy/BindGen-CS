using System;
using System.IO;
using BGCS.ApiSnapshot;
using Xunit;

namespace BGCS.Tool.Tests;

public sealed class SnapshotLoadContextTests
{
    [Fact]
    public void ResolveDependencyPath_UsesOnlyTheVersionAndAssetPinnedByDeps()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-snapshot-resolver-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string depsPath = Path.Combine(root, "Consumer.deps.json");
            File.WriteAllText(depsPath,
                """
                {
                  "runtimeTarget": { "name": ".NETCoreApp,Version=v9.0" },
                  "targets": {
                    ".NETCoreApp,Version=v9.0": {
                      "Example.Dependency/9.0.7": {
                        "runtime": { "lib/net9.0/Example.Dependency.dll": { "assemblyVersion": "9.0.0.0" } }
                      }
                    }
                  },
                  "libraries": {
                    "Example.Dependency/9.0.7": {
                      "type": "package",
                      "path": "example.dependency/9.0.7"
                    }
                  }
                }
                """);

            string packages = Path.Combine(root, "packages");
            string pinned = Path.Combine(packages, "example.dependency", "9.0.7", "lib", "net9.0", "Example.Dependency.dll");
            string newer = Path.Combine(packages, "example.dependency", "10.0.0", "lib", "net10.0", "Example.Dependency.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(pinned)!);
            Directory.CreateDirectory(Path.GetDirectoryName(newer)!);
            File.WriteAllText(pinned, "pinned");
            File.WriteAllText(newer, "newer");

            var assets = SnapshotLoadContext.ReadPackageAssemblies(depsPath, packages);
            Assert.Single(assets);
            Assert.Equal(pinned, SnapshotLoadContext.ResolvePinnedPackagePath(assets, "Example.Dependency"));
            Assert.Null(SnapshotLoadContext.ResolvePinnedPackagePath(assets, "System.Runtime"));

            File.Delete(pinned);
            Assert.Null(SnapshotLoadContext.ResolvePinnedPackagePath(assets, "Example.Dependency"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
