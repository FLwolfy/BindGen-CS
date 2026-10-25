#!/usr/bin/env python3
"""Install and consume actual local BGCS products in isolated temporary projects."""
from __future__ import annotations

import argparse
from contextlib import ExitStack
import functools
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import signal
import stat
import subprocess
import tempfile
import time
import xml.etree.ElementTree as ET
import zipfile
import sys
import uuid
import importlib.util

ROOT = Path(__file__).resolve().parents[1]
PRODUCT_MANIFEST = json.loads((ROOT / "distribution/manifests/product-packages.json").read_text(encoding="utf-8"))
sys.path.insert(0, str(ROOT / "scripts"))
from acceptance_evidence import publication, source_identity
import cpp_acceptance_fixture
import installed_advanced_worker
import installed_custom_target
import installed_source_contributor
from native_fixture_build import compile_shared
from disk_budget import check_free_space, wait_for_process


def release_consumer_outputs(work: Path, evidence: Path, label: str, relative_paths: tuple[str, ...]) -> None:
    """Release only this run's completed executable copies, preserving source and plugin inputs."""
    allowed = {"sdk-published", "sdk/bin", "sdk/obj", "advanced-clang-sdk/bin", "advanced-clang-sdk/obj",
               "custom-target-sdk/bin", "custom-target-sdk/obj", "source-protocol-sdk/bin", "source-protocol-sdk/obj"}
    if (not relative_paths or len(set(relative_paths)) != len(relative_paths)
            or any(path not in allowed for path in relative_paths) or not re.fullmatch(r"[a-z-]+", label)):
        raise ValueError("Consumer output release requires distinct explicitly owned stage paths.")
    parent = ROOT / "artifacts/temporary"
    if (work.parent != parent or not work.name.startswith("package-consumers-")
            or work.resolve(strict=True) != work.absolute() or not work.resolve().is_relative_to(ROOT.resolve())):
        raise RuntimeError("Consumer output release escaped its newly owned temporary workspace.")

    def checked(path: Path):
        metadata = path.lstat()
        if (stat.S_ISLNK(metadata.st_mode)
                or getattr(metadata, "st_file_attributes", 0) & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)):
            raise RuntimeError("Consumer output release refuses linked paths: " + str(path))
        return metadata

    for ancestor in (ROOT, ROOT / "artifacts", parent, work):
        if not stat.S_ISDIR(checked(ancestor).st_mode):
            raise RuntimeError("The owned consumer boundary is no longer a directory.")
    owner = checked(work)
    owner_identity = owner.st_dev, owner.st_ino
    if evidence.resolve().is_relative_to(work.resolve()):
        raise RuntimeError("Consumer release evidence must remain outside its disposable workspace.")
    planned = []
    for relative in relative_paths:
        target = work / relative
        current = work
        for part in Path(relative).parts:
            current /= part
            if not current.exists() and not current.is_symlink():
                raise FileNotFoundError("A completed consumer output is missing: " + str(current))
            checked(current)
        if not stat.S_ISDIR(checked(target).st_mode) or not target.resolve(strict=True).is_relative_to(work.resolve()):
            raise RuntimeError("A completed consumer output escaped its owned boundary.")
        files, byte_count = 0, 0
        for directory, children, names in os.walk(target, followlinks=False):
            checked(Path(directory))
            for child in children:
                if not stat.S_ISDIR(checked(Path(directory) / child).st_mode):
                    raise RuntimeError("A consumer output child is no longer a directory.")
            for name in names:
                metadata = checked(Path(directory) / name)
                if not stat.S_ISREG(metadata.st_mode):
                    raise RuntimeError("A consumer output contains a nonregular file.")
                files += 1
                byte_count += metadata.st_size
        metadata = checked(target)
        planned.append((target, (metadata.st_dev, metadata.st_ino), {"path": relative, "files": files, "bytes": byte_count}))
    journal = evidence / ("released-" + label + ".json")
    if journal.exists():
        raise RuntimeError("Consumer release evidence already exists for this stage.")
    record = {"scope": "completed-owned-consumer-executable-copies", "complete": False,
              "freeBytesBefore": shutil.disk_usage(ROOT).free, "outputs": [row for _, _, row in planned]}
    journal.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    for target, identity, row in planned:
        check_free_space(ROOT)
        owner_now, target_now = checked(work), checked(target)
        if ((owner_now.st_dev, owner_now.st_ino) != owner_identity
                or (target_now.st_dev, target_now.st_ino) != identity
                or not target.resolve(strict=True).is_relative_to(work.resolve(strict=True))):
            raise RuntimeError("The exact owned consumer output changed before release.")
        shutil.rmtree(target)
        row["released"] = True
        journal.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        check_free_space(ROOT)
    record.update(complete=True, freeBytesAfter=shutil.disk_usage(ROOT).free)
    journal.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")


def preserve_failure_inputs(work: Path, evidence: Path, name: str) -> None:
    """Keep bounded text reproduction inputs while still removing heavy consumers."""
    if not work.resolve().is_relative_to((ROOT / "artifacts/temporary").resolve()):
        return
    if evidence.resolve().is_relative_to(work.resolve()):
        raise ValueError("Failure evidence must be outside the disposable consumer tree.")
    if Path(name).name != name:
        raise ValueError("Failure evidence names must be a single relative component.")
    destination = evidence / (name + "-inputs")
    allowed = {".json", ".h", ".hpp", ".hxx", ".c", ".cpp", ".cxx", ".cs", ".csproj", ".props", ".targets"}
    excluded = {"bin", "obj", ".store", ".git", "packages", "nuget-http-cache", "appbundle", "tool"}
    maximum_file, maximum_total = 2 * 1024 * 1024, 8 * 1024 * 1024
    captured, omitted, total = [], [], 0
    for directory, children, files in os.walk(work, followlinks=False):
        children[:] = sorted(child for child in children if child.casefold() not in excluded
                             and not (Path(directory) / child).is_symlink())
        for filename in sorted(files):
            source = Path(directory) / filename
            if source.suffix.lower() not in allowed or source.is_symlink():
                continue
            relative = source.relative_to(work).as_posix()
            size = source.stat().st_size
            if size > maximum_file or total + size > maximum_total:
                omitted.append({"path": relative, "bytes": size, "reason": "bounded-failure-evidence-limit"})
                continue
            with source.open("rb") as stream:
                payload = stream.read(maximum_file + 1)
            if len(payload) > maximum_file or total + len(payload) > maximum_total:
                omitted.append({"path": relative, "bytes": len(payload), "reason": "bounded-failure-evidence-limit"})
                continue
            target = destination / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(payload)
            total += len(payload)
            captured.append({"path": relative, "bytes": len(payload), "sha256": hashlib.sha256(payload).hexdigest()})
    (evidence / (name + "-inputs.json")).write_text(json.dumps({"scope": "bounded-text-reproduction-inputs",
        "totalBytes": total, "maximumFileBytes": maximum_file, "maximumTotalBytes": maximum_total,
        "files": captured, "omitted": omitted}, indent=2), encoding="utf-8")


