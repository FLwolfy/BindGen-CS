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
        Type[] expected = [typeof(ClangFrontend), typeof(IClangFactContributor), typeof(ClangFactContributionContext), typeof(ClangPluginAssemblyIdentity)];
        Assert.Equal(expected.Select(type => type.FullName).Order(StringComparer.Ordinal),
            typeof(ClangFrontend).Assembly.GetExportedTypes().Select(type => type.FullName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheSoleFriendIsTheFrontendImplementationTestAssembly()
    {
        InternalsVisibleToAttribute friend = Assert.Single(typeof(ClangFrontend).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>());
        Assert.Equal(typeof(FrontendSurfaceTests).Assembly.GetName().Name, friend.AssemblyName);
    }
}
