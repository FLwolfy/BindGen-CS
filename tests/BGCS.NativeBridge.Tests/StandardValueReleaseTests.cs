using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Analysis.Bridge;
using BGCS.Core.Artifacts;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.NativeBridge.Emission;
using BGCS.NativeBridge.Planning;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class StandardValueReleaseTests
{
    [Theory]
    [InlineData(NativeTextEncoding.Utf8,1)]
    [InlineData(NativeTextEncoding.Utf16,2)]
    [InlineData(NativeTextEncoding.Utf32,4)]
    [InlineData(NativeTextEncoding.Unknown,1)]
    public void OwnedDefaultTextResultCopiesInputWithoutInventingItsEncoding(NativeTextEncoding encoding,int width)
    {
        NativeModule source=InputModule(width);
        CppFamilyBinding[] bindings=encoding==NativeTextEncoding.Unknown?[]:[new(){nativeTemplate="std::basic_string",family=StandardValueKind.String,stringEncoding=encoding,evidence=new(EvidenceLevel.UserAsserted,"explicit-Unicode") }];
        NativeBridgePlan primary=new NativeBridgePlanner(familyBindings:bindings).Plan(new LoweringContext(source,new(new(source.targetId),"windows","x64","msvc",source.triple,new()),"value",["value.hpp"]));
        NativeBridgePlan bridge=new SequenceInputAdapterPlanner().Apply(primary,[new("Make",[new("text")])]);
        BridgeExport adapter=Assert.Single(bridge.exports,item=>item.inputAdapterOf is not null);
        Assert.True(SequenceAdapterResultFacts.IsNonEscaping(bridge,adapter));
        Assert.Equal(BridgeConversionKind.OwnedValue,adapter.resultConversion);
        StringContract? text=StandardSequenceTextFacts.Read(bridge,Assert.Single(adapter.operation.argumentGroups));
        if(encoding==NativeTextEncoding.Unknown)Assert.Null(text);
        else
        {
            Assert.Equal(encoding,text!.encoding);Assert.Equal(width,text.codeUnitWidth);Assert.False(text.zeroTerminated);
            Assert.Equal(NativeLengthUnit.CodeUnits,text.lengthUnit);Assert.Equal(NativeLifetimeKind.Call,text.lifetime.kind);
            Assert.Equal(EvidenceLevel.UserAsserted,text.evidence.level);Assert.False(text.retainsPointer);Assert.NotNull(text.lengthParameter);
        }
        Sink sink=new();new TypedBridgeEmitter().Emit(bridge,sink,"value");Assert.Equal(2,sink.files.Count);
    }

    [Theory]
    [InlineData("missing-release")]
    [InlineData("borrowed-result")]
    [InlineData("custom-allocator")]
    public void InputAdaptersCannotUpgradeIncompleteOrBorrowingTextResults(string mutation)
    {
        NativeBridgePlan bridge=Plan(InputModule(1));
        BridgeExport factory=Assert.Single(bridge.exports,item=>item.operation.declaration==new StableId("make"));
        if(mutation=="custom-allocator")
        {
            NativeModule source=bridge.source;
            source=new(source.targetId,source.triple,source.pointerSize,source.types.Select(type=>type.id.value=="allocator"?type with {spelling="std::pmr::polymorphic_allocator<char>"}:type).ToImmutableArray(),source.declarations,[]){language=source.language};
            bridge=bridge with {source=source};
        }
        else factory=mutation=="missing-release"?factory with {resultRelease=null}:factory with {resultConversion=BridgeConversionKind.PointerCast};
        if(mutation!="custom-allocator")bridge=bridge with {exports=bridge.exports.Select(export=>export.id==factory.id?factory:export).ToImmutableArray()};
        Assert.False(SequenceAdapterResultFacts.IsNonEscaping(bridge,factory));
        Assert.ThrowsAny<Exception>(()=>new SequenceInputAdapterPlanner().Apply(bridge,[new("Make",[new("text")])]));
    }

    private static NativeModule InputModule(int width)
    {
        NativeModule source=Module(false);string unit=width==1?"char":width==2?"char16_t":"char32_t";
        NativeDeclaration input=new(new("input"),"Parameter","text","Make::text",new("owner"),new("value.hpp",2,1)){parent=new("make")};
        return new(source.targetId,source.triple,source.pointerSize,source.types.Select(type=>type.id.value=="unit"
            ?type with {spelling=unit,size=width,alignment=width,primitiveKind=width==1?"Char":width==2?"Char16":"Char32",isSigned=width==1}
            :type with {spelling=type.spelling.Replace("<char","<"+unit,StringComparison.Ordinal)}).ToImmutableArray(),
            source.declarations.Select(declaration=>declaration.id.value=="make"?declaration with {children=[input.id]}:declaration).Append(input).ToImmutableArray(),[])
        {language=source.language,byteOrder=NativeByteOrder.LittleEndian};
    }

    [Theory]
    [InlineData(NativeTextEncoding.Utf8,"std::string","char",1)]
    [InlineData(NativeTextEncoding.Utf16,"std::u16string","char16_t",2)]
    [InlineData(NativeTextEncoding.Utf32,"std::u32string","char32_t",4)]
    public void NamedStandardStringAliasSelectsExactEncodingFromItsActualTemplateUnit(NativeTextEncoding encoding,string alias,string unit,int width)
    {
        NativeModule source=Module(false);
        source=new(source.targetId,source.triple,source.pointerSize,source.types.Select(type=>type.id.value=="unit"
            ?type with {spelling=unit,size=width,alignment=width,primitiveKind=width==1?"Char":width==2?"Char16":"Char32",isSigned=width==1}
            :type.id.value=="alias"?type with {spelling=alias,declaration=new("alias-decl")}:type with {spelling=type.spelling.Replace("<char","<"+unit,StringComparison.Ordinal)}).ToImmutableArray(),
            source.declarations.Add(new(new("alias-decl"),"Typedef",alias.Split("::")[^1],alias,new("alias"),new("value.hpp",2,1))).ToImmutableArray(),[]){language=source.language};
        CppFamilyBinding binding=new(){nativeType=alias,family=StandardValueKind.String,stringEncoding=encoding,evidence=new(EvidenceLevel.UserAsserted,"explicit-source-encoding")};
        StandardLibraryFactReader reader=new([binding]);reader.ValidateBindings(source);
        Assert.Equal(new StableId("unit"),Assert.Single(reader.Read(source,new("owner"))!.argumentTypes));
        Assert.Equal(binding,reader.FindBinding(source,new("alias")));
        NativeBridgePlan bridge=new NativeBridgePlanner(familyBindings:[binding]).Plan(new LoweringContext(source,
            new(new(source.targetId),"windows","x64","msvc",source.triple,new()),"value",["value.hpp"]));
        Sink sink=new();new TypedBridgeEmitter().Emit(bridge,sink,"value");
        Assert.Contains("exact native element type",sink.files["src/value.cpp"]);
        Assert.NotNull(StandardValueReleaseFacts.Read(source,new("alias")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictingStandardStringAliasPoliciesCannotChangeOneCanonicalOwner(bool template)
    {
        NativeModule source=Module(false);
        NativeType secondAlias=new(new("second-alias"),NativeTypeCategory.Alias,"OtherText",32,8){elementType=new("owner"),declaration=new("second-alias-decl")};
        source=new(source.targetId,source.triple,source.pointerSize,source.types.Add(secondAlias),
            source.declarations.Add(new(new("alias-decl"),"Typedef","Text","Text",new("alias"),new("value.hpp",2,1)))
                .Add(new(new("second-alias-decl"),"Typedef","OtherText","OtherText",secondAlias.id,new("value.hpp",3,1))),[]){language=source.language};
        CppFamilyBinding exact=new(){nativeType="Text",family=StandardValueKind.String,stringEncoding=NativeTextEncoding.Utf8,evidence=new(EvidenceLevel.UserAsserted,"utf8")};
        CppFamilyBinding second=template?exact with {nativeType=null,nativeTemplate="std::basic_string"}:exact with {nativeType="OtherText"};
        Assert.ThrowsAny<Exception>(()=>new StandardLibraryFactReader([exact,second]).ValidateBindings(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultStandardStringsAndPathsKeepCleanupInsideTheCreatingCall(bool path)
    {
        NativeModule source=Module(path);
        NativeValueReleaseContract release=Assert.IsType<NativeValueReleaseContract>(StandardValueReleaseFacts.Read(source,new("owner")));
        Assert.Equal(NativeValueReleaseScope.CreatingCall,release.scope);
        Assert.Equal(NativeValueReleaseScope.Unknown,default);
        Assert.Equal(EvidenceLevel.Declared,release.evidence.level);
        Assert.NotNull(StandardValueReleaseFacts.Read(source,new("alias")));
        NativeBridgePlan bridge=Plan(source);
        BridgeExport copy=Assert.Single(bridge.exports,export=>export.operation.standardValue?.action==StandardValueAction.CopyCodeUnits);
        Assert.Equal(3,copy.parameters.Length);
        Assert.True(bridge.types.Single(type=>type.id==copy.parameters[0].type).isConst);
        Assert.Equal("destination",copy.parameters[1].name);
        Assert.Equal("capacity",copy.parameters[2].name);
        Sink sink=new();new TypedBridgeEmitter().Emit(bridge,sink,"value");
        string emitted=sink.files["src/value.cpp"];
        Assert.Contains(path?"::std::filesystem::path>::value":"::std::allocator<char>>>::value",emitted);
        Assert.Contains("if (capacity < bgcs_copy_count)",emitted);
        Assert.Contains("bgcs_copy_count != 0 && destination == nullptr",emitted);
        Assert.Contains("destination[bgcs_copy_index]",emitted);
        Assert.Contains("#if defined(bgcs_copy_count)",emitted);
        Assert.Contains("static_assert(std::is_nothrow_destructible<",emitted);
        Assert.DoesNotContain("memcpy(destination",emitted);
    }

    [Theory]
    [InlineData("custom-owner")]
    [InlineData("custom-traits")]
    [InlineData("custom-allocator")]
    [InlineData("pmr")]
    [InlineData("wrong-unit")]
    [InlineData("missing-defaults")]
    [InlineData("incomplete")]
    public void CustomOrIncompleteStorageNeverAcquiresDefaultStandardCleanup(string mutation)
    {
        NativeModule source=Module(false);
        source=new(source.targetId,source.triple,source.pointerSize,source.types.Select(type=>type.id.value switch
        {
            "owner" when mutation=="custom-owner"=>type with {spelling="custom::Text<char>"},
            "traits" when mutation=="custom-traits"=>type with {spelling="custom::Traits<char>"},
            "allocator" when mutation is "custom-allocator" or "pmr"=>type with {spelling=mutation=="pmr"?"std::pmr::polymorphic_allocator<char>":"custom::Allocator<char>"},
            _=>type
        }).ToImmutableArray(),source.declarations.Select(declaration=>declaration.id.value switch
        {
            "owner-decl" when mutation=="incomplete"=>declaration with {isCompleteDefinition=false},
            "owner-decl" when mutation=="missing-defaults"=>declaration with {templateArguments=[declaration.templateArguments[0]]},
            "allocator-decl" when mutation=="wrong-unit"=>declaration with {templateArguments=[new("Type","int",new("integer"))]},
            _=>declaration
        }).ToImmutableArray(),[]){language=SourceLanguage.Cpp};
        Assert.Null(StandardValueReleaseFacts.Read(source,new("owner")));
    }

    [Theory]
    [InlineData("destination")]
    [InlineData("capacity")]
    [InlineData("const")]
    [InlineData("result")]
    [InlineData("family")]
    public void ChangedCodeUnitCopyCannotWriteAnyCandidateArtifact(string mutation)
    {
        NativeBridgePlan bridge=Plan(Module(false));
        BridgeExport copy=Assert.Single(bridge.exports,item=>item.operation.standardValue?.action==StandardValueAction.CopyCodeUnits);
        BridgeExport changed=mutation switch
        {
            "destination"=>copy with {parameters=copy.parameters.SetItem(1,copy.parameters[1] with {type=copy.parameters[2].type})},
            "capacity"=>copy with {parameters=copy.parameters.SetItem(2,copy.parameters[2] with {type=copy.parameters[1].type})},
            "const"=>copy with {parameters=copy.parameters.SetItem(0,copy.parameters[0] with {type=copy.parameters[1].type})},
            "result"=>copy with {nativeResultType=new("unit")},
            "family"=>copy with {operation=copy.operation with {standardValue=copy.operation.standardValue! with {family=StandardValueKind.Vector}}},
            _=>throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        bridge=bridge with {exports=bridge.exports.Replace(copy,changed)};
        Sink sink=new();
        Assert.Throws<InvalidOperationException>(()=>new TypedBridgeEmitter().Emit(bridge,sink,"value"));
        Assert.Empty(sink.files);
    }

    private static NativeBridgePlan Plan(NativeModule source)=>new NativeBridgePlanner().Plan(new LoweringContext(source,
        new(new(source.targetId),"windows","x64","msvc",source.triple,new()),"value",["value.hpp"]));

    private static NativeModule Module(bool path)
    {
        SourceLocation at=new("value.hpp",1,1);
        NativeType unit=new(new("unit"),NativeTypeCategory.Primitive,"char",1,1){primitiveKind="Char",isSigned=true};
        NativeType integer=new(new("integer"),NativeTypeCategory.Primitive,"int",4,4){primitiveKind="Int",isSigned=true};
        NativeType traits=new(new("traits"),NativeTypeCategory.Record,"std::char_traits<char>",1,1){declaration=new("traits-decl")};
        NativeType allocator=new(new("allocator"),NativeTypeCategory.Record,"std::allocator<char>",1,1){declaration=new("allocator-decl")};
        NativeType owner=new(new("owner"),NativeTypeCategory.Record,path?"std::filesystem::path":"std::basic_string<char, std::char_traits<char>, std::allocator<char>>",32,8){declaration=new("owner-decl")};
        NativeType alias=new(new("alias"),NativeTypeCategory.Alias,"Text",32,8){elementType=owner.id};
        NativeDeclaration record=new(new("owner-decl"),"Class",path?"path":"basic_string",owner.spelling,owner.id,at)
        {isCompleteDefinition=true,isSystemDeclaration=true,templateArguments=path?[]:[new("Type","char",unit.id),new("Type",traits.spelling,traits.id),new("Type",allocator.spelling,allocator.id)]};
        NativeDeclaration traitsDecl=new(new("traits-decl"),"Class","char_traits",traits.spelling,traits.id,at){isCompleteDefinition=true,isSystemDeclaration=true,templateArguments=[new("Type","char",unit.id)]};
        NativeDeclaration allocatorDecl=new(new("allocator-decl"),"Class","allocator",allocator.spelling,allocator.id,at){isCompleteDefinition=true,isSystemDeclaration=true,templateArguments=[new("Type","char",unit.id)]};
        NativeDeclaration make=new(new("make"),"Function","Make","Make",null,at){returnType=owner.id};
        return new("windows-x64-msvc","x86_64-pc-windows-msvc",8,[unit,integer,traits,allocator,owner,alias],[record,traitsDecl,allocatorDecl,make],[]){language=SourceLanguage.Cpp};
    }

    private sealed class Sink:IArtifactSink
    {
        internal Dictionary<string,string> files{get;}=[];
        public string WriteText(string relativePath,string content){files.Add(relativePath,content);return relativePath;}
    }
}