def stop_process_tree(process: subprocess.Popen) -> None:
    if process.poll() is not None:
        return
    if os.name == "nt":
        taskkill = Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32/taskkill.exe"
        subprocess.run([str(taskkill), "/PID", str(process.pid), "/T", "/F"],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                       timeout=30, check=False, creationflags=subprocess.CREATE_NO_WINDOW)
        if process.poll() is None:
            process.kill()
    else:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
    process.wait(timeout=30)


def emit_failure_log_tails(log_path: Path, error_path: Path | None) -> None:
    """Expose bounded child diagnostics while preserving each complete evidence file."""
    for path in (log_path, error_path):
        if path is None:
            continue
        try:
            with path.open("rb") as stream:
                stream.seek(0, os.SEEK_END)
                size = stream.tell()
                stream.seek(max(0, size - 8192))
                tail = stream.read(8192).decode("utf-8-sig", errors="replace")
            print(f"\n--- {path.name}: last {min(size, 8192)} of {size} bytes ---\n{tail}", file=sys.stderr, flush=True)
        except OSError as failure:
            print(f"Could not read failed child log '{path.name}': {failure}", file=sys.stderr, flush=True)


def run_process(arguments: list[str], work: Path, evidence: Path, name: str, timeout: int = 900,
                environment_overrides: dict[str, str] | None = None, separate_stderr: bool = False) -> str:
    environment = dict(os.environ)
    for name_to_remove in tuple(environment):
        # Consumer subprocesses must use the explicitly selected SDK, rather than
        # inherited build-server/resolver state or a desktop native Platform.
        if (name_to_remove.casefold() == "platform"
                or name_to_remove.upper().startswith(("MSBUILD", "DOTNET_MSBUILD_SDK_RESOLVER_"))
                or name_to_remove in ("BGCS_CLANG_RUNTIME_DIR", "DYLD_LIBRARY_PATH")):
            environment.pop(name_to_remove, None)
    environment.update(environment_overrides or {})
    log_path = evidence / f"{name}.log"
    error_path = evidence / (name + ".stderr.log") if separate_stderr else None
    try:
        check_free_space(ROOT)
        with log_path.open("wb") as log, ExitStack() as streams:
            error = streams.enter_context(error_path.open("wb")) if error_path is not None else subprocess.STDOUT
            process = subprocess.Popen(arguments, cwd=work, stdout=log, stderr=error,
                                       env=environment, start_new_session=os.name != "nt",
                                       creationflags=subprocess.CREATE_NO_WINDOW | subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0)
            try:
                code = wait_for_process(process, ROOT, timeout)
            except BaseException:
                stop_process_tree(process)
                raise
    except BaseException:
        emit_failure_log_tails(log_path, error_path)
        try:
            preserve_failure_inputs(work, evidence, name)
        except OSError as failure:
            (evidence / (name + "-input-capture-error.log")).write_text(str(failure), encoding="utf-8")
        raise
    if code:
        emit_failure_log_tails(log_path, error_path)
        try:
            preserve_failure_inputs(work, evidence, name)
        except OSError as failure:
            (evidence / (name + "-input-capture-error.log")).write_text(str(failure), encoding="utf-8")
        raise RuntimeError(f"{name} exited {code}; see {log_path}")
    return log_path.read_text(encoding="utf-8-sig", errors="replace")


def write_project(directory: Path, version: str, references: list[str], extra: str = "", sdk: bool = False) -> None:
    directory.mkdir(parents=True, exist_ok=True)
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    properties = ET.SubElement(project, "PropertyGroup")
    for name, value in {"TargetFramework": "net10.0", "OutputType": "Exe", "AllowUnsafeBlocks": "true", "Nullable": "enable",
                        "ImplicitUsings": "disable", "TreatWarningsAsErrors": "true", "UseSharedCompilation": "false"}.items():
        ET.SubElement(properties, name).text = value
    packages = ET.SubElement(project, "ItemGroup")
    for name in references:
        ET.SubElement(packages, "PackageReference", Include=name, Version=version)
    if extra:
        wrapper = ET.fromstring("<Extra>" + extra + "</Extra>")
        for child in wrapper:
            project.append(child)
    ET.ElementTree(project).write(directory / "Consumer.csproj", encoding="utf-8", xml_declaration=True)


def native_filename() -> str:
    return "package_probe.dll" if os.name == "nt" else "libpackage_probe.dylib" if platform.system() == "Darwin" else "libpackage_probe.so"


def verify_sdk_content_copy(package: Path, output: Path) -> None:
    """Check actual ordinary build/publish copy results against the delivered contentFiles bytes."""
    prefix = "contentFiles/any/net10.0/"
    with zipfile.ZipFile(package) as archive:
        expected = {item.filename[len(prefix):]: hashlib.sha256(archive.read(item)).hexdigest()
                    for item in archive.infolist() if not item.is_dir() and item.filename.startswith(prefix)}
    root = output / "bgcs/worker-assets"
    actual = {path.relative_to(output).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
              for path in root.rglob("*") if path.is_file()}
    if (not expected or actual != expected or any(path.is_symlink() or not path.resolve().is_relative_to(output.resolve())
            for path in root.rglob("*"))):
        raise RuntimeError("Standard SDK contentFiles were not copied with their exact relative paths and bytes.")


def host_runtime_identifier() -> str:
    machine = platform.machine().lower()
    architecture = "arm64" if machine in ("arm64", "aarch64") else "x64" if machine in ("amd64", "x86_64") else None
    if architecture is None:
        raise RuntimeError(f"Package consumption has not certified host architecture {machine}.")
    return ("win" if os.name == "nt" else "osx" if platform.system() == "Darwin" else "linux") + "-" + architecture


