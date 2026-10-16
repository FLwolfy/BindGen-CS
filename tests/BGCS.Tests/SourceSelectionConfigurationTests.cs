using System;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using BGCS.Configuration;
using BGCS.Contracts.Requests;
using Xunit;

namespace BGCS.Tests;

public sealed class SourceSelectionConfigurationTests
{
    [Fact]
    public void HeaderSelectionPaths_PreserveEachDeclaringDocumentBaseAcrossComposition()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-selection-config-" + Guid.NewGuid().ToString("N"));
        string parent = Path.Combine(root, "base"), child = Path.Combine(root, "child");
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(child);
        try
        {
            File.WriteAllText(Path.Combine(parent, "base.json"), """
                { "schemaVersion":1,"library":{"id":"selection","nativeName":"selection"},
                  "source":{"headers":["entry.hpp"],"selection":{"includeHeaders":["include/public.hpp"],"excludeDeclarations":["Demo::Hidden"]}},
                  "profiles":{"restricted":{"source":{"selection":{"excludeHeaders":["private/internal.hpp"]}}}} }
                """);
            string path = Path.Combine(child, "bindgen.json");
            File.WriteAllText(path, """
                {"schemaVersion":1,"extends":["../base/base.json"],"profile":"restricted",
                  "source":{"selection":{"includeDeclarations":["Demo::Selected(int)"]}}}
                """);
            DeclarationSelectionRequest selected = GenerationRequest.FromFile(path).configuration.source.selection;
            Assert.Equal(Path.Combine(parent, "include", "public.hpp"), Assert.Single(selected.includeHeaders));
            Assert.Equal(Path.Combine(parent, "private", "internal.hpp"), Assert.Single(selected.excludeHeaders));
            Assert.Equal("Demo::Hidden", Assert.Single(selected.excludeDeclarations));
            Assert.Equal("Demo::Selected(int)", Assert.Single(selected.includeDeclarations));
            File.WriteAllText(path, """
                {"schemaVersion":1,"extends":["../base/base.json"],"source":{"selection":{"includeHeaders":[],"excludeHeaders":["local.hpp"]}}}
                """);
            selected = GenerationRequest.FromFile(path).configuration.source.selection;
            Assert.Empty(selected.includeHeaders);
            Assert.Equal(Path.Combine(child, "local.hpp"), Assert.Single(selected.excludeHeaders));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("default-list")]
    [InlineData("null-entry")]
    [InlineData("blank-entry")]
    [InlineData("duplicate-entry")]
    [InlineData("overlap")]
    public void SdkSelection_InvalidSnapshotsFailBeforeFrontendExecution(string invalid)
    {
        DeclarationSelectionRequest? selected = invalid switch
        {
            "null" => null,
            "default-list" => new() { includeHeaders = default },
            "null-entry" => new() { excludeHeaders = [null!] },
            "blank-entry" => new() { includeDeclarations = [" "] },
            "duplicate-entry" => new() { excludeDeclarations = ["Demo::Hidden", "Demo::Hidden"] },
            _ => new() { includeDeclarations = ["Demo::Selected"], excludeDeclarations = ["Demo::Selected"] }
        };
        BindingConfiguration configuration = new() { schemaVersion = 1, library = new() { id = "selection", nativeName = "selection" },
            source = new() { headers = ["entry.hpp"], selection = selected! } };
        Assert.ThrowsAny<ArgumentException>(() => new GenerationRequest(configuration, Path.GetTempPath()));
    }

    [Fact]
    public void NestedSelectionTypos_AreRejectedWithoutRewritingTheInput()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-selection-strict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "bindgen.json");
            string text = """
                {"schemaVersion":1,"library":{"id":"selection","nativeName":"selection"},"source":{"headers":["entry.h"],"selection":{"includeHeader":["entry.h"]}}}
                """;
            File.WriteAllText(path, text);
            Assert.Throws<JsonException>(() => GenerationRequest.FromFile(path));
            Assert.Equal(text, File.ReadAllText(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("default")]
    [InlineData("undefined")]
    [InlineData("duplicate")]
    [InlineData("overlap")]
    public void InvalidTypedCategoryChoicesFailBeforeFrontendExecution(string invalid)
    {
        DeclarationSelectionRequest selection = invalid switch
        {
            "default" => new() { includeCategories = default },
            "undefined" => new() { excludeCategories = [(NativeDeclarationCategory)999] },
            "duplicate" => new() { includeCategories = [NativeDeclarationCategory.Callables, NativeDeclarationCategory.Callables] },
            _ => new() { includeCategories = [NativeDeclarationCategory.Records], excludeCategories = [NativeDeclarationCategory.Records] }
        };
        BindingConfiguration configuration = new() { schemaVersion = 1, library = new() { id = "selection", nativeName = "selection" },
            source = new() { headers = ["entry.hpp"], selection = selection } };
        Assert.Throws<ArgumentException>(() => new GenerationRequest(configuration, Path.GetTempPath()));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("duplicate-family")]
    [InlineData("default-selectors")]
    [InlineData("duplicate-selector")]
    [InlineData("overlap")]
    public void InvalidIndependentFamilyRulesFailBeforeAnyFrontendExecution(string invalid)
    {
        DeclarationSelectionRequest selected = invalid switch
        {
            "default" => new() { categories = default },
            "duplicate-family" => new() { categories = [new(NativeDeclarationCategory.Callables), new(NativeDeclarationCategory.Callables)] },
            "default-selectors" => new() { categories = [new(NativeDeclarationCategory.Callables) { includeDeclarations = default }] },
            "duplicate-selector" => new() { categories = [new(NativeDeclarationCategory.Records) { excludeDeclarations = ["Record", "Record"] }] },
            _ => new() { categories = [new(NativeDeclarationCategory.Callables) { includeDeclarations = ["read"], excludeDeclarations = ["read"] }] }
        };
        Assert.Throws<ArgumentException>(() => new GenerationRequest(new()
        { schemaVersion = 1, library = new() { id = "selection", nativeName = "selection" }, source = new() { headers = ["input.h"], selection = selected } }, Path.GetTempPath()));
    }
}
