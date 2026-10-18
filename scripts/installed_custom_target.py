"""Consume the real Contracts-only target example through installed SDK and CLI packages."""
from __future__ import annotations

import argparse
import functools
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import tempfile
import xml.etree.ElementTree as ET

from acceptance_evidence import digest, publication, source_identity

ROOT = Path(__file__).resolve().parents[1]
CHECK = "installed-contracts-custom-target-same-manifest-sdk-cli-cache"
ASSEMBLY = "Package.CustomTarget"
TARGET = "example-private-host"
PHASES = ("baseline", "warm", "sdkChanged", "profileChanged")


def isolate_consumer_root(directory: Path) -> None:
    """Stop ordinary external consumers from importing the repository's parent build files."""
    directory.mkdir(parents=True, exist_ok=True)
    for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"):
        path = directory / name
        if path.exists():
            raise RuntimeError("The fresh consumer boundary already contains " + name)
        path.write_text("<Project />\n", encoding="utf-8")


def _write(path: Path, value: dict) -> None:
    content = json.dumps(value, indent=2) + "\n"
    if not path.is_file() or path.read_text(encoding="utf-8") != content:
        path.write_text(content, encoding="utf-8")


def _seal(state: dict) -> None:
    if any(digest(Path(path)) != expected for path, expected in state["sealed"].items()):
        raise RuntimeError("Installed custom-target configuration, plugin or example source changed during consumption.")


def _profile(state: dict, revision: int) -> None:
    _write(state["profile"], {**state["hostProfile"], "defines": ["PROFILE_VERSION=" + str(revision)]})


def _facts(native: dict, sdk: int, profile: int) -> None:
    declarations = native.get("declarations", [])
    if native.get("targetId") != TARGET or any(
            len([item for item in declarations if item.get("name") == name
                 and item.get("evaluatedIntegralValue") == str(value)]) != 1
            for name, value in (("ExampleSdkLimit", sdk), ("ExampleProfile", profile))):
        raise RuntimeError("The installed target did not preserve its actual SDK/profile enum facts.")


def _phases(state: dict, entry: str, command: list[str], evidence: Path, run) -> dict:
    # CLI starts from new declared bytes, so its cold miss is independent of
    # whether SDK and CLI worker execution identities happen to be identical.
    sdk, profile = (42, 1) if entry == "sdk" else (44, 3)
    phases = []
    for phase, sdk_value, profile_value in (("baseline", sdk, profile), ("warm", sdk, profile),
            ("sdkChanged", sdk + 1, profile), ("profileChanged", sdk + 1, profile + 1)):
        print("Installed custom target: " + entry + "/" + phase, flush=True)
        header = f"#define EXAMPLE_SDK_VALUE {sdk_value}\n"
        if not state["header"].is_file() or state["header"].read_text(encoding="utf-8") != header:
            state["header"].write_text(header, encoding="utf-8")
        _profile(state, profile_value)
        _seal(state)
        result = json.loads(run(command, state["input"], evidence, "custom-target-" + entry + "-" + phase))
        if result.get("success") is not True:
            raise RuntimeError("Installed custom-target generation failed: " + entry + "/" + phase)
        if entry == "sdk":
            native = result["native"]
        else:
            inspection = json.loads(run([command[0], "inspect", str(state["configuration"]), "--json"],
                state["input"], evidence, "custom-target-cli-" + phase + "-inspect"))
            if inspection.get("success") is not True or len(inspection.get("targets", [])) != 1:
                raise RuntimeError("Installed CLI inspection did not retain its single custom target.")
            native = inspection["targets"][0]["native"]
        _facts(native, sdk_value, profile_value)
        stages = result.get("cacheStages", [])
        statuses = {}
        for name in ("primaryFrontend", "csharpEmission"):
            values = [item.get("status") for item in stages if item.get("stage") == name]
            if len(values) != 1 or values[0] not in ("hit", "miss"):
                raise RuntimeError("Installed custom-target cache was not eligible: " + str(stages))
            statuses[name] = values[0]
        if phase == "warm" and set(statuses.values()) != {"hit"}:
            raise RuntimeError("Installed custom-target unchanged inputs did not hit both caches.")
        if phase == "baseline" and statuses["primaryFrontend"] != "miss":
            raise RuntimeError("Installed custom-target fresh input state did not report a cold frontend miss.")
        if phase in ("sdkChanged", "profileChanged") and statuses["primaryFrontend"] != "miss":
            raise RuntimeError("Installed custom-target changed declared inputs reused stale frontend facts.")
        manifest = publication(state["output"], "private_target")
        if not any(item["mediaType"] == "text/x-csharp" for item in manifest["artifacts"]):
            raise RuntimeError("The installed target did not publish real managed bindings.")
        phases.append({"phase": phase, **statuses, "sdkValue": sdk_value, "profileValue": profile_value,
            "sdkHeaderSha256": digest(state["header"]), "profileSha256": digest(state["profile"]),
            "publicationSha256": digest(state["output"] / ".bgcs-artifacts.json")})
        _seal(state)
    return {"targetId": TARGET, "phases": phases}


