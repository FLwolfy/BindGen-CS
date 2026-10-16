#!/usr/bin/env python3
"""Verify the exact SDK/runtime and the tested immutable WebAssembly manifests."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def host_rid() -> str:
    machine = platform.machine().lower()
    if machine not in ("amd64", "x86_64", "arm64", "aarch64"):
        raise RuntimeError("The locked toolchain has no recorded host CPU.")
    return ("win" if os.name == "nt" else "osx" if platform.system() == "Darwin" else "linux") + ("-arm64" if machine in ("arm64", "aarch64") else "-x64")


def verify_workloads(dotnet_root: Path, lock: dict, rid: str) -> dict:
    workloads, packs, inputs = {}, {}, []
    for entry in lock["manifests"]:
        directory = dotnet_root / "sdk-manifests" / entry["manifestFeatureBand"] / entry["id"] / entry["version"]
        for name, expected in entry["files"].items():
            source = directory / name
            if not source.is_file() or hashlib.sha256(source.read_bytes()).hexdigest() != expected:
                raise RuntimeError("The installed workload manifest differs from the tested lock: " + str(source))
            inputs.append({"path": source.relative_to(dotnet_root).as_posix(), "sha256": expected})
        manifest = json.loads((directory / "WorkloadManifest.json").read_text(encoding="utf-8"))
        if manifest.get("version") != entry["version"]:
            raise RuntimeError("A locked workload manifest has a different internal version.")
        workloads.update(manifest["workloads"])
        packs.update(manifest["packs"])
    seen, required, pending = set(), set(), [lock["requiredWorkload"]]
    while pending:
        identity = pending.pop()
        if identity in seen:
            continue
        seen.add(identity)
        definition = workloads[identity]
        if "platforms" in definition and rid not in definition["platforms"]:
            raise RuntimeError("The requested workload is unavailable for the actual generator host.")
        required.update(definition.get("packs", []))
        pending.extend(definition.get("extends", []))
    resolved = []
    for identity in sorted(required):
        pack = packs[identity]
        aliases = pack.get("alias-to")
        actual = aliases.get(rid, aliases.get("any")) if aliases else identity
        if actual is None:
            continue  # The manifest explicitly uses host-provided tools here.
        version = pack["version"]
        source = (dotnet_root / "library-packs" / (actual.lower() + "." + version + ".nupkg")
                  if pack["kind"].lower() == "library" else dotnet_root / "packs" / actual / version)
        if version != lock["runtimePackVersion"] or not source.exists():
            raise RuntimeError("A required tested workload pack/version is absent: " + str(actual) + "/" + version)
        resolved.append({"id": actual, "version": version})
    return {"manifestInputs": inputs, "resolvedRequiredPacks": resolved}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=os.environ.get("DOTNET_HOST_PATH", "dotnet"))
    parser.add_argument("--workloads", action="store_true", help="Verify installed packs without modifying the SDK.")
    parser.add_argument("--install-workloads", action="store_true", help="Explicit CI bootstrap: install the locked SDK's wasm-tools without updating manifests.")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    tool = Path(args.dotnet) if Path(args.dotnet).is_file() else Path(shutil.which(args.dotnet) or "")
    if not tool.is_file():
        raise RuntimeError("The selected dotnet host is unavailable.")
    tool = tool.resolve()
    lock = json.loads((ROOT / "eng/toolchains/toolchains.lock.json").read_text(encoding="utf-8"))
    environment = {key: value for key, value in os.environ.items() if key.casefold() != "platform"}
    environment.update(DOTNET_ROOT=str(tool.parent), DOTNET_HOST_PATH=str(tool), DOTNET_CLI_UI_LANGUAGE="en")
    version = subprocess.check_output([str(tool), "--version"], cwd=ROOT, env=environment, text=True).strip()
    runtimes = subprocess.check_output([str(tool), "--list-runtimes"], cwd=ROOT, env=environment, text=True)
    net10 = {line.split()[1] for line in runtimes.splitlines() if line.startswith("Microsoft.NETCore.App 10.")}
    if version != lock["sdkVersion"] or net10 != {lock["managedRuntimeVersion"]}:
        raise RuntimeError("Use the isolated locked SDK/runtime; another net10 runtime could silently roll execution forward.")
    report = {"schemaVersion": 1, "success": True, "hostRid": host_rid(), "sdkVersion": version,
              "runtimeVersion": lock["managedRuntimeVersion"], "dotnet": str(tool)}
    if args.workloads or args.install_workloads:
        workload_lock = json.loads((ROOT / "eng/toolchains/workloads.lock.json").read_text(encoding="utf-8"))
        if workload_lock["sdkVersion"] != version:
            raise RuntimeError("The workload lock and selected SDK disagree.")
        if args.install_workloads:
            subprocess.run([str(tool), "workload", "install", workload_lock["requiredWorkload"],
                            "--skip-manifest-update", "--disable-parallel"], cwd=ROOT, env=environment, check=True)
        report["workload"] = verify_workloads(tool.parent, workload_lock, report["hostRid"])
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("Verified locked SDK " + version + ", runtime " + lock["managedRuntimeVersion"] + (" and actual wasm-tools packs." if "workload" in report else "."))


if __name__ == "__main__":
    main()
