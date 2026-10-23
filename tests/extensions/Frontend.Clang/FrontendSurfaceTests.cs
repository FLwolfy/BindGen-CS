using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BGCS.Frontend.Clang.Advanced;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class FrontendSurfaceTests
{
    [Fact]
    public void OnlyNeutralFrontendAndScopedAdvancedEntryPointsAreExported()
    {
        Type[] exported = typeof(ClangFrontend).Assembly.GetExportedTypes();
        Assert.Contains(typeof(ClangFrontend), exported);
        Assert.Contains(typeof(ClangFrontendPlugin), exported);
        Assert.Contains(typeof(IClangFactContributor), exported);
        Assert.Contains(typeof(ClangFactContributionContext), exported);
        Assert.Contains(typeof(ClangPluginAssemblyIdentity), exported);
        Assert.DoesNotContain(exported, type => type.Namespace?.StartsWith("BGCS.Frontend.Clang.Model", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void FrontendHasNoFriendAssemblyOrPrivateTestEntryPoint()
    {
        Assert.Empty(typeof(ClangFrontend).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>());
    }
}