def prepare(dotnet: str, version: str, root: Path, work: Path, evidence: Path, run, write_project) -> dict:
    plugin = work / "custom-target-plugin"
    write_project(plugin, version, ["BGCS.Contracts"])
    project = ET.parse(plugin / "Consumer.csproj")
    properties = project.find("./PropertyGroup")
    properties.find("OutputType").text = "Library"
    ET.SubElement(properties, "AssemblyName").text = ASSEMBLY
    ET.SubElement(properties, "CopyLocalLockFileAssemblies").text = "true"
    project.write(plugin / "Consumer.csproj", encoding="utf-8", xml_declaration=True)
    example = root / "examples/CustomTargetPlugin"
    shutil.copyfile(example / "CustomTargetPlugin.cs", plugin / "CustomTargetPlugin.cs")
    run([dotnet, "build", "Consumer.csproj", "-c", "Release", "-m:1", "-nr:false"],
        plugin, evidence, "custom-target-contracts-plugin-build")
    plugin_output = plugin / "bin/Release/net10.0"
    assets = [plugin_output / name for name in (ASSEMBLY + ".dll", ASSEMBLY + ".deps.json", "BGCS.Contracts.dll")]
    if not all(path.is_file() for path in assets):
        raise RuntimeError("The installed Contracts-only target plugin closure is incomplete.")
    dependencies = json.loads(assets[1].read_text(encoding="utf-8-sig"))["libraries"]
    if {name.split("/")[0] for name in dependencies} != {ASSEMBLY, "BGCS.Contracts"}:
        raise RuntimeError("The custom-target plugin did not depend exclusively on installed BGCS.Contracts.")
    manifest = plugin_output / "plugin.json"
    _write(manifest, {"schemaVersion": 1, "assembly": ASSEMBLY + ".dll",
        "entryType": "BGCS.Examples.CustomTargetPlugin.CustomTargetPlugin", "plugin": {
            "id": "bgcs.examples.custom-target", "version": "3.0.0", "contractsMajor": 3,
            "services": [{"id": "example.custom-target", "port": "nativeTargetProvider"}],
            "configurationSchema": {"type": "object", "additionalProperties": False, "required": ["profile"],
                "properties": {"profile": {"type": "string", "minLength": 1}}}},
        "assets": [{"path": path.name, "digest": {"value": digest(path)}} for path in assets]})
    host = work / "custom-target-sdk"
    write_project(host, version, ["BGCS"])
    (host / "Program.cs").write_text('''using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BGCS;
using BGCS.Platforms.Targeting;
JsonSerializerOptions options = new() { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
if (args.Length == 1 && args[0] == "describe")
{
    var target = new TargetResolver().Resolve(new(new("host")));
    Console.WriteLine(JsonSerializer.Serialize(new { platformId = "private-desktop", target.architectureId,
        target.abiId, target.triple, target.byteOrder, requiresExplicitSdk = true, supportsDynamicModules = true,
        systemIncludeDirectories = new[] { "sdk/include" } }, options));
    return;
}
await using BindingHost host = BindingHost.CreateDefault();
GenerationResult result = await host.GenerateAsync(await GenerationRequest.FromFileAsync(args.Single()));
if (!result.success) throw new InvalidOperationException(result.GetDiagnosticSummary());
Console.WriteLine(JsonSerializer.Serialize(new { result.success, result.cacheStages,
    native = result.targetModels.Single().native }, options));
''', encoding="utf-8")
    run([dotnet, "build", "Consumer.csproj", "-c", "Release", "-m:1", "-nr:false"],
        host, evidence, "custom-target-installed-sdk-build")
    host_output = host / "bin/Release/net10.0"
    if digest(host_output / "BGCS.Contracts.dll") != digest(assets[-1]):
        raise RuntimeError("Installed SDK and Contracts-only target plugin resolved different Contracts bytes.")
    driver = [dotnet, str(host_output / "Consumer.dll")]
    host_profile = json.loads(run([*driver, "describe"], host, evidence, "custom-target-host-description"))
    source = work / "custom-target-input"
    (source / "sdk/include").mkdir(parents=True)
    shutil.copyfile(example / "native.h", source / "native.h")
    configuration = json.loads((example / "bindgen.json").read_text(encoding="utf-8"))
    configuration["extensions"]["plugins"] = [str(manifest)]
    configuration["output"]["root"] = str(work / "custom-target-generated")
    configuration["cache"] = {"enabled": True, "directory": str(work / "custom-target-cache")}
    path = source / "bindgen.json"
    _write(path, configuration)
    state = {"input": source, "configuration": path, "manifest": manifest, "output": work / "custom-target-generated",
        "profile": source / "target-sdk.json", "header": source / "sdk/include/private_sdk.h", "hostProfile": host_profile,
        "evidence": {"manifestSha256": digest(manifest), "configurationSha256": digest(path),
            "pluginSourceSha256": digest(plugin / "CustomTargetPlugin.cs"), "pluginReferences": ["BGCS.Contracts"],
            "pluginAssets": [{"file": asset.name, "sha256": digest(asset)} for asset in assets], "entries": {}},
        "sealed": {str(asset): digest(asset) for asset in [*assets, manifest, path, source / "native.h", plugin / "CustomTargetPlugin.cs"]}}
    state["evidence"]["entries"]["sdk"] = _phases(state, "sdk", [*driver, str(path)], evidence, run)
    return state


