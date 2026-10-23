#!/usr/bin/env python3
"""Confirm public NuGet indexing, exact package payloads and clean four-product consumption."""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import tempfile
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
INDEX = "https://api.nuget.org/v3/index.json"
FLAT = "https://api.nuget.org/v3-flatcontainer"


def payload(path: Path) -> dict[str, str]:
    with zipfile.ZipFile(path) as archive:
        all_files = [entry for entry in archive.infolist() if not entry.is_dir()]
        if len({entry.filename for entry in all_files}) != len(all_files):
            raise RuntimeError("A published package has duplicate payload entries: " + path.name)
        files = [entry for entry in all_files if entry.filename != ".signature.p7s"]
        return {entry.filename: hashlib.sha256(archive.read(entry)).hexdigest() for entry in files}


def index_versions(identity: str, timeout: float) -> set[str]:
    with urllib.request.urlopen(FLAT + "/" + identity.lower() + "/index.json", timeout=timeout) as response:
        data = response.read(1024 * 1024 + 1)
    if len(data) > 1024 * 1024:
        raise RuntimeError("The public package version index exceeds its bounded input size.")
    return set(json.loads(data)["versions"])


def wait_for_index(identities: list[str], version: str, timeout: float, query=index_versions, pause=time.sleep) -> None:
    deadline = time.monotonic() + timeout
    missing = set(identities)
    while missing:
        for identity in sorted(missing):
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise RuntimeError("NuGet indexing did not complete for: " + ", ".join(sorted(missing)))
            try:
                if version.lower() in query(identity, min(20, remaining)):
                    missing.remove(identity)
            except urllib.error.HTTPError as error:
                if error.code not in (404, 408, 429, 500, 502, 503, 504):
                    raise
            except (urllib.error.URLError, TimeoutError):
                pass
        if missing:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise RuntimeError("NuGet indexing did not complete for: " + ", ".join(sorted(missing)))
            pause(min(15, remaining))


