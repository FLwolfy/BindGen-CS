using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using BGCS.Core.Logging;
using BGCS.CppAst.Parsing;
using BGCS.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace BGCS.Tests;

public class GeneratedCodeRuntimeSemanticsTests
{
    private const string GeneratedNamespace = "Runtime.Generated";

    [Fact]
    public void Generate_FunctionTableBindings_ShouldExecuteAgainstCustomContext()
    {
        string header = """
            int bgcs_add(int a, int b);
            void bgcs_set_last(int value);
            int bgcs_get_last(void);
            """;

        var run = RunGenerator(header);
        try
        {
            AssertGeneratorSucceeded(run.Ok, run.Messages);

            Assembly asm = CompileGeneratedSources(run.OutputPath, "BGCS.Generated.RuntimeSemantics");
            Type apiType = asm.GetType("Runtime.Generated.RuntimeApi")!;
            Assert.NotNull(apiType);

            FakeNativeContext context = new();
            object generatedContext = CreateGeneratedContextAdapter(asm, context);
            InvokeStatic(apiType, "InitApi", [generatedContext]);

            int sum = (int)InvokeStatic(apiType, "BgcsAddNative", [7, 35])!;
            Assert.Equal(42, sum);

            InvokeStatic(apiType, "BgcsSetLastNative", [1234]);
            int last = (int)InvokeStatic(apiType, "BgcsGetLastNative", [])!;
            Assert.Equal(1234, last);

            InvokeStatic(apiType, "FreeApi", []);

            Assert.True(context.DisposeCallCount >= 1);
            Assert.Contains("bgcs_add", context.RequestedNames);
            Assert.Contains("bgcs_set_last", context.RequestedNames);
            Assert.Contains("bgcs_get_last", context.RequestedNames);
        }
        finally
        {
            Cleanup(run.TempDirectory);
        }
    }

    private static object? InvokeStatic(Type type, string methodName, object?[] args)
    {
        MethodInfo? method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method!.Invoke(null, args);
    }

    private static Assembly CompileGeneratedSources(string outputPath, string assemblyName)
    {
        string[] sourceFiles = Directory.GetFiles(outputPath, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(sourceFiles);

        SyntaxTree[] trees = sourceFiles
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
            .Concat(
            [
                CSharpSyntaxTree.ParseText(
                    BuildContextBridgeSource(),
                    path: "__GeneratedContextBridge__.cs")
            ])
            .ToArray();

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        using MemoryStream pe = new();
        var emit = compilation.Emit(pe);

        if (!emit.Success)
        {
            string diagnostics = string.Join(
                Environment.NewLine,
                emit.Diagnostics
                    .Where(x => x.Severity == DiagnosticSeverity.Error)
                    .Select(x => x.ToString()));
            Assert.True(false, "Generated code compilation failed:\n" + diagnostics);
        }

        pe.Position = 0;
        AssemblyLoadContext alc = new("BGCS.Generated.RuntimeSemantics", isCollectible: true);
        return alc.LoadFromStream(pe);
    }

    private static object CreateGeneratedContextAdapter(Assembly asm, INativeContext inner)
    {
        Type bridgeType = asm.GetType("BGCS.Tests.Generated.GeneratedContextBridge")!;
        Assert.NotNull(bridgeType);

        Func<string, nint> getProcAddress = inner.GetProcAddress;
        Func<string, bool> isExtensionSupported = inner.IsExtensionSupported;
        Action dispose = inner.Dispose;
        object? bridge = Activator.CreateInstance(bridgeType, getProcAddress, isExtensionSupported, dispose);
        Assert.NotNull(bridge);
        return bridge!;
    }

    private static string BuildContextBridgeSource()
    {
        return $$"""
            namespace BGCS.Tests.Generated;

            public sealed class GeneratedContextBridge : global::BGCS.Runtime.INativeContext
            {
                private readonly global::System.Func<string, nint> getProcAddress;
                private readonly global::System.Func<string, bool> isExtensionSupported;
                private readonly global::System.Action dispose;

                public GeneratedContextBridge(
                    global::System.Func<string, nint> getProcAddress,
                    global::System.Func<string, bool> isExtensionSupported,
                    global::System.Action dispose)
                {
                    this.getProcAddress = getProcAddress;
                    this.isExtensionSupported = isExtensionSupported;
                    this.dispose = dispose;
                }

                public nint GetProcAddress(string procName)
                {
                    return getProcAddress(procName);
                }

                public bool TryGetProcAddress(string procName, out nint procAddress)
                {
                    procAddress = getProcAddress(procName);
                    return procAddress != 0;
                }

                public bool IsExtensionSupported(string extensionName)
                {
                    return isExtensionSupported(extensionName);
                }

                public void Dispose()
                {
                    dispose();
                }
            }
            """;
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        string? tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(tpa))
        {
            throw new InvalidOperationException("Missing TRUSTED_PLATFORM_ASSEMBLIES.");
        }

        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in tpa.Split(Path.PathSeparator))
        {
            paths.Add(path);
        }