def verify_cli(tool: Path, state: dict, evidence: Path, run) -> dict:
    state["evidence"]["entries"]["cli"] = _phases(state, "cli",
        [str(tool), "generate", str(state["configuration"]), "--json"], evidence, run)
    validate_evidence(state["evidence"])
    return state["evidence"]


def validate_evidence(value: dict) -> None:
    """Validate only this finite installed fixture; it does not certify other target platforms."""
    valid_hash = lambda item: isinstance(item, str) and re.fullmatch(r"[0-9a-f]{64}", item)
    assets = value.get("pluginAssets", [])
    if (value.get("pluginReferences") != ["BGCS.Contracts"]
            or any(not valid_hash(value.get(name)) for name in ("manifestSha256", "configurationSha256", "pluginSourceSha256"))
            or len(assets) != 3 or {item.get("file") for item in assets} != {ASSEMBLY + ".dll", ASSEMBLY + ".deps.json", "BGCS.Contracts.dll"}
            or any(not valid_hash(item.get("sha256")) for item in assets)
            or set(value.get("entries", {})) != {"sdk", "cli"}):
        raise RuntimeError("Installed Contracts-only custom-target closure or entry points are incomplete.")
    for entry, initial_sdk, initial_profile in (("sdk", 42, 1), ("cli", 44, 3)):
        evidence = value["entries"][entry]
        phases = evidence.get("phases", [])
        if evidence.get("targetId") != TARGET or [item.get("phase") for item in phases] != list(PHASES):
            raise RuntimeError("Installed custom-target phases are incomplete or duplicated.")
        for index, phase in enumerate(phases):
            if (phase.get("sdkValue") != initial_sdk + (index >= 2)
                    or phase.get("profileValue") != initial_profile + (index == 3)
                    or any(not valid_hash(phase.get(name)) for name in ("sdkHeaderSha256", "profileSha256", "publicationSha256"))
                    or any(phase.get(name) not in ("hit", "miss") for name in ("primaryFrontend", "csharpEmission"))
                    or index == 1 and any(phase[name] != "hit" for name in ("primaryFrontend", "csharpEmission"))
                    or index == 0 and phase["primaryFrontend"] != "miss"
                    or index >= 2 and phase["primaryFrontend"] != "miss"):
                raise RuntimeError("Installed custom-target facts or cache invalidation disagree with the consumed inputs.")
        if (phases[0]["sdkHeaderSha256"] != phases[1]["sdkHeaderSha256"]
                or phases[1]["sdkHeaderSha256"] == phases[2]["sdkHeaderSha256"]
                or phases[2]["sdkHeaderSha256"] != phases[3]["sdkHeaderSha256"]
                or len({phase["profileSha256"] for phase in phases[:3]}) != 1
                or phases[2]["profileSha256"] == phases[3]["profileSha256"]):
            raise RuntimeError("Installed custom-target changes were not isolated to the declared SDK/profile inputs.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--packages", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    spec = importlib.util.spec_from_file_location("installed_custom_target_verifier", ROOT / "eng/verify-packages.py")
    verifier = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(verifier)
    evidence = args.output.resolve()
    evidence.mkdir(parents=True, exist_ok=False)
    dotnet = Path(args.dotnet).resolve(strict=True)
    packages = args.packages.resolve(strict=True)
    source = source_identity(ROOT)
    report = {"schemaVersion": 1, "success": False, "scope": "focused-installed-custom-target-local-only",
        "completeProductSet": False, "check": CHECK, "version": args.version, "verifierSource": source,
        "packageSourceReceipt": json.loads((packages / "package-verification.json").read_text(encoding="utf-8")),
        "packageDigests": [{"file": path.name, "sha256": digest(path)} for path in sorted(packages.glob(f"*.{args.version}.nupkg"))]}
    try:
        required = {f"{name}.{args.version}.nupkg" for name in ("BGCS.Contracts", "BGCS", "BindGen-CS")}
        if not required.issubset({item["file"] for item in report["packageDigests"]}):
            raise RuntimeError("The explicit local feed does not contain the required installed products.")
        parent = ROOT / "artifacts/temporary"
        parent.mkdir(parents=True, exist_ok=True)
        if not parent.resolve().is_relative_to(ROOT):
            raise RuntimeError("The owned consumer root escaped the workspace.")
        with tempfile.TemporaryDirectory(prefix="installed-custom-target-", dir=parent) as directory:
            work = Path(directory)
            isolate_consumer_root(work)
            configuration = ET.Element("configuration")
            sources = ET.SubElement(configuration, "packageSources")
            ET.SubElement(sources, "clear")
            ET.SubElement(sources, "add", key="local-bgcs", value=str(packages))
            ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
            mapping = ET.SubElement(configuration, "packageSourceMapping")
            local = ET.SubElement(mapping, "packageSource", key="local-bgcs")
            for pattern in ("BGCS*", "BindGen-CS*"):
                ET.SubElement(local, "package", pattern=pattern)
            ET.SubElement(ET.SubElement(mapping, "packageSource", key="nuget.org"), "package", pattern="*")
            ET.ElementTree(configuration).write(work / "NuGet.config", encoding="utf-8", xml_declaration=True)
            environment = {"DOTNET_ROOT": str(dotnet.parent), "DOTNET_HOST_PATH": str(dotnet),
                "DOTNET_ROOT_" + verifier.host_runtime_identifier().rsplit("-", 1)[1].upper(): str(dotnet.parent),
                "NUGET_PACKAGES": str(work / "packages"), "NUGET_HTTP_CACHE_PATH": str(work / "nuget-http-cache")}
            run = functools.partial(verifier.run_process, environment_overrides=environment)
            state = prepare(str(dotnet), args.version, ROOT, work, evidence, run, verifier.write_project)
            tool_root = work / "tool"
            run([str(dotnet), "tool", "install", "BindGen-CS", "--version", args.version,
                "--tool-path", str(tool_root), "--configfile", str(work / "NuGet.config")], work, evidence, "custom-target-tool-install")
            tool = next((path for path in (tool_root / "bindgen-cs", tool_root / "bindgen-cs.exe",
                tool_root / "bindgen-cs.cmd") if path.is_file()), None)
            if tool is None:
                raise RuntimeError("The installed tool did not provide its declared command shim.")
            report["installedCustomTarget"] = verify_cli(tool, state, evidence, run)
            if any(digest(packages / item["file"]) != item["sha256"] for item in report["packageDigests"]):
                raise RuntimeError("The existing package bytes changed during focused consumption.")
            after = source_identity(ROOT)
            if after != source:
                raise RuntimeError("Verifier source identity changed during the focused installed consumption.")
            report["verifierSourceAtCompletion"] = after
            report["success"] = True
    finally:
        _write(evidence / "verification.json", report)


if __name__ == "__main__":
    main()
