using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeOwnedBufferValueTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable)]
    [InlineData(BindingImportMode.DllImport)]
    [InlineData(BindingImportMode.LibraryImport)]
    public async Task StatusFactoriesAndOwnedDispatchRetainDirectedValueAndOutputCountProtocols(BindingImportMode mode)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-owned-buffer-value-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "widget.hpp"), """
                #include <stdexcept>
                inline int process(const int* input, int* state, int* output) {
                    if (*input < 0) throw std::runtime_error("negative input");
                    *state += *input; *output = *state*2; return *output;
                }
                class Widget {
                    int m_value;
                public:
                    explicit Widget(const int* seed) : m_value(*seed) { }
                    ~Widget() noexcept { }
                    int Update(const int* input, int* state, int* output) { m_value+=*input; *state+=*input; *output=m_value; return m_value; }
                    int Fill(int* buffer, int capacity, int* written, int mode) {
                        for(int i=0;i<capacity;++i) buffer[i]=m_value+i;
                        *written=mode==1 ? -1 : mode==2 ? capacity+1 : capacity; return m_value;
                    }
                };
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "owned_protocol", nativeName = "owned_protocol" },
                source = new() { language = SourceLanguage.Cpp, headers = ["widget.hpp"], parseMacros = false },
                api = new() { @namespace = "Generated.OwnedProtocol", name = "WidgetApi", importMode = mode,
                    names = ImmutableDictionary<string, string>.Empty.Add("Widget::Widget", "CreateWidget").Add("Widget::Update", "Update").Add("Widget::Fill", "Fill") },
                semantics = new()
                {
                    ownedResults = [new() { function = "Widget::Widget", releaseThreadDomain = "any" }],
                    valueParameters = [Value("Widget::Widget", "seed", NativeParameterDirection.Input),
                        Value("process", "input", NativeParameterDirection.Input), Value("process", "state", NativeParameterDirection.InputOutput), Value("process", "output", NativeParameterDirection.Output),
                        Value("Widget::Update", "input", NativeParameterDirection.Input), Value("Widget::Update", "state", NativeParameterDirection.InputOutput), Value("Widget::Update", "output", NativeParameterDirection.Output)],
                    bufferParameters = [new() { function = "Widget::Fill", parameter = "buffer", capacityParameter = "capacity", writtenCountParameter = "written",
                        lengthUnit = NativeLengthUnit.Elements, direction = NativeParameterDirection.Output, lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = true }]
                },
                nativeBuild = new() { mode = "sources" }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult built = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(built.success, built.GetDiagnosticSummary());
            string target = Assert.Single(Directory.GetDirectories(built.outputRoot));
            string native = Assert.Single(Directory.GetFiles(Path.Combine(target, "native", "bin")));
            string create = mode == BindingImportMode.FunctionTable ? "WidgetApi.Load(binary)" : "WidgetApi.Create()";
            string consumer = $$"""
                using System;
                using Generated.OwnedProtocol;
                public static class OwnedProtocolConsumer {
                    public static int Run(string binary) {
                        using var api={{create}};
                        int input=2,state=19,output;
                        if(api.Process(in input,ref state,out output)!=42 || state!=21 || output!=42)return 1;
                        input=-1; state=19;
                        try{api.Process(in input,ref state,out output);return 2;}catch(global::BGCS.Runtime.NativeCallException){if(state!=19 || output!=0)return 3;}
                        int seed=40;
                        using var widget=api.CreateWidget(in seed);
                        input=2;state=19;
                        if(widget.Update(in input,ref state,out output)!=42 || state!=21 || output!=42)return 4;
                        Span<int> data=stackalloc int[3];
                        if(widget.Fill(data,out int written,0)!=42 || written!=3 || data[2]!=44)return 5;
                        for(int mode=1;mode<=2;++mode) {
                            try{widget.Fill(data,out written,mode);return 6;}catch(InvalidOperationException){if(written!=0)return 7;}
                        }
                        api.Dispose();
                        try{widget.Fill(data,out written,0);return 8;}catch(ObjectDisposedException){}
                        widget.Dispose();
                        if(widget.releaseFailure!=null)return 9;
                        return 42;
                    }
                }
                """;
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(target, "managed"), native,
                configuration.library.nativeName, consumer, "OwnedProtocolConsumer", root, sourceRuntime: false));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ValueParameterConfiguration Value(string function, string parameter, NativeParameterDirection direction) => new()
    { function = function, parameter = parameter, direction = direction, lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = false };
    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
