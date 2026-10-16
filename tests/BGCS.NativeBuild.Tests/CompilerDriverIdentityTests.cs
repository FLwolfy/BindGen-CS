using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.Execution;
using BGCS.NativeBuild.Planning;
using BGCS.Platforms.Targeting;
using BGCS.Platforms.Toolchains;
using Xunit;

namespace BGCS.NativeBuild.Tests;

public sealed class CompilerDriverIdentityTests
{
    [Theory]
    [InlineData("Apple clang version 17.0.0 (fixture)", "clang")]
    [InlineData("gcc (GCC) 14.2.0; Copyright (C) Free Software Foundation, Inc.", "gnu")]
    [InlineData("unrecognized compiler fixture", null)]
    public async Task GccFilenameCannotReplaceTheActualReportedDriverFamily(string version, string? family)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs gcc implementation identity " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "version.c"), executable = Path.Combine(root, OperatingSystem.IsWindows() ? "gcc.exe" : "gcc");
            await File.WriteAllTextAsync(source, "#include <stdio.h>\nint main(void) { puts(\"" + version + "\"); return 0; }\n");
            string compiler = ToolchainDiscovery.FindCompiler(SourceLanguage.C, "clang")
                ?? (OperatingSystem.IsWindows() && File.Exists("C:/Program Files/LLVM/bin/clang.exe") ? "C:/Program Files/LLVM/bin/clang.exe" : null)
                ?? throw new InvalidOperationException("The native driver identity fixture requires the installed Clang gate compiler.");
            NativeTargetDescriptor host = ToolchainResolver.ResolveToolchain(
                new TargetResolver().Resolve(new(new NativeTargetId("host"), new(compilerPath: compiler))), SourceLanguage.C, "clang");
            List<string> arguments = ["--no-default-config"];
            foreach (string include in host.toolchain.systemIncludeFolders) { arguments.Add("-isystem"); arguments.Add(include); }
            arguments.AddRange([source, "-o", executable]);
            foreach (string directory in host.toolchain.libraryDirectories) { arguments.Add("-L"); arguments.Add(directory); }
            var compiled = await ProcessExecutor.ExecuteAsync(compiler, arguments, root, TimeSpan.FromMinutes(2));
            Assert.True(compiled.exitCode == 0 && !compiled.timedOut, compiled.standardOutput + compiled.standardError);
            Assert.Equal(family, ToolchainDiscovery.GetCompilerDriverFamily(SourceLanguage.C, executable));
            NativeTargetDescriptor declared = new TargetResolver().Resolve(new(new NativeTargetId("linux-x64-gnu"), new(compilerPath: executable)));
            if (family == "gnu")
            {
                NativeTargetDescriptor resolved = ToolchainResolver.ResolveToolchain(declared, SourceLanguage.C, "gnu");
                Assert.Equal("gnu", new NativeBuildProviderResolver().Resolve(resolved, "gnu").id);
            }
            else
            {
                InvalidOperationException rejected = Assert.Throws<InvalidOperationException>(() => ToolchainResolver.ResolveToolchain(declared, SourceLanguage.C, "gnu"));
                Assert.Contains("filename does not prove GNU identity", rejected.Message);
                if (family == "clang")
                {
                    Assert.Throws<InvalidOperationException>(() => new NativeBuildProviderResolver().Resolve(declared, "gnu"));
                    Assert.Equal("clang", new NativeBuildProviderResolver().Resolve(declared, "auto").id);
                }
                else Assert.Throws<NotSupportedException>(() => new NativeBuildProviderResolver().Resolve(declared, "auto"));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
