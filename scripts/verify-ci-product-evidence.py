#!/usr/bin/env python3
"""Read actual installed-product receipts from a trusted workflow artifact, never its default-branch run SHA."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import zipfile
import hashlib
from browser_lifecycle_evidence import verify_receipt, verify_summary, verify_execution_identity
from cpp_acceptance_fixture import expected_checks, MODES, AGGREGATE_CHECKS
from source_contribution_evidence import verify_summary as verify_source_summary, verify_report as verify_source_report

MATRICES = {"installed-native-aot-full-abi-cpp-and-lifetime-matrix": 113,
            "installed-wasm-interpreter-full-import-matrix": 74,
            "installed-wasm-aot-full-import-matrix": 74}
VS_CHECK = "installed-visual-studio2026-msbuild-exe-net-task-host-native-invocation-clean-rebuild"
ADVANCED_CHECKS = ("installed-advanced-clang-plugin-sdk-msbuild-and-runtime-compile",
                   "installed-advanced-clang-plugin-same-manifest-sdk-cli-msbuild")
COMPLETE_SOURCE_CHECK = 'installed-complete-runtime-source-primitives-without-bgcs-references'


def validate(receipt: dict, revision: str, rid: str, require_vs2026: bool = False) -> None:
    if not re.fullmatch(r"[0-9a-f]{40}(?:[0-9a-f]{24})?", revision):
        raise RuntimeError("The expected release source revision is invalid.")
    if (receipt.get("schemaVersion") != 1 or receipt.get("success") is not True
            or receipt.get("sourceRevision") != revision or receipt.get("hostRuntimeIdentifier") != rid
            or receipt.get("sourceWorktreeDirty") is not False or receipt.get("sourceBytesStable") is not True
            or receipt.get("completeProductSet") is not True):
        raise RuntimeError("Installed products were not certified from the exact clean complete release source.")
    if COMPLETE_SOURCE_CHECK not in receipt.get('checks', []):
        raise RuntimeError('The installed SDK did not compile and invoke its complete Runtime source without BGCS references.')
    content = receipt.get("checkedOutSourceSha256")
    if not isinstance(content, str) or not re.fullmatch(r"[0-9a-f]{64}", content):
        raise RuntimeError("The installed-product source content identity is missing.")
    after = receipt.get("sourceAtCompletion", {})
    package = receipt.get("packagingEvidence", {})
    if (after.get("sourceRevision") != revision or after.get("sourceWorktreeDirty") is not False
            or after.get("checkedOutSourceSha256") != content or package.get("sourceRevision") != revision
            or package.get("sourceWorktreeDirty") is not False or package.get("sourceRevisionStable") is not True
            or package.get("sourceBytesStable") is not True or package.get("checkedOutSourceSha256") != content
            or package.get("completeProductSet") is not True or package.get("physicalPackageDigestsVerified") is not True):
        raise RuntimeError("Installed-product and physical-package source identities are inconsistent.")
    version = receipt.get("version")
    if not isinstance(version, str) or not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?", version):
        raise RuntimeError("The installed product version is invalid.")
    libraries = ("BGCS.Contracts", "BGCS", "BGCS.Runtime")
    rids = ("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
    expected = {f"{name}.{version}.{extension}" for name in libraries for extension in ("nupkg", "snupkg")}
    expected |= {f"BindGen-CS.{rid}.{version}.nupkg" for rid in rids}
    expected |= {f"BindGen-CS.{version}.nupkg", f"BGCS.ParserRuntime.osx-x64.{version}.nupkg"}
    packages = package.get("packages", [])
    if (len(packages) != len(expected) or {item.get("file") for item in packages} != expected
            or any(not isinstance(item.get("bytes"), int) or not 0 < item["bytes"] < 250 * 1024 ** 2
                   or not isinstance(item.get("sha256"), str) or not re.fullmatch(r"[0-9a-f]{64}", item["sha256"]) for item in packages)):
        raise RuntimeError("The physical product/symbol set or digest evidence is incomplete.")
    matrices = receipt.get("featureMatrices", [])
    if (len(matrices) != len(MATRICES) or {item.get("name"): item.get("requiredChecks") for item in matrices} != MATRICES
            or not all(name in receipt.get("checks", []) for name in MATRICES)
            or "installed-tool-all-unified-generation-commands" not in receipt.get("checks", [])):
        raise RuntimeError("Installed NativeAOT and both browser execution matrices did not all finish.")
    execution = None
    for matrix in matrices:
        if not re.fullmatch(r"[0-9a-f]{64}", matrix.get("receiptSha256", "")):
            raise RuntimeError("An installed feature matrix is missing its exact invocation receipt hash.")
        identity = verify_execution_identity(matrix.get("executionIdentity"))
        if execution is not None and identity != execution:
            raise RuntimeError("Installed NativeAOT and browser matrices used different execution closures.")
        execution = identity
        if matrix["name"].startswith("installed-wasm-"):
            verify_summary(matrix.get("nonblockingLifecycle"))
    advanced = receipt.get("installedAdvancedWorkerPlugin", {})
    assets = advanced.get("pluginAssets", [])
    if (not all(name in receipt.get("checks", []) for name in ADVANCED_CHECKS)
            or advanced.get("parserIdentity") != "ClangSharp/20.1.2.4;LLVM/20"
            or not re.fullmatch(r"[0-9a-f]{64}", advanced.get("manifestSha256", ""))
            or len(assets) != 2 or {item.get("file") for item in assets} != {"ClangFactPlugin.dll", "ClangFactPlugin.deps.json"}
            or any(not re.fullmatch(r"[0-9a-f]{64}", item.get("sha256", "")) for item in assets)):
        raise RuntimeError("The installed advanced worker plugin was not verified through the same SDK/CLI/MSBuild manifest.")
    if require_vs2026 and (VS_CHECK not in receipt.get("checks", [])
            or not re.search(r"(?m)^\s*18\.\d+\.\d+(?:\.\d+)?\s*$", receipt.get("visualStudioMsbuildVersion", ""))):
        raise RuntimeError("The dedicated Visual Studio2026 TaskHost/native invocation receipt is missing.")
    verify_source_summary(receipt)


def verify_feature_report(report: dict, matrix: dict, parent: dict) -> None:
    aggregates = matrix["name"] == "installed-native-aot-full-abi-cpp-and-lifetime-matrix"
    invocation = report.get("invocation", {})
    actual = invocation.get("checks")
    expected = expected_checks(aggregates=aggregates)
    if (report.get("success") is not True or report.get("installedProducts") is not True
            or report.get("sourceRevision") != parent.get("sourceRevision")
            or report.get("sourceWorktreeDirty") is not False
            or report.get("checkedOutSourceSha256") != parent.get("checkedOutSourceSha256")
            or verify_execution_identity(report) != verify_execution_identity(matrix.get("executionIdentity"))
            or invocation.get("success") is not True or not isinstance(actual, list)
            or len(actual) != len(expected) or any(not isinstance(check, str) for check in actual)
            or set(actual) != expected or invocation.get("pointerSize") != (8 if aggregates else 4)):
        raise RuntimeError("The archived installed matrix report disagrees with its exact source, closure or invocation contract.")
    abi = report.get("generatedAbiManifests", [])
    native_targets = {"win-x64": "windows-x64-msvc", "linux-x64": "linux-x64-gnu", "osx-x64": "macos-x64-darwin"}
    target = native_targets.get(parent.get("hostRuntimeIdentifier")) if aggregates else "emscripten-wasm32-emscripten"
    if (not target or len(abi) != len(MODES) or {item.get("mode") for item in abi} != set(MODES)
            or any(item.get("nativeTargetId") != target or item.get("nativePointerSize") != (8 if aggregates else 4)
                or item.get("nativeByteOrder") != 1 or not re.fullmatch(r"[0-9a-f]{64}", item.get("abiManifestSha256", "")) for item in abi)):
        raise RuntimeError("The archived matrix did not retain every resolved import-mode ABI identity.")
    if aggregates:
        oracle = report.get("aggregateNativeOracle", {})
        signatures = invocation.get("aggregateSignatures", {})
        if (report.get("aggregateNativeComparisonPassed") is not True or oracle.get("success") is not True
                or oracle.get("checks") != len(AGGREGATE_CHECKS) or not oracle.get("signature")
                or not re.fullmatch(r"[0-9a-f]{64}", oracle.get("probeSha256", ""))
                or set(signatures) != set(MODES) or any(signatures[mode] != oracle["signature"] for mode in MODES)):
            raise RuntimeError("The archived NativeAOT matrix lacks the independent native aggregate comparison.")
    elif (verify_receipt(report) != verify_summary(matrix.get("nonblockingLifecycle"))
            or report.get("aot") is not ("-aot-" in matrix["name"])
            or not isinstance(report.get("browserVersion"), str) or not report["browserVersion"].strip()):
        raise RuntimeError("The archived browser lifecycle or deployment mode differs from the installed matrix.")


def read_archive(path: Path) -> dict:
    if path.stat().st_size > 64 * 1024 ** 2:
        raise RuntimeError("The product evidence archive exceeds the bounded preflight input size.")
    with zipfile.ZipFile(path) as archive:
        receipts = [item for item in archive.infolist() if item.filename == "verification.json"]
        if len(receipts) != 1 or receipts[0].file_size > 1024 ** 2:
            raise RuntimeError("The product artifact must contain one bounded exact verification.json receipt.")
        with archive.open(receipts[0]) as source:
            receipt = json.loads(source.read(1024 ** 2 + 1))
        names = set()
        for matrix in receipt.get("featureMatrices", []):
            name = matrix.get("receipt")
            if (not isinstance(name, str) or name in names or "\\" in name or ":" in name
                    or name.startswith("/") or any(part in ("", ".", "..") for part in name.split("/"))):
                raise RuntimeError("An installed matrix requires a unique portable report path inside its artifact.")
            names.add(name)
            entries = [item for item in archive.infolist() if item.filename == name]
            if len(entries) != 1 or entries[0].file_size > 1024 ** 2:
                raise RuntimeError("The installed product artifact lacks one exact bounded matrix report.")
            with archive.open(entries[0]) as source:
                payload = source.read(1024 ** 2 + 1)
            if hashlib.sha256(payload).hexdigest() != matrix.get("receiptSha256"):
                raise RuntimeError("An installed matrix report does not match its recorded hash.")
            verify_feature_report(json.loads(payload), matrix, receipt)
        source_summary = verify_source_summary(receipt)
        matches = [item for item in archive.infolist() if item.filename == source_summary['receipt']]
        if len(matches) != 1 or matches[0].file_size > 1024 ** 2:
            raise RuntimeError('The installed product artifact lacks one exact bounded source-contributor report.')
        payload = archive.read(matches[0])
        if hashlib.sha256(payload).hexdigest() != source_summary['receiptSha256']:
            raise RuntimeError('The archived source-contributor report does not match its actual digest.')
        verify_source_report(json.loads(payload), receipt)
        return receipt


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--revision", required=True)
    parser.add_argument("--host-rid", required=True)
    parser.add_argument("--require-vs2026", action="store_true")
    args = parser.parse_args()
    validate(read_archive(args.archive), args.revision, args.host_rid, args.require_vs2026)
    print("Verified actual clean installed products for " + args.revision + " on " + args.host_rid)


if __name__ == "__main__":
    main()
