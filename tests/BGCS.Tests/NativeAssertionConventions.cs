using System;
using BGCS.Core.Native;
using BGCS.Core.Targets;
using BGCS.Targets.Desktop;

namespace BGCS.Tests;

internal static class NativeAssertionConventions
{
    internal static NativeSourceConventions For(NativeModule module)
    {
        if (!new DesktopTargetProvider().TryResolve(new(new(module.targetId)) { language = module.language }, out NativeTargetDescriptor? target))
            throw new NotSupportedException("The test must explicitly supply assertion facts for its private target.");
        return target.sourceConventions ?? throw new InvalidOperationException("The desktop test target omitted its source assertion facts.");
    }
}
