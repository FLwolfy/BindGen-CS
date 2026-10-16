using System.Collections.Immutable;
using BGCS.Analysis.Declarations;
using BGCS.Contracts.Common;
using BGCS.Contracts.Native;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class NativeSourceLocatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceUsageSurvivesAliasCallbackAndCanonicalEdgesIndependentlyOfInputOrder(bool reverse)
    {
        NativeType integer = new(new("integer"), NativeTypeCategory.Primitive, "int", 4, 4);
        NativeType pointer = new(new("pointer"), NativeTypeCategory.Pointer, "int*", 8, 8) { elementType = integer.id };
        NativeType alias = new(new("alias"), NativeTypeCategory.Alias, "Buffer", 8, 8) { elementType = pointer.id, canonicalType = pointer.id };
        NativeType callback = new(new("callback"), NativeTypeCategory.Function, "void(Buffer)", null, null)
        { parameterTypes = [alias.id] };
        SourceLocation application = new("include/native.h", 12, 9);
        NativeDeclaration own = new(new("application"), "Parameter", "callback", "callback", callback.id, application);
        NativeDeclaration sdk = new(new("sdk"), "Parameter", "callback", "callback", callback.id, new("sdk/system.h", 100, 3)) { isSystemDeclaration = true };
        ImmutableArray<NativeType> types = reverse ? [callback, alias, pointer, integer] : [integer, pointer, alias, callback];
        NativeModule module = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, types, reverse ? [sdk, own] : [own, sdk], []);
        NativeSourceLocator source = new(module);
        Assert.Equal(application, source.Locate(callback.id));
        Assert.Equal(application, source.Locate(alias.id));
        Assert.Equal(application, source.Locate(pointer.id));
        Assert.Equal(application, source.Locate(integer.id));
        Assert.Null(source.Locate(new("not-present")));
    }

    [Fact]
    public void NamedTypesKeepTheirDeclarationAndSyntheticLocationsAreNotInvented()
    {
        NativeDeclaration declaration = new(new("record-declaration"), "Class", "Record", "Record", new("record"), new("records.h", 8, 2));
        NativeType record = new(new("record"), NativeTypeCategory.Record, "Record", 4, 4) { declaration = declaration.id };
        NativeType anonymous = new(new("anonymous"), NativeTypeCategory.Pointer, "void*", 8, 8) { canonicalType = new("anonymous") };
        NativeDeclaration synthetic = new(new("synthetic"), "Parameter", "value", "value", anonymous.id, new("synthetic", 0, 0));
        NativeModule module = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [record, anonymous], [synthetic, declaration], []);
        NativeSourceLocator locator = new(module);
        Assert.Equal(declaration.source, locator.Locate(record.id));
        Assert.Null(locator.Locate(anonymous.id));
    }
}