        paths.Add(typeof(CsCodeGenerator).Assembly.Location);
        paths.Add(typeof(CsCodeGeneratorConfig).Assembly.Location);
        paths.Add(typeof(FunctionTable).Assembly.Location);
        paths.Add(typeof(INativeContext).Assembly.Location);

        return paths.Select(static path => MetadataReference.CreateFromFile(path));
    }

    private static void AssertGeneratorSucceeded(bool ok, IReadOnlyList<LogMessage> messages)
    {
        Assert.True(ok);
        Assert.DoesNotContain(messages, x => x.Severtiy is LogSeverity.Error or LogSeverity.Critical);
    }

    private static (bool Ok, string TempDirectory, string OutputPath, IReadOnlyList<LogMessage> Messages) RunGenerator(string headerText)
    {
        string temp = Path.Combine(Path.GetTempPath(), "bgcs-runtime-semantics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        string headerPath = Path.Combine(temp, "input.h");
        string outputPath = Path.Combine(temp, "out");
        File.WriteAllText(headerPath, headerText);

        CsCodeGeneratorConfig cfg = new()
        {
            ApiName = "RuntimeApi",
            Namespace = GeneratedNamespace,
            LibName = "runtimetest",
            GenerateExtensions = false,
            ImportType = ImportType.FunctionTable,
            UseCustomContext = true,
            DelegatesAsVoidPointer = false
        };

        CsCodeGenerator generator = new(cfg);
        CppParserOptions parserOptions = new()
        {
            ParseMacros = true,
            ParseComments = true,
            ParseSystemIncludes = false,
            ParseCommentAttribute = true,
            ParserKind = CppParserKind.Cpp,
            AutoSquashTypedef = false
        };
        parserOptions.AdditionalArguments.Add("-undef");

        bool ok = generator.Generate(parserOptions, headerPath, outputPath);
        return (ok, temp, outputPath, generator.Messages);
    }

    private static void Cleanup(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class FakeNativeContext : INativeContext
    {
        private int lastValue;

        private readonly AddDelegate add;
        private readonly SetLastDelegate setLast;
        private readonly GetLastDelegate getLast;
        private readonly Dictionary<string, nint> exports;

        public FakeNativeContext()
        {
            add = AddImpl;
            setLast = SetLastImpl;
            getLast = GetLastImpl;

            exports = new Dictionary<string, nint>(StringComparer.Ordinal)
            {
                ["bgcs_add"] = Marshal.GetFunctionPointerForDelegate(add),
                ["bgcs_set_last"] = Marshal.GetFunctionPointerForDelegate(setLast),
                ["bgcs_get_last"] = Marshal.GetFunctionPointerForDelegate(getLast)
            };
        }

        public int DisposeCallCount { get; private set; }

        public List<string> RequestedNames { get; } = [];

        public nint GetProcAddress(string procName)
        {
            RequestedNames.Add(procName);
            return exports.TryGetValue(procName, out nint value) ? value : 0;
        }

        public bool TryGetProcAddress(string procName, out nint procAddress)
        {
            RequestedNames.Add(procName);
            return exports.TryGetValue(procName, out procAddress);
        }

        public bool IsExtensionSupported(string extensionName)
        {
            return false;
        }

        public void Dispose()
        {
            DisposeCallCount++;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AddDelegate(int a, int b);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetLastDelegate(int value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetLastDelegate();

        private int AddImpl(int a, int b)
        {
            return a + b;
        }

        private void SetLastImpl(int value)
        {
            lastValue = value;
        }

        private int GetLastImpl()
        {
            return lastValue;
        }
    }
}