def verify_package_receipt(packages: Path, version: str, revision: str, complete: bool) -> dict:
    """Bind installed-product evidence to the exact physical packages being restored."""
    receipt_path = packages / "package-verification.json"
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    if receipt.get("schemaVersion") != 1 or receipt.get("version") != version:
        raise RuntimeError("The packaging receipt does not describe the requested product version.")
    files = {file.name: file for file in packages.glob(f"*.{version}.*nupkg")}
    if complete:
        expected = {f"{name}.{version}.nupkg" for name in PRODUCT_MANIFEST["products"] + PRODUCT_MANIFEST["optionalProducts"] + PRODUCT_MANIFEST["technicalPackages"]}
        expected |= {f"BindGen-CS.{rid}.{version}.nupkg" for rid in PRODUCT_MANIFEST["toolRuntimeIdentifiers"]}
        expected |= {f"{name}.{version}.snupkg" for name in PRODUCT_MANIFEST["symbolProducts"]}
        if files.keys() != expected:
            raise RuntimeError("Complete certification requires the declared physical product, RID and symbol package set.")
    entries = receipt.get("packages", [])
    if not entries or len({entry["file"] for entry in entries}) != len(entries) or {entry["file"] for entry in entries} != files.keys():
        raise RuntimeError("The physical product/symbol set differs from its packaging receipt.")
    for entry in entries:
        path = files[entry["file"]]
        with path.open("rb") as stream:
            actual = hashlib.file_digest(stream, "sha256").hexdigest()
        if path.stat().st_size != entry["bytes"] or actual != entry["sha256"]:
            raise RuntimeError("A physical package changed after packaging verification: " + path.name)
    if complete and (receipt.get("completeProductSet") is not True
                     or receipt.get("sourceRevision") != revision
                     or receipt.get("sourceRevisionStable") is not True
                     or receipt.get("sourceBytesStable") is not True
                     or receipt.get("checkedOutSourceSha256") != source_identity(ROOT)["checkedOutSourceSha256"]
                     or receipt.get("sourceWorktreeDirty") is not False
                     or receipt.get("sourceAtCompletion", {}).get("sourceWorktreeDirty") is not False):
        raise RuntimeError("Complete installed-product certification requires a clean, complete package set from this exact source revision.")
    return {"receiptSha256": hashlib.sha256(receipt_path.read_bytes()).hexdigest(),
            "sourceRevision": receipt.get("sourceRevision"),
            "sourceWorktreeDirty": receipt.get("sourceWorktreeDirty"),
            "sourceRevisionStable": receipt.get("sourceRevisionStable"),
            "sourceBytesStable": receipt.get("sourceBytesStable"),
            "checkedOutSourceSha256": receipt.get("checkedOutSourceSha256"),
            "completeProductSet": receipt.get("completeProductSet"),
            "physicalPackageDigestsVerified": True, "packages": entries}


def schema_differences(delivered, actual, path: str = ""):
    if type(delivered) is not type(actual):
        yield {"path": path or "/", "delivered": delivered, "installed": actual}
    elif isinstance(delivered, dict):
        for key in sorted(set(delivered) | set(actual)):
            pointer = path + "/" + key.replace("~", "~0").replace("/", "~1")
            if key not in delivered:
                yield {"path": pointer, "deliveredMissing": True, "installed": actual[key]}
            elif key not in actual:
                yield {"path": pointer, "delivered": delivered[key], "installedMissing": True}
            else:
                yield from schema_differences(delivered[key], actual[key], pointer)
    elif isinstance(delivered, list):
        if len(delivered) != len(actual):
            yield {"path": path, "deliveredLength": len(delivered), "installedLength": len(actual)}
        for index, (left, right) in enumerate(zip(delivered, actual)):
            yield from schema_differences(left, right, path + "/" + str(index))
    elif delivered != actual:
        yield {"path": path or "/", "delivered": delivered, "installed": actual}


def configuration(namespace: str, output: str, runtime: str = "package", single: bool = False, cpp: bool = False) -> dict:
    return {"schemaVersion": 2, "library": {"id": "package_probe", "nativeName": "package_probe"},
            "source": {"language": "cpp" if cpp else "c", "headers": ["widget.hpp" if cpp else "native.h"]},
            "api": {"namespace": namespace, "name": "NativeApi", "names": {"native_add": "Add"}},
            "runtime": {"mode": runtime}, "output": {"root": output, "layout": "singleFile" if single else "split"}}


def copy_installed_plugin_contract(host_output: Path, plugin_output: Path) -> None:
    """Bind the plugin manifest to the exact shared Contracts bytes of its installed SDK host."""
    source = host_output / "BGCS.Core.dll"
    destination = plugin_output / source.name
    if not source.is_file():
        raise FileNotFoundError("The installed SDK host has no shared Contracts execution assembly.")
    if destination.exists():
        if not destination.is_file() or hashlib.sha256(destination.read_bytes()).digest() != hashlib.sha256(source.read_bytes()).digest():
            raise RuntimeError("The plugin Contracts asset differs from its exact installed SDK host.")
    else:
        shutil.copyfile(source, destination)


def record_source_completion(report: dict, source_at_start: dict, final_source: dict, *, require_clean: bool) -> None:
    """Preserve the original source identity independently of managed source-file loop variables."""
    report["sourceAtCompletion"] = {"sourceRevision": final_source["revision"],
                                    "sourceWorktreeDirty": final_source["workingTreeDirty"],
                                    "checkedOutSourceSha256": final_source["checkedOutSourceSha256"]}
    report["sourceBytesStable"] = final_source == source_at_start
    if not report["sourceBytesStable"] or (require_clean and final_source["workingTreeDirty"]):
        report["success"] = False
        raise RuntimeError("The verifier source changed during installed-product consumption or is not clean for complete certification.")