def verify_public_payload(local: Path, identity: str, version: str, destination: Path, maximum_bytes: int) -> dict:
    url = FLAT + "/" + identity.lower() + "/" + version.lower() + "/" + identity.lower() + "." + version.lower() + ".nupkg"
    total = 0
    try:
        with urllib.request.urlopen(url, timeout=60) as response, destination.open("wb") as output:
            while True:
                chunk = response.read(1024 * 1024)
                if not chunk:
                    break
                total += len(chunk)
                if total > maximum_bytes + 1024 * 1024:
                    raise RuntimeError("A public NuGet package exceeds the declared distribution size bound.")
                output.write(chunk)
        expected, actual = payload(local), payload(destination)
        if expected != actual:
            differences = sorted(key for key in expected.keys() | actual.keys() if expected.get(key) != actual.get(key))
            raise RuntimeError("NuGet already contains different package payload bytes: " + identity + ": " + ", ".join(differences[:12]))
        canonical = json.dumps(actual, sort_keys=True, separators=(",", ":")).encode("utf-8")
        return {"id": identity, "version": version, "publicBytes": total, "payloadEntries": len(actual),
                "payloadManifestSha256": hashlib.sha256(canonical).hexdigest(),
                "allowedTransportDifference": "NuGet repository signature entry only"}
    finally:
        if destination.exists():
            destination.unlink()  # One fresh owned download at a time; do not retain every RID's native archive.


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--packages", type=Path, required=True)
    parser.add_argument("--dotnet", default=os.environ.get("DOTNET_HOST_PATH", "dotnet"))
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/supply-chain/nuget-public-verification.json")
    parser.add_argument("--index-timeout", type=float, default=1200)
    args = parser.parse_args()
    if not 0 < args.index_timeout <= 3600:
        parser.error("The public indexing timeout must be positive and bounded to one hour.")
    spec = importlib.util.spec_from_file_location("installed_product_helpers", ROOT / "eng/verify-packages.py")
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    source = helper.source_identity(ROOT)
    packages = args.packages.resolve()
    helper.verify_package_receipt(packages, args.version, source["revision"], True)
    manifest = helper.PRODUCT_MANIFEST
    identities = [*manifest["products"], *manifest["optionalProducts"], *manifest["technicalPackages"],
                  *("BindGen-CS." + rid for rid in manifest["toolRuntimeIdentifiers"])]
    selected = Path(args.dotnet) if Path(args.dotnet).is_file() else Path(shutil.which(args.dotnet) or "")
    if not selected.is_file():
        raise RuntimeError("The selected release SDK host is absent.")
    dotnet = str(selected.resolve())
    parent = ROOT / "artifacts/temporary"
    parent.mkdir(parents=True, exist_ok=True)
    if not parent.resolve().is_relative_to(ROOT):
        raise RuntimeError("Public consumer workspace escaped the owned repository.")
    minimum_free = int(os.environ.get("BGCS_MINIMUM_FREE_BYTES", str(2 * 1024 ** 3)))
    if minimum_free <= 0 or shutil.disk_usage(ROOT).free < minimum_free:
        raise RuntimeError("The public consumption gate requires its free-space floor.")
    report = {"schemaVersion": 1, "success": False, "version": args.version, "source": source,
              "feed": INDEX, "checks": [], "symbols": "Separate symbol pushes must succeed; this gate does not claim symbol-server indexing."}
    evidence = args.output.resolve().parent / "nuget-public-logs"
    evidence.mkdir(parents=True, exist_ok=True)
    try:
        wait_for_index(identities, args.version, args.index_timeout)
        report["checks"].append("all-declared-nupkg-versions-publicly-indexed")
        with tempfile.TemporaryDirectory(prefix="nuget-public-consumers-", dir=parent) as temporary:
            work = Path(temporary)
            report["payloads"] = [verify_public_payload(packages / f"{identity}.{args.version}.nupkg", identity,
                args.version, work / "current-public.nupkg", manifest["maximumPackageBytes"]) for identity in identities]
            environment = {"NUGET_PACKAGES": str(work / "packages"), "NUGET_HTTP_CACHE_PATH": str(work / "http-cache"),
                "DOTNET_ROOT": str(Path(dotnet).parent), "DOTNET_HOST_PATH": dotnet}
            def run(command, cwd, name):
                return helper.run_process(command, cwd, evidence, name, environment_overrides=environment)
            def run_optional(command, cwd, logs, name):
                return helper.run_process(command, cwd, logs, name, environment_overrides=environment)
            (work / "NuGet.Config").write_text('<configuration><packageSources><clear/><add key="nuget.org" value="' + INDEX + '"/></packageSources></configuration>\n', encoding="utf-8")
            report['optionalExtensions'] = helper.verify_optional_extension_consumers(dotnet, args.version, work, evidence, run_optional)
            report['checks'].append('public-five-optional-extensions-independent-core-only-consumption')
            tool_root = work / "tool"
            run([dotnet, "tool", "install", "BindGen-CS", "--version", args.version, "--tool-path", str(tool_root),
                 "--configfile", str(work / "NuGet.Config")], work, "public-tool-install")
            tool = next((path for path in (tool_root / "bindgen-cs", tool_root / "bindgen-cs.exe", tool_root / "bindgen-cs.cmd") if path.is_file()), None)
            if tool is None:
                raise RuntimeError("The public tool installation has no launcher.")
            version = json.loads(run([str(tool), "version", "--json"], work, "public-tool-version"))
            if version.get("parser", {}).get("assetDigestsVerified") is not True:
                raise RuntimeError("The public tool did not deploy its verified parser closure.")
            report["checks"].append("public-tool-clean-install-and-parser-identity")
            sdk = work / "sdk"
            helper.write_project(sdk, args.version, ["BGCS"])
            inputs = sdk / "input"
            inputs.mkdir()
            (inputs / "native.h").write_text("int native_add(int left,int right);\n", encoding="utf-8")
            (inputs / "widget.hpp").write_text("class Widget { public: Widget()=default; int Read()const{return 42;} };\n", encoding="utf-8")
            for language, header, namespace, output in (("c", "native.h", "PublicRelease.C", "GeneratedC"), ("cpp", "widget.hpp", "PublicRelease.Cpp", "GeneratedCpp")):
                config = {"schemaVersion": 2, "library": {"id": "public_probe", "nativeName": "public_probe"},
                    "source": {"language": language, "headers": [str(inputs / header)], "parseMacros": False},
                    "api": {"namespace": namespace, "name": "Api"},
                    "output": {"root": str(sdk / output)}, "cache": {"enabled": False}}
                (sdk / (language + ".json")).write_text(json.dumps(config), encoding="utf-8")
            (sdk / "Program.cs").write_text('''using System;
using BGCS;
await using BindingHost host=BindingGenerator.CreateDefault();
foreach(string path in new[]{"c.json","cpp.json"}) {
    GenerationResult result=await host.GenerateAsync(GenerationRequest.FromFile(path));
    if(!result.success)throw new InvalidOperationException(result.GetDiagnosticSummary());
}
''', encoding="utf-8")
            run([dotnet, "build", "Consumer.csproj", "-c", "Release", "-m:1", "-nr:false"], sdk, "public-sdk-build")
            run([dotnet, str(sdk / "bin/Release/net10.0/Consumer.dll")], sdk, "public-sdk-c-cpp-generation")
            run([str(tool), "generate", "c.json"], sdk, "public-tool-generation")
            if not list((sdk / "GeneratedCpp").rglob("*.cpp")):
                raise RuntimeError("The publicly restored SDK lost its C++ bridge capability.")
            report["checks"].append("public-sdk-and-tool-c-cpp-generation")
            contracts = work / "contracts"
            helper.write_project(contracts, args.version, ["BGCS.Core"])
            (contracts / "Program.cs").write_text('''using System;
using BGCS.Core.Versioning;
int major=ContractVersion.C_MAJOR;
if(major!=3)throw new InvalidOperationException("Wrong public contract major.");
Console.WriteLine(typeof(BGCS.Core.Native.NativeModule).FullName);
''', encoding="utf-8")
            run([dotnet, "build", "Consumer.csproj", "-c", "Release", "-m:1", "-nr:false"], contracts, "public-contracts-build")
            run([dotnet, str(contracts / "bin/Release/net10.0/Consumer.dll")], contracts, "public-contracts-consumer")
            runtime = work / "runtime"
            items = ET.Element("ItemGroup")
            sources = list((sdk / "GeneratedC").rglob("*.cs"))
            if not sources:
                raise RuntimeError("The public generated C consumer has no managed artifacts.")
            for path in sources:
                ET.SubElement(items, "Compile", Include=str(path))
            helper.write_project(runtime, args.version, ["BGCS.Runtime"], ET.tostring(items, encoding="unicode"))
            (runtime / "Program.cs").write_text('using System; Console.WriteLine(typeof(PublicRelease.C.Api).FullName);\n', encoding="utf-8")
            run([dotnet, "build", "Consumer.csproj", "-c", "Release", "-m:1", "-nr:false"], runtime, "public-runtime-build")
            run([dotnet, str(runtime / "bin/Release/net10.0/Consumer.dll")], runtime, "public-runtime-consumer")
            for project, only in ((contracts, "BGCS.Core"), (runtime, "BGCS.Runtime")):
                assets = json.loads((project / "obj/project.assets.json").read_text(encoding="utf-8"))
                if any(name.split("/")[0] != only for name in assets["libraries"]):
                    raise RuntimeError("A public standalone Contracts/Runtime consumer acquired another package dependency.")
            report["checks"].append("public-contracts-only-and-runtime-only-consumption")
        after = helper.source_identity(ROOT)
        if after != source:
            raise RuntimeError("Release source bytes changed during public product verification.")
        report.update(success=True, sourceAtCompletion=after, sourceBytesStable=True)
    except BaseException as error:
        report["error"] = str(error)
        raise
    finally:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("Confirmed public NuGet payloads and clean installs of all four products for " + args.version)


if __name__ == "__main__":
    main()
