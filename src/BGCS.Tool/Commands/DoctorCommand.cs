using System;
using System.Collections.Generic;
using BGCS.Core.Targeting;
using BGCS.CppAst.Parsing;
using BGCS.CppAst.Targeting;

namespace BGCS.Tool.Commands;

internal static class DoctorCommand
{
    internal static int Run()
    {
        NativeTargetDescriptor target = new ClangTargetResolver().Resolve(new(new NativeTargetId("host")));
        string? clangPath = CppToolchainDiscovery.FindCompiler(CppParserKind.Cpp);
        bool clang = clangPath != null;
        IReadOnlyList<string> includes = clang ? CppToolchainDiscovery.DiscoverSystemIncludeFolders(CppParserKind.Cpp, clangPath) : [];
        Console.WriteLine($"OS: {Environment.OSVersion}");
        Console.WriteLine($".NET: {Environment.Version}");
        Console.WriteLine($"Host target: {target.targetId.value} ({target.triple})");
        Console.WriteLine($"C++ compiler: {(clang ? clangPath : "not found; set BGCS_CPP2C_CXX")}");
        Console.WriteLine($"System includes: {includes.Count}");
        Console.WriteLine($"Target sysroot: {target.toolchain.sysRoot ?? "none specified or discovered"}");
        return clang && includes.Count > 0 ? 0 : 1;
    }
}