def verify_optional_extension_consumers(dotnet: str, version: str, work: Path, evidence: Path, run) -> list[dict]:
    """Restore each independently selected extension without the full generator product."""
    classes = {"Android": "AndroidPlugin", "AppleMobile": "AppleMobilePlugin", "BrowserWasm": "BrowserWasmPlugin",
               "FreeBsd": "FreeBsdPlugin", "NativeAot": "NativeAotPlugin"}
    receipts = []
    for family, plugin in classes.items():
        product = "BGCS.Extension." + family
        directory = work / ("extension-" + family)
        write_project(directory, version, [product])
        source = """using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BGCS.Core.Plugins;
using BGCS.Extension.FAMILY;
IBindingPlugin plugin = new PLUGIN();
Registry registry = new();
plugin.Configure(registry, new(plugin.descriptor.id, JsonSerializer.SerializeToElement(new { }), AppContext.BaseDirectory));
if (!registry.ids.SetEquals(plugin.descriptor.services.Select(value => value.id))) throw new InvalidOperationException("Optional plugin registration does not match its declared services.");
string[] forbidden = { "BGCS", "BGCS.Engine", "BGCS.Infrastructure", "BGCS.Frontend.Clang", "BGCS.Runtime" };
if (AppDomain.CurrentDomain.GetAssemblies().Any(value => forbidden.Contains(value.GetName().Name))) throw new InvalidOperationException("Optional Core-only composition loaded generator/runtime implementation.");
Console.WriteLine(plugin.descriptor.id + ": independent Core-only registration passed.");
sealed class Registry : IExtensionRegistryBuilder
{
    internal HashSet<string> ids { get; } = new(StringComparer.Ordinal);
    public void RegisterFactory<TService>(string id, Func<ExtensionActivationContext, TService> factory) where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!ids.Add(id)) throw new InvalidOperationException("Duplicate optional service identity.");
    }
}
""".replace("FAMILY", family).replace("PLUGIN", plugin)
        (directory / "Program.cs").write_text(source, encoding="utf-8")
        run([dotnet, "build", "Consumer.csproj", "-c", "Release", "-m:1", "-nr:false"], directory, evidence, "optional-" + family + "-build")
        output = directory / "bin/Release/net10.0"
        run([dotnet, str(output / "Consumer.dll")], directory, evidence, "optional-" + family + "-registration")
        deps = json.loads((output / "Consumer.deps.json").read_text(encoding="utf-8-sig"))
        delivered = {name.rsplit("/", 1)[0] for name in deps["libraries"]}
        if delivered != {"Consumer", "BGCS.Core", product}:
            raise RuntimeError("An optional extension unexpectedly depends on another product or third-party package: " + product + ": " + str(delivered))
        if (output / "BGCS.Engine.dll").exists() or (output / "BGCS.Frontend.Clang.dll").exists():
            raise RuntimeError("Optional extension consumption copied the generation engine or parser.")
        receipts.append({"product": product, "pluginType": plugin, "coreOnlyProductDependency": True,
            "assemblySha256": hashlib.sha256((output / (product + ".dll")).read_bytes()).hexdigest(),
            "coreSha256": hashlib.sha256((output / "BGCS.Core.dll").read_bytes()).hexdigest()})
    return receipts

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=os.environ.get("DOTNET_HOST_PATH", "dotnet"))
    parser.add_argument("--packages", type=Path, default=ROOT / "artifacts/packages")
    parser.add_argument("--version", required=True)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/verification/packages")
    parser.add_argument("--sdk-only", action="store_true", help="Local partial SDK verification; not full product certification.")
    parser.add_argument("--host-tool", action="store_true", help="With --sdk-only, also consume the current host's explicitly partial tool package set.")
    parser.add_argument("--native-aot", action="store_true", help="Also run actual NativeAOT consumers in a partial SDK check. Optional developer application check.")
    parser.add_argument("--native-feature-matrix", action="store_true", help="With a partial host tool, optionally invoke the native ABI/aggregate/C++/lifetime matrix under NativeAOT as a developer application check.")
    parser.add_argument("--wasm", action="store_true", help="Additionally run both browser interpreter and AOT developer checks. Requires wasm-tools and Chromium; optional developer application checks.")
    args = parser.parse_args()
    if args.host_tool and not args.sdk_only:
        parser.error("--host-tool requires --sdk-only; complete certification already installs the tool.")
    if args.sdk_only and (args.wasm or args.native_feature_matrix) and not args.host_tool:
        parser.error("Partial browser/native feature matrices require --host-tool to consume an actual installed generator product.")
    if not re.fullmatch(r"4\.1\.2(?:-ci(?:\.[0-9A-Za-z-]+)*)?", args.version):
        parser.error("Only 4.1.2 or private 4.1.2-ci candidate packages can be certified from this tree.")
    packages = args.packages.resolve()
    evidence = args.output.resolve()
    evidence.mkdir(parents=True, exist_ok=True)
    source_at_start = source_identity(ROOT)
    revision, dirty = source_at_start["revision"], source_at_start["workingTreeDirty"]
    report = {"schemaVersion": 1, "version": args.version, "sourceRevision": revision, "sourceWorktreeDirty": dirty,
              "checkedOutSourceSha256": source_at_start["checkedOutSourceSha256"],
              "completeProductSet": not args.sdk_only, "success": False, "checks": []}
    rid = host_runtime_identifier()
    explicit_host = Path(args.dotnet)
    selected_host = explicit_host.resolve() if explicit_host.is_file() else Path(shutil.which(args.dotnet) or "")
    if not selected_host.is_file():
        raise FileNotFoundError("The explicitly selected .NET SDK host is unavailable.")
    selected_host = selected_host.resolve()
    args.dotnet = str(selected_host)
    # Tool apphosts need the same intentionally selected installation as dotnet CLI
    # commands. A private SDK installation need not be registered globally.
    environment_overrides = {"DOTNET_ROOT": str(selected_host.parent), "DOTNET_HOST_PATH": str(selected_host),
                             "DOTNET_ROOT_" + rid.rsplit("-", 1)[1].upper(): str(selected_host.parent)}
    run = functools.partial(run_process, environment_overrides=environment_overrides)
    report["hostRuntimeIdentifier"] = rid
    report["nativeAotSmokeRequested"] = args.native_aot
    report["nativeAotFeatureMatrixRequested"] = args.native_feature_matrix
    report["wasmInterpreterAndAotRequested"] = args.wasm
    temporary_parent = ROOT / "artifacts/temporary"
    temporary_parent.mkdir(parents=True, exist_ok=True)
    if not temporary_parent.resolve().is_relative_to(ROOT):
        raise RuntimeError("The disposable installed-product workspace escaped the repository through a link.")
    try:
        minimum_free = int(os.environ.get("BGCS_MINIMUM_FREE_BYTES", str(50 * 1024 ** 3)))
        if minimum_free <= 0 or shutil.disk_usage(ROOT).free < minimum_free:
            raise RuntimeError("Installed product certification requires its declared positive free-space floor.")
        if not args.sdk_only and dirty:
            raise RuntimeError("Complete installed-product certification requires a clean verifier working tree at the packaged revision.")
        report["packagingEvidence"] = verify_package_receipt(packages, args.version, revision, not args.sdk_only)
        sdk_version = run([args.dotnet, "--version"], ROOT, evidence, "selected-dotnet-sdk").strip()
        if sdk_version != "10.0.402":
            raise RuntimeError("Installed product certification requires the exact locked .NET SDK10.0.402.")
        report["dotnetSdkVersion"] = sdk_version
        run([args.dotnet, "--list-runtimes"], ROOT, evidence, "selected-dotnet-runtimes")
        with tempfile.TemporaryDirectory(prefix="package-consumers-", dir=temporary_parent) as temp:
            work = Path(temp)
            installed_custom_target.isolate_consumer_root(work)
            # Every local test version is unique. Keeping its packages in the user's
            # permanent global cache both hides stale restore problems and grows disk
            # usage after repeated certification. This run owns and removes both caches.
            environment_overrides["NUGET_PACKAGES"] = str(work / "packages")
            environment_overrides["NUGET_HTTP_CACHE_PATH"] = str(work / "nuget-http-cache")
            environment_overrides["NUGET_SCRATCH"] = str(work / "nuget-scratch")
            environment_overrides["DOTNET_CLI_HOME"] = str(work / "dotnet-cli-home")
            report["restoreIsolation"] = {"privateGlobalPackages": True, "privateHttpCache": True,
                                          "privateScratch": True, "privateCliHome": True,
                                          "disposedWithConsumerWorkspace": True}
            nuget = ET.Element("configuration")
            sources = ET.SubElement(nuget, "packageSources")
            ET.SubElement(sources, "clear")
            ET.SubElement(sources, "add", key="local-bgcs", value=str(packages))
            ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
            ET.ElementTree(nuget).write(work / "NuGet.config", encoding="utf-8", xml_declaration=True)
            report["optionalExtensions"] = verify_optional_extension_consumers(args.dotnet, args.version, work, evidence, run)
            report["checks"].append("installed-five-optional-extensions-core-only-registration-and-closure")
            sdk = work / "sdk"
            plugin = work / "plugin"
            write_project(plugin, args.version, ["BGCS.Core"])
            # A library avoids giving the generator's consumer application another entry point.
            document = ET.parse(plugin / "Consumer.csproj")
            document.find("./PropertyGroup/OutputType").text = "Library"
            ET.SubElement(document.find("./PropertyGroup"), "AssemblyName").text = "Package.Plugin"
            document.write(plugin / "Consumer.csproj", encoding="utf-8", xml_declaration=True)
            (plugin / "Plugin.cs").write_text('''using System;
using System.Threading;
using BGCS.Core;
using BGCS.Core.Interop;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;
using BGCS.Core.Requests;
public sealed class PackagePlugin : IBindingPlugin
{
    public PluginDescriptor descriptor => new("package.plugin", "1.0.0", [new("package.transform", ExtensionPort.InteropTransform)]);
    public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration configuration)
        => registry.RegisterFactory<IInteropTransform>("package.transform", _ => new NeutralTransform());
    private sealed class NeutralTransform : IInteropTransform
    {
        public string id => "package.transform";
        public int priority => 0;
        public InteropModule Transform(InteropModule module, AnalysisContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(module.libraryId, module.targetId, module.pointerSize, module.types, module.functions,
                module.diagnostics.Add(new(BindingDiagnosticSeverity.Information, "Package plugin executed.", "BGCS-PACKAGE-PLUGIN"))) { byteOrder = module.byteOrder, constants = module.constants };
        }
    }
}
''', encoding="utf-8")
            shutil.copyfile(ROOT / "tests/fixtures/plugins/NeutralLoweringPlugin/SourceFixturePlugin.cs", plugin / "SourceFixturePlugin.cs")
            write_project(sdk, args.version, ["BGCS"], '<ItemGroup><ProjectReference Include="../plugin/Consumer.csproj" /></ItemGroup>')
            (sdk / "native.h").write_text('''#if defined(_WIN32)
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif
EXPORT int native_add(int left, int right);
''', encoding="utf-8")
            (sdk / "native.c").write_text('#include "native.h"\nEXPORT int native_add(int left, int right) { return left + right; }\n', encoding="utf-8")
            (sdk / "widget.hpp").write_text('class Widget { public: Widget() = default; int Value() const { return 42; } };\n', encoding="utf-8")
            configurations = [("package", "Package.Probe", "Generated", False, False), ("source", "Package.Source", "Source", False, False),
                              ("source", "Package.Single", "Single", True, False), ("package", "Package.Cpp", "Cpp", False, True)]
            for mode, namespace, destination, single, cpp in configurations:
                declared = configuration(namespace, destination, mode, single, cpp)
                if destination == 'Source':
                    declared['runtime']['sourceFeatures'] = ['runtime.complete']
                (sdk / (destination + ".json")).write_text(json.dumps(declared), encoding="utf-8")
            (sdk / "Program.cs").write_text('''using System;
using System.IO;
using System.Linq;
using BGCS;
using BGCS.Configuration;
await using BindingHost host = BindingGenerator.CreateBuilder().AddPlugin<PackagePlugin>()
    .Build();
SchemaCatalog schemas = new();
File.WriteAllText("sdk-binding.schema.json", schemas.CreateBindingSchema().ToJsonString());
File.WriteAllText("sdk-workspace.schema.json", schemas.CreateWorkspaceSchema().ToJsonString());
File.WriteAllText("sdk-extension.schema.json", schemas.CreatePluginSchema().ToJsonString());
foreach (string file in new[] { "Generated", "Source", "Single", "Cpp" })
{
    Console.WriteLine("Running installed SDK request: " + file);
    GenerationRequest request = GenerationRequest.FromFile(file + ".json");
    GenerationResult result = await host.GenerateAsync(request);
    if (!result.success) throw new InvalidOperationException(result.GetDiagnosticSummary());
    if (!result.diagnostics.Any(diagnostic => diagnostic.code == "BGCS-PACKAGE-PLUGIN")) throw new InvalidOperationException("The Contracts-only plugin did not execute.");
    if (!result.artifacts.Any(artifact => artifact.relativePath.EndsWith("/contributions/fixture.source/helpers/generated.h", StringComparison.Ordinal))) throw new InvalidOperationException("The Contracts-only source contributor's public header was omitted.");
    if (!result.artifacts.Any(artifact => artifact.relativePath.EndsWith("/contributions/fixture.source/helpers/payload.txt", StringComparison.Ordinal))) throw new InvalidOperationException("The Contracts-only source contributor's resource was omitted.");
    if (file == "Cpp" && !result.artifacts.Any(artifact => artifact.relativePath.EndsWith(".cpp", StringComparison.Ordinal))) throw new InvalidOperationException("C++ bridge functionality was omitted.");
    if (file == "Single" && result.artifacts.Count(artifact => artifact.mediaType == "text/x-csharp") != 1) throw new InvalidOperationException("Single-file Runtime closure was omitted.");
    Console.WriteLine("Completed installed SDK request: " + file);
}
Console.WriteLine("Installed SDK C/C++ generation and Contracts-only plugin passed.");
''', encoding="utf-8")
            run([args.dotnet, "build", "Consumer.csproj", "--configuration", "Release", "-m:1", "-nr:false"], sdk, evidence, "sdk-and-contracts-plugin-build")
            verify_sdk_content_copy(packages / f"BGCS.{args.version}.nupkg", sdk / "bin/Release/net10.0")
            published_sdk = work / "sdk-published"
            run([args.dotnet, "publish", "Consumer.csproj", "--configuration", "Release", "--no-build", "--no-restore",
                 "--output", str(published_sdk), "-m:1", "-nr:false"], sdk, evidence, "sdk-standard-content-publish")
            verify_sdk_content_copy(packages / f"BGCS.{args.version}.nupkg", published_sdk)
            report["checks"].append("installed-sdk-standard-contentfiles-build-and-publish")
            plugin_output = plugin / "bin/Release/net10.0"
            copy_installed_plugin_contract(sdk / "bin/Release/net10.0", plugin_output)
            plugin_assets = sorted([*plugin_output.glob("*.dll"), *plugin_output.glob("*.deps.json")])
            plugin_manifest_path = plugin_output / "source-plugin.json"
            plugin_manifest_path.write_text(json.dumps({
                "schemaVersion": 1, "assembly": "Package.Plugin.dll", "entryType": "BGCS.Tests.NeutralPlugin.SourceFixturePlugin",
                "plugin": {"id": "fixture.source-plugin", "version": "1.0.0", "contractsMajor": 4,
                           "services": [{"id": "fixture.source/helpers", "port": "sourceContributor", "priority": 0}],
                           "configurationSchema": installed_source_contributor.configuration_schema()},
                "assets": [{"path": path.name, "digest": {"value": hashlib.sha256(path.read_bytes()).hexdigest()}} for path in plugin_assets]
            }, indent=2) + "\n", encoding="utf-8")
            for _, _, destination, _, _ in configurations:
                configuration_path = sdk / (destination + ".json")
                sdk_configuration = json.loads(configuration_path.read_text())
                sdk_configuration["extensions"] = {"plugins": [str(plugin_manifest_path)]}
                configuration_path.write_text(json.dumps(sdk_configuration, indent=2) + "\n", encoding="utf-8")
            run([args.dotnet, str(sdk / "bin/Release/net10.0/Consumer.dll")], sdk, evidence, "sdk-and-contracts-plugin")
            run([args.dotnet, str(published_sdk / "Consumer.dll")], sdk, evidence, "sdk-clean-published-worker-generation")
            report["checks"].append("installed-sdk-clean-published-worker-without-global-package-fallback")
            report["checks"].append("installed-sdk-c-cpp-and-contracts-only-plugin")
            with zipfile.ZipFile(packages / f"BGCS.{args.version}.nupkg") as delivered_sdk:
                for kind, filename in (("binding", "bindgen.schema.json"), ("workspace", "workspace.schema.json"), ("extension", "extension.schema.json")):
                    delivered = json.loads(delivered_sdk.read("schemas/" + filename))
                    actual = json.loads((sdk / ("sdk-" + kind + ".schema.json")).read_text(encoding="utf-8"))
                    if delivered != actual:
                        differences = list(schema_differences(delivered, actual))
                        (evidence / (kind + "-schema-differences.json")).write_text(json.dumps(differences, indent=2) + "\n", encoding="utf-8")
                        raise RuntimeError("The delivered " + kind + " schema differs from the actual installed SDK contract: " + ", ".join(item["path"] for item in differences[:8]))
            report["checks"].append("delivered-schemas-match-installed-sdk-contracts")
            release_consumer_outputs(work, evidence, "sdk", ("sdk-published", "sdk/bin", "sdk/obj"))
            advanced_worker = installed_advanced_worker.prepare(args.dotnet, args.version, ROOT, work, evidence, run, write_project)
            report["installedAdvancedWorkerPlugin"] = advanced_worker["evidence"]
            report["checks"].append("installed-advanced-clang-plugin-sdk-and-runtime-compile")
            release_consumer_outputs(work, evidence, "advanced-worker", ("advanced-clang-sdk/bin", "advanced-clang-sdk/obj"))
            custom_target = installed_custom_target.prepare(args.dotnet, args.version, ROOT, work, evidence, run, write_project)
            release_consumer_outputs(work, evidence, "custom-target", ("custom-target-sdk/bin", "custom-target-sdk/obj"))
            source_contributor = installed_source_contributor.prepare(args.dotnet, args.version, work, evidence,
                plugin_manifest_path, run, write_project)
            release_consumer_outputs(work, evidence, "source-contributor", ("source-protocol-sdk/bin", "source-protocol-sdk/obj"))
            for folder, namespace, reference in (("Generated", "Package.Probe", ["BGCS.Runtime"]), ("Source", "Package.Source", []), ("Single", "Package.Single", [])):
                consumer = work / (folder + "Consumer")
                managed = next((sdk / folder).glob("*/managed"))
                binary = compile_shared(sdk, sdk / folder, work / "external-native" / folder / native_filename(), [sdk / "native.c"], run, evidence, exports=("contributed_add",))
                extra = '<ItemGroup><Compile Include="' + str(managed / "**/*.cs") + '" /><None Include="' + str(binary) + '" Link="' + native_filename() + '" CopyToOutputDirectory="PreserveNewest" /></ItemGroup>'
                write_project(consumer, args.version, reference, extra)
                complete_proof = '''unsafe
{
    global::Package.Source.Runtime.Bool8 enabled = true;
    int value = 42;
    global::Package.Source.Runtime.Pointer<int> pointer = new(&value);
    using var storage = new global::Package.Source.Runtime.NativeAlignedBuffer(13, 64);
    if (!(bool)enabled || *pointer.handle != 42 || (nuint)storage.address % 64 != 0 || storage.byteLength != 13)
        throw new InvalidOperationException("Installed complete Runtime primitives failed.");
}
if (typeof(global::Package.Source.Runtime.Bool8).Assembly != typeof(NativeApi).Assembly)
    throw new InvalidOperationException("Complete source Runtime primitive came from another assembly.");
''' if folder == 'Source' else ''
                (consumer / "Program.cs").write_text(f'''using System;
using System.IO;
using System.Linq;
using {namespace};
using NativeApi api = NativeApi.Load(Path.Combine(AppContext.BaseDirectory, "{native_filename()}"));
if (api.Add(19, 23) != 42) throw new InvalidOperationException("Installed runtime native call failed.");
if (api.ContributedAdd(29) != 41 || ContributedHelper.Marker != 41) throw new InvalidOperationException("The installed Contracts-only source contributor did not reach native and managed consumers.");
{complete_proof}
Console.WriteLine("{folder} consumer native invocation passed.");
''', encoding="utf-8")
                run([args.dotnet, "build", "Consumer.csproj", "--configuration", "Release", "-m:1", "-nr:false"], consumer, evidence, folder.lower() + "-consumer-build")
                run([args.dotnet, str(consumer / "bin/Release/net10.0/Consumer.dll")], consumer, evidence, folder.lower() + "-consumer")
                assets = json.loads((consumer / "obj/project.assets.json").read_text(encoding="utf-8"))
                if any("clang" in name.lower() or name.startswith("BGCS/") for name in assets["libraries"]):
                    raise RuntimeError(f"{folder} runtime-only consumer acquired generator/parser dependencies.")
                if not reference and any(details.get('type') == 'package' for details in assets['libraries'].values()):
                    raise RuntimeError(f'{folder} source Runtime consumer acquired a package dependency.')
                if not reference:
                    dependencies = json.loads((consumer / 'bin/Release/net10.0/Consumer.deps.json').read_text())
                    if any(name.split('/')[0].startswith('BGCS') for name in dependencies['libraries']):
                        raise RuntimeError(f'{folder} source Runtime consumer references a BGCS assembly.')
                report["checks"].append(folder.lower() + "-native-invocation")
                if folder == 'Source':
                    report['checks'].append('installed-complete-runtime-source-primitives-without-bgcs-references')
                if args.native_aot:
                    published = consumer / "aot"
                    run([args.dotnet, "publish", "Consumer.csproj", "--configuration", "Release", "--runtime", rid,
                         "--self-contained", "true", "--output", str(published), "-p:PublishAot=true", "-m:1", "-nr:false"],
                        consumer, evidence, folder.lower() + "-native-aot-publish")
                    executable = published / ("Consumer.exe" if os.name == "nt" else "Consumer")
                    if not executable.is_file() or not (published / native_filename()).is_file():
                        raise RuntimeError(f"The {folder} installed NativeAOT consumer lacks its executable or declared native fixture.")
                    run([str(executable)], consumer, evidence, folder.lower() + "-native-aot-invocation")
                    report["checks"].append(folder.lower() + "-installed-native-aot-invocation")
            # Verify .NET actually selects a native RID asset from a package, through the generated assembly-aware loader.
            native_package = work / "native-package"
            native_package.mkdir()
            binary = work / "external-native/Generated" / native_filename()
            native_project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
            props = ET.SubElement(native_project, "PropertyGroup")
            for name, value in {"TargetFramework": "net10.0", "PackageId": "BGCS.NativeAsset.Probe", "Version": args.version, "IncludeBuildOutput": "false", "SuppressDependenciesWhenPacking": "true"}.items():
                ET.SubElement(props, name).text = value
            ET.SubElement(ET.SubElement(native_project, "ItemGroup"), "None", Include=str(binary), Pack="true", PackagePath=f"runtimes/{rid}/native/{native_filename()}")
            ET.ElementTree(native_project).write(native_package / "NativeAsset.csproj", encoding="utf-8", xml_declaration=True)
            native_source = work / "native-source"
            run([args.dotnet, "pack", "NativeAsset.csproj", "--configuration", "Release", "--output", str(native_source), "-m:1", "-nr:false"], native_package, evidence, "native-rid-package-pack")
            ET.SubElement(sources, "add", key="local-native-fixture", value=str(native_source))
            ET.ElementTree(nuget).write(work / "NuGet.config", encoding="utf-8", xml_declaration=True)
            rid_consumer = work / "rid-consumer"
            managed = next((sdk / "Generated").glob("*/managed"))
            write_project(rid_consumer, args.version, ["BGCS.Runtime", "BGCS.NativeAsset.Probe"], '<ItemGroup><Compile Include="' + str(managed / "**/*.cs") + '" /></ItemGroup>')
            (rid_consumer / "Program.cs").write_text('''using System;
using Package.Probe;
using NativeApi api = NativeApi.Load();
if (api.Add(19, 23) != 42) throw new InvalidOperationException("NuGet RID asset selection failed.");
Console.WriteLine("NuGet RID asset selection and default generated loader passed.");
''', encoding="utf-8")
            run([args.dotnet, "build", "Consumer.csproj", "--configuration", "Release", "-m:1", "-nr:false"], rid_consumer, evidence, "native-rid-package-build")
            run([args.dotnet, str(rid_consumer / "bin/Release/net10.0/Consumer.dll")], rid_consumer, evidence, "native-rid-package-invocation")
            report["checks"].append("native-rid-package-selection-default-loader")
            if not args.sdk_only or args.host_tool:
                tool_root = work / "tool"
                run([args.dotnet, "tool", "install", "BindGen-CS", "--version", args.version, "--tool-path", str(tool_root), "--add-source", str(packages)], work, evidence, "tool-install")
                tool = next((file for file in (tool_root / "bindgen-cs", tool_root / "bindgen-cs.exe", tool_root / "bindgen-cs.cmd") if file.is_file()), None)
                if tool is None:
                    raise RuntimeError("The installed tool launcher is absent.")
                probe = work / "tool-probe"
                probe.mkdir()
                version_result = json.loads(run([str(tool), "version", "--json"], probe, evidence, "tool-version"))
                bare_doctor = json.loads(run([str(tool), "doctor", "--json"], probe, evidence, "tool-bare-doctor"))
                if (not version_result.get("parser", {}).get("assetDigestsVerified")
                        or version_result["parser"].get("parseExecuted") is not False
                        or not bare_doctor.get("success")
                        or not bare_doctor.get("worker", {}).get("assetDigestsVerified")
                        or bare_doctor["worker"].get("parseExecuted") is not False
                        or bare_doctor.get("inspectionExecuted") is not False
                        or bare_doctor.get("generationExecuted") is not False):
                    raise RuntimeError("The installed tool did not report verified worker assets separately from unexecuted parsing/building.")
                report["installedToolVersion"] = version_result
                report["checks"].append("installed-tool-verified-worker-identity-without-parsing")
                installed_advanced_worker.verify_cli(tool, advanced_worker, work, evidence, run)
                report["checks"].append("installed-advanced-clang-plugin-same-manifest-sdk-cli")
                report["installedCustomTarget"] = installed_custom_target.verify_cli(tool, custom_target, evidence, run)
                report["checks"].append(installed_custom_target.CHECK)
                shutil.copyfile(sdk / "native.h", probe / "native.h")
                shutil.copyfile(sdk / "native.c", probe / "native.c")
                run([str(tool), "init", "native.h", "--language", "c"], probe, evidence, "tool-init")
                tool_configuration_path = probe / "bindgen.json"
                tool_configuration = json.loads(tool_configuration_path.read_text(encoding="utf-8"))
                tool_configuration["api"].update({"namespace": "Package.ToolSource", "name": "NativeApi", "names": {"native_add": "Add"}})
                tool_configuration["extensions"] = {"plugins": [str(plugin_manifest_path)]}
                tool_configuration_path.write_text(json.dumps(tool_configuration, indent=2) + "\n", encoding="utf-8")
                for command in ("doctor", "validate", "inspect", "generate", "diff"):
                    run([str(tool), command, "bindgen.json"], probe, evidence, "tool-" + command)
                tool_publications = list((probe / "Generated").rglob(".bgcs-artifacts.json"))
                if len(tool_publications) != 1:
                    raise RuntimeError("The installed CLI source-plugin build did not publish one complete artifact collection.")
                tool_artifacts = json.loads(tool_publications[0].read_text(encoding="utf-8"))["artifacts"]
                if not any(item["relativePath"].endswith("/contributions/fixture.source/helpers/generated.h") for item in tool_artifacts):
                    raise RuntimeError("The installed CLI did not execute the same Contracts-only source plugin manifest.")
                tool_output = tool_publications[0].parent
                tool_managed = [tool_output / item["relativePath"] for item in tool_artifacts if item["mediaType"] == "text/x-csharp"]
                tool_native = [compile_shared(probe, tool_output, work / "external-native/tool" / native_filename(), [probe / "native.c"], run, evidence, exports=("contributed_add",))]
                tool_consumer = work / "tool-source-consumer"
                source_items = ET.Element("ItemGroup")
                for managed_source in tool_managed:
                    ET.SubElement(source_items, "Compile", Include=str(managed_source))
                ET.SubElement(source_items, "None", Include=str(tool_native[0]), Link=native_filename(), CopyToOutputDirectory="PreserveNewest")
                write_project(tool_consumer, args.version, ["BGCS.Runtime"], ET.tostring(source_items, encoding="unicode"))
                (tool_consumer / "Program.cs").write_text('''using System;
using System.IO;
using Package.ToolSource;
using NativeApi api = NativeApi.Load(Path.Combine(AppContext.BaseDirectory, "''' + native_filename() + '''"));
if (api.Add(19, 23) != 42 || api.ContributedAdd(29) != 41 || ContributedHelper.Marker != 41)
    throw new InvalidOperationException("The installed CLI source plugin did not reach actual managed/native invocation.");
Console.WriteLine("Installed CLI Contracts-only source contribution native invocation passed.");
''', encoding="utf-8")
                run([args.dotnet, "build", "Consumer.csproj", "--configuration", "Release", "-m:1", "-nr:false"], tool_consumer, evidence, "tool-source-plugin-consumer-build")
                run([args.dotnet, str(tool_consumer / "bin/Release/net10.0/Consumer.dll")], tool_consumer, evidence, "tool-source-plugin-native-invocation")
                report["checks"].append("installed-tool-all-unified-generation-commands")
                report["checks"].append("installed-contracts-only-source-plugin-sdk-cli-manifest")
                report["installedSourceContributor"] = installed_source_contributor.verify_cli(tool, args.dotnet,
                    args.version, work, evidence, source_contributor, run, write_project)
                report["checks"].append(installed_source_contributor.CHECK)
                from source_contribution_evidence import verify_report as verify_source_contributor_report
                verify_source_contributor_report(json.loads((evidence / "installed-source-contributor.json").read_text(encoding="utf-8")), report,
                    require_clean=not args.sdk_only)
                cpp_probe = work / "tool-cpp-probe"
                cpp_probe.mkdir()
                shutil.copyfile(sdk / "widget.hpp", cpp_probe / "widget.hpp")
                run([str(tool), "init", "widget.hpp", "--language", "cpp"], cpp_probe, evidence, "tool-cpp-init")
                cpp_config_path = cpp_probe / "bindgen.json"
                cpp_config = json.loads(cpp_config_path.read_text(encoding="utf-8"))
                cpp_config_path.write_text(json.dumps(cpp_config, indent=2) + "\n", encoding="utf-8")
                run([str(tool), "bridge", "bindgen.json"], cpp_probe, evidence, "tool-cpp-bridge")
                compile_shared(cpp_probe, cpp_probe / "Generated", work / "external-native/tool-cpp" / native_filename(), [], run, evidence)
                report["checks"].append("installed-tool-cpp-bridge-external-fixture-build")
                runtime_assembly = work / "GeneratedConsumer/bin/Release/net10.0/BGCS.Runtime.dll"
                if not runtime_assembly.is_file():
                    raise RuntimeError("The installed Runtime product assembly is absent from its Runtime-only consumer.")
                from browser_lifecycle_evidence import verify_execution_identity
                expected_execution = verify_execution_identity(cpp_acceptance_fixture.execution_identity(ROOT, tool, runtime_assembly))
                def feature_matrix(name, script, extra=()):
                    matrix_output = evidence / (name + "-" + uuid.uuid4().hex)
                    run([sys.executable, str(ROOT / "scripts" / script), "--dotnet", args.dotnet,
                         "--tool-path", str(tool), "--runtime-path", str(runtime_assembly),
                         "--output", str(matrix_output), *extra], work, evidence, name, timeout=1800)
                    receipts = list(matrix_output.glob("*/report.json"))
                    if len(receipts) != 1:
                        raise RuntimeError("The installed " + name + " gate did not preserve one exact invocation receipt.")
                    receipt = json.loads(receipts[0].read_text(encoding="utf-8"))
                    if (verify_execution_identity(receipt) != expected_execution
                            or receipt.get("sourceRevision") != source_at_start["revision"]
                            or receipt.get("sourceWorktreeDirty") != source_at_start["workingTreeDirty"]
                            or receipt.get("checkedOutSourceSha256") != source_at_start["checkedOutSourceSha256"]):
                        raise RuntimeError("The installed feature matrix used different generator/runtime bytes or source inputs.")
                    aggregates = script == "test-native-aot-bindings.py"
                    expected = cpp_acceptance_fixture.expected_checks(aggregates=aggregates)
                    actual = receipt.get("invocation", {}).get("checks", [])
                    if not receipt.get("success") or not receipt.get("installedProducts") or len(actual) != len(expected) or set(actual) != expected:
                        raise RuntimeError("The installed " + name + " gate did not complete its full product invocation matrix.")
                    if aggregates and receipt.get("aggregateNativeComparisonPassed") is not True:
                        raise RuntimeError("The installed NativeAOT aggregate results lack their independent native comparison.")
                    abi = receipt.get("generatedAbiManifests", [])
                    if (len(abi) != len(cpp_acceptance_fixture.MODES)
                            or {item["mode"] for item in abi} != set(cpp_acceptance_fixture.MODES)
                            or len({item["nativeTargetId"] for item in abi}) != 1
                            or any(item["nativePointerSize"] != (8 if aggregates else 4) for item in abi)
                            or not receipt.get("executionClosureSha256")):
                        raise RuntimeError("The installed feature matrix has no consistent resolved ABI and complete execution identity.")
                    matrix = {"name": name, "receipt": receipts[0].relative_to(evidence).as_posix(), "requiredChecks": len(expected),
                              "receiptSha256": hashlib.sha256(receipts[0].read_bytes()).hexdigest(), "executionIdentity": expected_execution}
                    if not aggregates:
                        from browser_lifecycle_evidence import verify_receipt
                        matrix["nonblockingLifecycle"] = verify_receipt(receipt)
                    report.setdefault("featureMatrices", []).append(matrix)
                    report["checks"].append(name)
                if args.native_feature_matrix:
                    feature_matrix("installed-native-aot-full-abi-cpp-and-lifetime-matrix", "test-native-aot-bindings.py")
                if args.wasm:
                    feature_matrix("installed-wasm-interpreter-full-import-matrix", "test-wasm-bindings.py")
                    feature_matrix("installed-wasm-aot-full-import-matrix", "test-wasm-bindings.py", ("--aot",))
                if verify_execution_identity(cpp_acceptance_fixture.execution_identity(ROOT, tool, runtime_assembly)) != expected_execution:
                    raise RuntimeError("The installed generator/runtime execution bytes changed during feature certification.")
            final_source = source_identity(ROOT)
            record_source_completion(report, source_at_start, final_source, require_clean=not args.sdk_only)
            report["success"] = True
    except BaseException as error:
        report["failure"] = str(error)
        raise
    finally:
        (evidence / "verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
