using System;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using BGCS.Targets.Desktop;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class TargetPointerTests : FrontendTestHost
{
    [Theory]
    [InlineData("windows-x86-msvc", 4)]
    [InlineData("linux-arm64-gnu", 8)]
    public async Task CanonicalPointerRetainsTheParsedTargetWidth(string targetId, int pointerSize)
    {
        Assert.True(new DesktopTargetProvider().TryResolve(new(new(targetId)), out NativeTargetDescriptor? target));
        NativeModule module = (await ParseSourceAsync("typedef int Value; Value* value;", target)).module;
        Assert.DoesNotContain(module.diagnostics, value => value.isFailure);
        Assert.Equal(pointerSize, module.pointerSize);
        NativeType pointer = module.GetType(module.declarations.Single(value => value.name == "value").type!.Value);
        Assert.Equal(NativeTypeCategory.Pointer, pointer.category);
        Assert.Equal(pointerSize, pointer.size);
        Assert.Equal(pointerSize, pointer.alignment);
        Assert.NotNull(pointer.elementType);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SyntheticModuleRequiresAnExplicitPositiveTargetPointerWidth(int size)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new NativeModule("fixture", "x86_64-unknown-linux-gnu", size, [], [], []));
}
