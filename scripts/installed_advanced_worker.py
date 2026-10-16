"""Consume the advanced Clang extension surface from delivered SDK/tool packages only."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET

MARKER = "advanced-clang:read_value:installed-worker-input"


def assert_generated_documentation(directory: Path) -> None:
    sources = list(directory.rglob("*.cs"))
    if not sources or not any(MARKER in source.read_text(encoding="utf-8") for source in sources):
        raise RuntimeError("The installed advanced worker plugin did not contribute live-cursor facts to generated documentation.")


def prepare(dotnet: str, version: str, root: Path, work: Path, evidence: Path, run, write_project) -> dict:
    parser_identity = json.loads((root / "distribution/manifests/native-parser-assets.lock.json").read_text(encoding="utf-8"))["parserIdentity"]
    plugin = work / "advanced-clang-plugin"
    # The public SDK must expose the advanced frontend assembly and its exact
    # ClangSharp reference closure even after Host stops referencing frontend.
    write_project(plugin, version, ["BGCS"])
    project = ET.parse(plugin / "Consumer.csproj")
    properties = project.find("./PropertyGroup")
    properties.find("OutputType").text = "Library"
    ET.SubElement(properties, "AssemblyName").text = "ClangFactPlugin"
    project.write(plugin / "Consumer.csproj", encoding="utf-8", xml_declaration=True)
    shutil.copyfile(root / "tests/fixtures/plugins/ClangFactPlugin/FactPlugin.cs", plugin / "FactPlugin.cs")
    run([dotnet, "build", "Consumer.csproj", "--configuration", "Release", "-m:1", "-nr:false"],
        plugin, evidence, "installed-advanced-clang-plugin-build")
    output = plugin / "bin/Release/net10.0"
    files = [output / "ClangFactPlugin.dll", output / "ClangFactPlugin.deps.json"]
    if not all(path.is_file() for path in files):
        raise RuntimeError("The separately built installed advanced plugin has an incomplete closure.")
    manifest = output / "plugin.json"
    manifest.write_text(json.dumps({
        "schemaVersion": 1, "assembly": "ClangFactPlugin.dll", "entryType": "ClangFactFixture.FingerprintedFactPlugin",
        "workerOnly": True, "workerParserIdentity": parser_identity,
        "plugin": {"id": "clang.fixture", "version": "1.0.0", "contractsMajor": 3,
            "services": [{"id": "clang.fixture.facts", "port": "frontendFactContributor", "priority": 0}],
            "configurationSchema": {"type": "object", "additionalProperties": False, "required": ["externalFile"],
                "properties": {"externalFile": {"type": "string", "minLength": 1}, "mode": {"type": "string"}}}},
        "assets": [{"path": path.name, "digest": {"value": hashlib.sha256(path.read_bytes()).hexdigest()}} for path in files]
    }, indent=2) + "\n", encoding="utf-8")
    source = work / "advanced-clang-input"
    source.mkdir()
    (source / "native.h").write_text("int read_value(void);\n", encoding="utf-8")
    external = source / "external.txt"
    external.write_text("installed-worker-input", encoding="utf-8")
    configuration = {
        "schemaVersion": 1, "library": {"id": "advanced_worker", "nativeName": "advanced_worker"},
        "source": {"language": "c", "headers": [str(source / "native.h")], "parseMacros": False},
        "api": {"namespace": "Package.AdvancedWorker", "name": "NativeApi"},
        "runtime": {"mode": "package"}, "nativeBuild": {"mode": "none"},
        "output": {"root": str(work / "advanced-sdk-generated")},
        "cache": {"directory": str(work / "advanced-worker-cache")},
        "extensions": {"plugins": [str(manifest)], "options": {"clang.fixture": {"values": {"externalFile": str(external)}}}}
    }
    host = work / "advanced-clang-sdk"
    write_project(host, version, ["BGCS"])
    path = host / "bindgen.json"
    path.write_text(json.dumps(configuration, indent=2) + "\n", encoding="utf-8")
    (host / "Program.cs").write_text('''using System;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using BGCS;
await using BindingHost host = BindingHost.CreateDefault();
GenerationRequest request = await GenerationRequest.FromFileAsync("bindgen.json");
GenerationResult result = await host.GenerateAsync(request);
if (!result.success) throw new InvalidOperationException(result.GetDiagnosticSummary());
if (AssemblyLoadContext.Default.Assemblies.Any(assembly => assembly.GetName().Name == "ClangFactPlugin"))
    throw new InvalidOperationException("The installed SDK loaded the worker-only plugin into its host process.");
Console.WriteLine("Installed SDK advanced live Clang facts were contributed exclusively inside the parser worker.");
''', encoding="utf-8")
    run([dotnet, "build", "Consumer.csproj", "--configuration", "Release", "-m:1", "-nr:false"],
        host, evidence, "installed-advanced-clang-sdk-build")
    run([dotnet, str(host / "bin/Release/net10.0/Consumer.dll")], host, evidence, "installed-advanced-clang-sdk")
    generated = work / "advanced-sdk-generated"
    assert_generated_documentation(generated)
    consumer = work / "advanced-clang-runtime-consumer"
    items = ET.Element("ItemGroup")
    for path in sorted(generated.rglob("*.cs")):
        ET.SubElement(items, "Compile", Include=str(path))
    write_project(consumer, version, ["BGCS.Runtime"], ET.tostring(items, encoding="unicode"))
    (consumer / "Program.cs").write_text("public static class Program { public static void Main() { } }\n", encoding="utf-8")
    run([dotnet, "build", "Consumer.csproj", "--configuration", "Release", "-m:1", "-nr:false"],
        consumer, evidence, "installed-advanced-clang-runtime-consumer-build")

    integrated = work / "advanced-clang-msbuild"
    write_project(integrated, version, ["BGCS", "BGCS.Runtime"],
        "<PropertyGroup><BGCSGenerateBindings>true</BGCSGenerateBindings></PropertyGroup>")
    msbuild_configuration = {**configuration, "output": {"root": "UnusedConfiguredRoot"}}
    (integrated / "bindgen.json").write_text(json.dumps(msbuild_configuration, indent=2) + "\n", encoding="utf-8")
    (integrated / "Program.cs").write_text("public static class Program { public static void Main() { } }\n", encoding="utf-8")
    run([dotnet, "build", "Consumer.csproj", "--configuration", "Release", "-m:1", "-nr:false"],
        integrated, evidence, "installed-advanced-clang-msbuild")
    assert_generated_documentation(integrated / "obj")
    if (integrated / "UnusedConfiguredRoot").exists():
        raise RuntimeError("The installed advanced-worker MSBuild request escaped its intermediate output.")
    return {"configuration": configuration, "manifest": manifest,
        "evidence": {"manifestSha256": hashlib.sha256(manifest.read_bytes()).hexdigest(),
            "parserIdentity": parser_identity, "pluginAssets": [
                {"file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()} for path in files]}}


def verify_cli(tool: Path, state: dict, work: Path, evidence: Path, run) -> None:
    probe = work / "advanced-clang-cli"
    probe.mkdir()
    configuration = {**state["configuration"], "output": {"root": str(work / "advanced-cli-generated")}}
    (probe / "bindgen.json").write_text(json.dumps(configuration, indent=2) + "\n", encoding="utf-8")
    run([str(tool), "generate", "bindgen.json"], probe, evidence, "installed-advanced-clang-cli")
    assert_generated_documentation(work / "advanced-cli-generated")
