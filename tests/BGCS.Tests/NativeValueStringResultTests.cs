using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.CSharp.Planning;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeValueStringResultTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable,false)]
    [InlineData(BindingImportMode.DllImport,false)]
    [InlineData(BindingImportMode.LibraryImport,false)]
    [InlineData(BindingImportMode.FunctionTable,true)]
    [InlineData(BindingImportMode.DllImport,true)]
    [InlineData(BindingImportMode.LibraryImport,true)]
    public async Task FreshStandardValuesCopyCountedUnicodeAndDestroyWithinTheirCreatingCall(BindingImportMode mode,bool sourceRuntime)
    {
        string root=Path.Combine(Path.GetTempPath(),"bgcs-value-text-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root,"values.hpp"),Header);
            string[] names=["Utf8","Utf16","Utf32","Empty","Path","Invalid8","Invalid16","Invalid32","TooLong"];
            BindingConfiguration configuration=new()
            {
                schemaVersion=1,library=new(){id="textvalues",nativeName="text_value_fixture"},
                source=new(){language=SourceLanguage.Cpp,headers=["values.hpp"],parseMacros=false,includeTransitivelyReferencedHeaders=false,
                    selection=new(){includeDeclarations=names.Select(name=>"values::"+name).ToImmutableArray()}},
                api=new(){@namespace="Generated.ValueText",name="ValueApi",importMode=mode,maximumValueStringCodeUnits=32,
                    style=sourceRuntime?BGCS.Contracts.Managed.ManagedApiStyle.Static:BGCS.Contracts.Managed.ManagedApiStyle.Instance,nestGeneratedTypesInApi=sourceRuntime},
                cpp=new(){familyBindings=[Text("std::string",NativeTextEncoding.Utf8),Text("std::u16string",NativeTextEncoding.Utf16),Text("std::u32string",NativeTextEncoding.Utf32),
                    new(){nativeType="std::filesystem::path",family=StandardValueKind.Path,pathFormat=NativePathFormat.NativeUtf8,evidence=new(EvidenceLevel.UserAsserted,"preserve-native-separators")} ]},
                runtime=new(){mode=sourceRuntime?"source":"package"},output=new(){layout=sourceRuntime?"singleFile":"split"},
                nativeBuild=new(){mode="sources"},cache=new(){enabled=false}
            };
            await using BindingHost host=BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult built=await host.BuildAsync(new(new(configuration,root)));
            Assert.True(built.success,built.GetDiagnosticSummary());
            TargetInspection target=Assert.Single(built.generation.targetModels);
            CSharpEmissionPlan plan=new CSharpEmissionPlanner().Plan(target.managed!,new(configuration.library.nativeName,new NoWrites())
                {importMode=mode,runtimeNamespace=sourceRuntime?"Generated.ValueText.Runtime":"BGCS.Runtime",maximumValueStringCodeUnits=32});
            foreach(var type in target.interop.types.Where(type=>type.valueProtocol is not null))
            {
                NativeValueProtocol protocol=type.valueProtocol!;
                Assert.Equal(NativeValueReleaseScope.CreatingCall,protocol.scopedRelease!.scope);
                Assert.Equal(EvidenceLevel.Declared,protocol.scopedRelease.evidence.level);
                Assert.Contains(protocol.operations,operation=>operation.kind==NativeValueOperationKind.CopyCodeUnits);
            }
            StringBuilder consumer=new();
            foreach(string name in names)
            {
                BridgeExport export=Assert.Single(target.bridge!.exports,item=>item.operation.declaration is StableId declaration&&target.bridge.source.GetDeclaration(declaration).qualifiedName=="values::"+name);
                CSharpValueStringResultPlan friendly=Assert.Single(plan.valueStringResults,item=>plan.methods[item.factory.methodIndex].nativeExport==export.id);
                Assert.Equal("string",friendly.signature.returnType);
                InteropFunction factory=target.interop.functions.Single(function=>function.id==export.id);
                Assert.Null(factory.parameters.Single(parameter=>parameter.id==factory.error!.valueParameter).ownership!.releaseThreadDomain);
                string call="api."+friendly.signature.name+"()";
                if(name.StartsWith("Invalid",StringComparison.Ordinal))
                    consumer.Append("try{_=").Append(call).Append(";throw new InvalidOperationException(\"invalid scalar accepted\");}catch(DecoderFallbackException){}\n");
                else if(name=="TooLong")
                    consumer.Append("try{_=").Append(call).Append(";throw new InvalidOperationException(\"copy bound ignored\");}catch(ArgumentOutOfRangeException){}\n");
                else consumer.Append("if(").Append(call).Append("!=").Append(name=="Empty"?"string.Empty":name=="Path"?"\"dir\\\\😀.txt\"":"\"A\\0😀B\"")
                    .AppendLine(")throw new InvalidOperationException(\"counted native Unicode result\");");
            }
            string targetRoot=Path.Combine(built.outputRoot,target.targetId);
            string binary=Assert.Single(Directory.GetFiles(Path.Combine(targetRoot,"native","bin")));
            string context=sourceRuntime?"ValueApiContext":"ValueApi";
            string factoryExpression=mode==BindingImportMode.FunctionTable?"ApiContext.Load(binary)":"ApiContext.Create()";
            Assert.Equal(42,await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot,"managed"),binary,configuration.library.nativeName,$$"""
                using System;
                using System.Text;
                using Generated.ValueText;
                using ApiContext=Generated.ValueText.{{context}};
                public static unsafe class ValueConsumer{
                    public static int Run(string binary){using var api={{factoryExpression}};{{consumer}}return 42;}
                }
                ""","ValueConsumer",root,sourceRuntime));
        }
        finally{Directory.Delete(root,recursive:true);}
    }

    private static CppFamilyBinding Text(string alias,NativeTextEncoding encoding)=>new(){nativeType=alias,family=StandardValueKind.String,stringEncoding=encoding,evidence=new(EvidenceLevel.UserAsserted,"fixture-declared-Unicode-encoding")};
    private static string WorkerRoot()
    {
        DirectoryInfo? root=new(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"BindGen-CS.slnx")))root=root.Parent;
        string configuration=AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar+"Release"+Path.DirectorySeparatorChar,StringComparison.Ordinal)?"Release":"Debug";
        return Path.Combine(root!.FullName,"src","BGCS.Frontend.Clang.Worker","bin",configuration,"net10.0");
    }
    private sealed class NoWrites:IArtifactSink{public string WriteText(string relativePath,string content)=>throw new InvalidOperationException("Planning cannot emit artifacts.");}
    private const string Header="""
        #include <string>
        #include <filesystem>
        namespace values {
        inline std::string Utf8(){return std::string("A\0\xF0\x9F\x98\x80" "B",7);}
        inline std::u16string Utf16(){return std::u16string(u"A\0\U0001F600B",5);}
        inline std::u32string Utf32(){return std::u32string(U"A\0\U0001F600B",4);}
        inline std::string Empty(){return {};}
        inline std::filesystem::path Path(){return std::filesystem::path(std::u8string(u8"dir\\\U0001F600.txt"));}
        inline std::string Invalid8(){return std::string("\xC0\xAF",2);}
        inline std::u16string Invalid16(){return std::u16string(1,static_cast<char16_t>(0xD800));}
        inline std::u32string Invalid32(){return std::u32string(1,static_cast<char32_t>(0x110000));}
        inline std::string TooLong(){return std::string(33,'a');}
        }
        """;
}
