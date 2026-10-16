#!/usr/bin/env python3
"""Lock parser resources independently of the host process and generated target."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import struct
import subprocess
import tempfile
import uuid

ROOT = Path(__file__).resolve().parents[1]


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read_lock(root: Path) -> dict:
    lock = json.loads((root / "distribution/manifests/native-parser-assets.lock.json").read_text(encoding="utf-8"))
    if lock.get("schemaVersion") != 1 or lock.get("llvmMajor") != 20:
        raise RuntimeError("The parser distribution requires its reviewed LLVM 20 resource lock.")
    headers = lock["builtinHeaders"]
    if digest(root / headers["path"]) != headers["sha256"]:
        raise RuntimeError("The builtin header bundle differs from the parser resource lock.")
    return lock


def verify_architecture(path: Path, rid: str) -> None:
    """Inspect portable binary headers; no foreign binary is loaded or executed."""
    with path.open("rb") as stream:
        header = stream.read(64)
        if rid.startswith("win-"):
            if len(header) < 64 or header[:2] != b"MZ":
                raise RuntimeError(f"The {rid} parser resource is not a PE binary: {path.name}")
            offset = struct.unpack_from("<I", header, 0x3C)[0]
            if offset < 64 or offset > 1024 * 1024:
                raise RuntimeError("The parser PE header offset is invalid.")
            stream.seek(offset)
            coff = stream.read(6)
            expected = 0xAA64 if rid.endswith("arm64") else 0x8664
            valid = len(coff) == 6 and coff[:4] == b"PE\0\0" and struct.unpack_from("<H", coff, 4)[0] == expected
        elif rid.startswith("linux-"):
            expected = 183 if rid.endswith("arm64") else 62
            valid = (len(header) >= 20 and header[:6] == b"\x7fELF\x02\x01"
                     and struct.unpack_from("<H", header, 18)[0] == expected)
        elif rid.startswith("osx-"):
            expected = 0x0100000C if rid.endswith("arm64") else 0x01000007
            valid = (len(header) >= 8 and header[:4] == b"\xcf\xfa\xed\xfe"
                     and struct.unpack_from("<I", header, 4)[0] == expected)
        else:
            raise RuntimeError(f"No reviewed parser binary architecture exists for {rid}.")
    if not valid:
        raise RuntimeError(f"The parser binary architecture does not match host {rid}: {path.name}")


def verify_upstream(root: Path, rid: str, assets: dict, files: dict[str, Path]) -> None:
    lock = read_lock(root)
    entry = lock["upstreamRuntimes"].get(rid)
    if entry is None:
        raise RuntimeError(f"No upstream parser resource lock exists for {rid}.")
    for package in entry["packages"]:
        identity = package["id"] + "/" + package["version"]
        keys = [key for key in assets["libraries"] if key.casefold() == identity.casefold()]
        if len(keys) != 1 or assets["libraries"][keys[0]].get("sha512") != package["sha512"]:
            raise RuntimeError(f"The restored parser package identity differs from its lock: {identity}")
    expected = {entry["name"]: entry for entry in entry["files"]}
    if files.keys() != expected.keys():
        raise RuntimeError(f"The {rid} parser file closure differs from its resource lock.")
    for name, source in files.items():
        if source.stat().st_size != expected[name]["bytes"] or digest(source) != expected[name]["sha256"]:
            raise RuntimeError(f"A parser resource differs from its reviewed upstream bytes: {rid}/{name}")
        verify_architecture(source, rid)


def git_head(directory: Path) -> str:
    return subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=directory, text=True).strip()


def verify_homebrew_info(path: Path, recipe: dict) -> None:
    info = json.loads(path.read_text(encoding="utf-8"))
    formulae = info.get("formulae", [])
    if len(formulae) != 1:
        raise RuntimeError("The Intel parser requires one captured Homebrew LLVM formula.")
    formula = formulae[0]
    stable = formula.get("urls", {}).get("stable", {})
    if (formula.get("name") != recipe["llvm"]["homebrewFormula"]
            or formula.get("versions", {}).get("stable") != recipe["llvm"]["version"]
            or stable.get("url") != recipe["llvm"]["sourceArchiveUrl"]
            or stable.get("checksum") != recipe["llvm"]["sourceArchiveSha256"]
            or not any(entry.get("version") == recipe["llvm"]["version"]
                       or entry.get("version", "").startswith(recipe["llvm"]["version"] + "_")
                       for entry in formula.get("installed", []))):
        raise RuntimeError("The installed LLVM formula differs from the pinned version/source archive.")


def supplemental_files(directory: Path) -> dict[str, Path]:
    paths = list(directory.rglob("*"))
    if any(path.is_symlink() or not path.resolve().is_relative_to(directory.resolve()) for path in paths):
        raise RuntimeError("An Intel parser provenance file escapes its resource closure.")
    return {path.relative_to(directory).as_posix(): path for path in paths if path.is_file()
            and path.relative_to(directory).as_posix() not in ("runtime-provenance.json", "SHA256SUMS")}


def verify_supplemental(root: Path, directory: Path, expected_revision: str) -> dict:
    lock = read_lock(root)
    path = directory / "runtime-provenance.json"
    if not path.is_file() or path.is_symlink() or path.stat().st_size > 16 * 1024 * 1024:
        raise RuntimeError("The Intel parser requires its bounded, actual build provenance manifest.")
    manifest = json.loads(path.read_text(encoding="utf-8"))
    recipe = lock["supplementalRuntime"]
    if (manifest.get("schemaVersion") != 1 or manifest.get("hostRid") != "osx-x64"
            or manifest.get("parserIdentity") != lock["parserIdentity"]
            or manifest.get("clangSharpCommit") != recipe["clangSharp"]["commit"]
            or manifest.get("llvmVersion") != recipe["llvm"]["version"]
            or manifest.get("llvmSourceArchiveSha256") != recipe["llvm"]["sourceArchiveSha256"]
            or manifest.get("bgcsSourceRevision") != expected_revision):
        raise RuntimeError("The Intel parser provenance does not match the pinned source recipe and current BGCS revision.")
    actual = supplemental_files(directory)
    entries = manifest.get("artifacts", [])
    if not entries or len({entry["path"] for entry in entries}) != len(entries) or {entry["path"] for entry in entries} != actual.keys():
        raise RuntimeError("The Intel parser provenance does not describe its complete file closure.")
    for entry in entries:
        relative = PurePosixPath(entry["path"])
        if relative.is_absolute() or ".." in relative.parts or "\\" in entry["path"]:
            raise RuntimeError("An Intel parser provenance path escapes its resource closure.")
        source = actual[entry["path"]]
        if source.is_symlink() or not source.resolve().is_relative_to(directory.resolve()):
            raise RuntimeError("An Intel parser provenance file escapes its resource closure.")
        if source.stat().st_size != entry["bytes"] or digest(source) != entry["sha256"]:
            raise RuntimeError("An Intel parser resource changed after its actual build: " + entry["path"])
        if source.suffix == ".dylib":
            verify_architecture(source, "osx-x64")
    for name in ("libclang.dylib", "libClangSharp.dylib", "licenses/LLVM-LICENSE.txt", "licenses/ClangSharp-LICENSE.md",
                 "licenses/ClangSharp-NOTICE.md", "build-inputs/homebrew-llvm.json"):
        if name not in actual:
            raise RuntimeError("The Intel parser provenance omits a required binary, source input or license: " + name)
    verify_homebrew_info(actual["build-inputs/homebrew-llvm.json"], recipe)
    verify_native_components(directory, manifest.get("components"), actual)
    return manifest


def stage_worker(root: Path, directory: Path, worker_output: Path, expected_revision: str) -> dict:
    """Install only the explicitly verified Intel resource closure into its worker build output."""
    directory = directory.resolve()
    worker_output = worker_output.resolve()
    if not (worker_output / "BGCS.Frontend.Clang.Worker.dll").is_file():
        raise RuntimeError("Intel parser staging requires the completed worker output assembly.")
    if directory.is_relative_to(worker_output) or worker_output.is_relative_to(directory):
        raise RuntimeError("The Intel parser source and worker output must have separate owned roots.")
    manifest = verify_supplemental(root, directory, expected_revision)
    destination = worker_output / "runtimes/osx-x64/native"
    if destination.is_symlink() or destination.resolve() != destination:
        raise RuntimeError("The owned worker parser destination cannot traverse a linked path.")
    if destination.exists() and any(path.is_symlink() for path in destination.rglob("*")):
        raise RuntimeError("The owned worker parser destination contains an unexpected symbolic link.")
    destination.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=".bgcs-parser-stage-", dir=destination.parent))
    previous = destination.with_name(".bgcs-parser-previous-" + uuid.uuid4().hex)
    try:
        for relative in [entry["path"] for entry in manifest["artifacts"]] + ["runtime-provenance.json"]:
            target = staging / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(directory / relative, target)
        verify_supplemental(root, staging, expected_revision)
        verify_supplemental(root, directory, expected_revision)
        if destination.exists():
            destination.rename(previous)
        try:
            staging.rename(destination)
        except BaseException:
            if previous.exists():
                previous.rename(destination)
            raise
        if previous.exists():
            shutil.rmtree(previous)
        return manifest
    finally:
        if staging.exists():
            shutil.rmtree(staging)


def verify_native_components(directory: Path, components, actual: dict[str, Path]) -> None:
    if not isinstance(components, list) or not components:
        raise RuntimeError("The Intel parser lacks actual native component dependency/license provenance.")
    identities, files = set(), set()
    for component in components:
        identity = component.get("id")
        if not identity or identity in identities or not component.get("version"):
            raise RuntimeError("Native component identities are missing or duplicated.")
        identities.add(identity)
        source = component.get("source", {})
        checksum = source.get("sha256") or source.get("gitCommit")
        if not source.get("uri") or not checksum or len(checksum) not in (40, 64) or any(character not in "0123456789abcdef" for character in checksum):
            raise RuntimeError("A native component lacks its exact original source digest.")
        if not component.get("licenses") or not component.get("files"):
            raise RuntimeError("Native components require actual delivered license and binary files.")
        for entry in [*component["licenses"], *component["files"]]:
            if entry.get("path") not in actual or digest(actual[entry["path"]]) != entry.get("sha256"):
                raise RuntimeError("Native component provenance contradicts actual delivered file bytes.")
        for entry in component["files"]:
            if Path(entry["path"]).name != entry["path"] or not entry["path"].endswith(".dylib") or entry["path"] in files:
                raise RuntimeError("Native component binary ownership is ambiguous.")
            files.add(entry["path"])
    if files != {path for path in actual if path.endswith(".dylib")}:
        raise RuntimeError("A redistributed native dependency lacks component/license provenance.")
    for component in components:
        if not isinstance(component.get("dependencies"), list) or any(dependency not in identities for dependency in component["dependencies"]):
            raise RuntimeError("The actual native component dependency graph is incomplete.")


def native_components(directory: Path, clang_source: Path, llvm_prefix: Path, origins_path: Path, recipe: dict) -> list[dict]:
    """Capture actual relocated link edges and original component/license identities."""
    if origins_path.resolve() != (directory / "build-inputs/native-source-origins.tsv").resolve():
        raise RuntimeError("Native component origins must be captured inside the delivered provenance closure.")
    origins = {}
    for line in origins_path.read_text(encoding="utf-8").splitlines():
        name, source = line.split("\t", 1)
        if name in origins or Path(name).name != name or not Path(source).is_file():
            raise RuntimeError("Native component source origins are ambiguous or missing.")
        origins[name] = Path(source).resolve()
    if set(origins) != {path.name for path in directory.glob("*.dylib")}:
        raise RuntimeError("Native source origins do not identify every actual redistributed dylib.")
    cellar = Path(subprocess.check_output(["brew", "--cellar"], text=True).strip()).resolve()
    components, owners = {}, {}
    for name, source in sorted(origins.items()):
        if name == "libClangSharp.dylib":
            key, version = "ClangSharp", "20.1.2.4"
            inputs = {"uri": "git+https://github.com/dotnet/ClangSharp.git", "gitCommit": recipe["clangSharp"]["commit"]}
            license_paths = ["licenses/ClangSharp-LICENSE.md", "licenses/ClangSharp-NOTICE.md"]
        elif source.is_relative_to(llvm_prefix.resolve()):
            key, version = "LLVM", recipe["llvm"]["version"]
            inputs = {"uri": recipe["llvm"]["sourceArchiveUrl"], "sha256": recipe["llvm"]["sourceArchiveSha256"]}
            license_paths = ["licenses/LLVM-LICENSE.txt"]
        else:
            if not source.is_relative_to(cellar) or len(source.relative_to(cellar).parts) < 3:
                raise RuntimeError("A native redistributed dependency has no captured installed component identity.")
            formula, version, *_ = source.relative_to(cellar).parts
            captured = subprocess.check_output(["brew", "info", "--json=v2", formula], text=True)
            info = json.loads(captured)
            records = info.get("formulae", [])
            if len(records) != 1 or not any(item.get("version") == version for item in records[0].get("installed", [])):
                raise RuntimeError("Homebrew did not verify the actual redistributed dependency keg version.")
            stable = records[0].get("urls", {}).get("stable", {})
            if records[0].get("versions", {}).get("stable") != version.split("_", 1)[0] or not stable.get("checksum") or not stable.get("url"):
                raise RuntimeError("The redistributed dependency's original source recipe is unavailable or has drifted.")
            key = "Homebrew:" + formula
            inputs = {"uri": stable["url"], "sha256": stable["checksum"]}
            license_paths = [path.relative_to(directory).as_posix() for path in sorted((directory / "licenses").glob(formula + "-*")) if path.is_file()]
            (directory / "build-inputs" / ("homebrew-" + formula + ".json")).write_text(captured, encoding="utf-8")
        if not license_paths or any(not (directory / path).is_file() for path in license_paths):
            raise RuntimeError("A redistributed native component has no actual delivered license text: " + key)
        component = components.setdefault(key, {"id": key, "version": version, "source": inputs,
            "licenses": [{"path": path, "sha256": digest(directory / path)} for path in license_paths], "files": [], "dependencies": []})
        if component["version"] != version or component["source"] != inputs:
            raise RuntimeError("Native dependencies contradict a captured component identity.")
        component["files"].append({"path": name, "sha256": digest(directory / name)})
        owners[name] = key
    for name, owner in owners.items():
        lines = subprocess.check_output(["otool", "-L", str(directory / name)], text=True).splitlines()[2:]
        for line in lines:
            dependency = line.strip().split(" ", 1)[0]
            if dependency.startswith(("/usr/lib/", "/System/Library/")):
                continue
            if not dependency.startswith("@loader_path/") or dependency.removeprefix("@loader_path/") not in owners:
                raise RuntimeError("A captured native link dependency is not in its relocated delivered closure.")
            target = owners[dependency.removeprefix("@loader_path/")]
            if target != owner and target not in components[owner]["dependencies"]:
                components[owner]["dependencies"].append(target)
    for component in components.values():
        component["dependencies"].sort()
    return [components[key] for key in sorted(components)]


def create(root: Path, directory: Path, clang_source: Path, llvm_version: str, homebrew_info: Path,
           llvm_prefix: Path, native_origins: Path) -> dict:
    lock = read_lock(root)
    recipe = lock["supplementalRuntime"]
    revision = git_head(clang_source)
    if revision != recipe["clangSharp"]["commit"] or llvm_version != recipe["llvm"]["version"]:
        raise RuntimeError("The actual ClangSharp checkout and LLVM version must match the reviewed Intel source recipe.")
    if subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=no"], cwd=clang_source, text=True).strip():
        raise RuntimeError("The pinned ClangSharp source checkout contains modified tracked files.")
    if homebrew_info.resolve() != (directory / "build-inputs/homebrew-llvm.json").resolve():
        raise RuntimeError("The actual Homebrew recipe must be captured inside the delivered Intel provenance closure.")
    verify_homebrew_info(homebrew_info, recipe)
    components = native_components(directory, clang_source, llvm_prefix, native_origins, recipe)
    manifest = {"schemaVersion": 1, "hostRid": "osx-x64", "parserIdentity": lock["parserIdentity"],
        "bgcsSourceRevision": git_head(root), "clangSharpCommit": revision, "llvmVersion": llvm_version,
        "llvmSourceArchiveSha256": recipe["llvm"]["sourceArchiveSha256"],
        "components": components,
        "scope": "actual-relocated-and-ad-hoc-signed-build-bytes; invocation-certification-is-a-separate-required-gate",
        "artifacts": [{"path": relative, "bytes": source.stat().st_size, "sha256": digest(source)}
            for relative, source in sorted(supplemental_files(directory).items())]}
    (directory / "runtime-provenance.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    verify_supplemental(root, directory, manifest["bgcsSourceRevision"])
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("create", "verify", "stage-worker"))
    parser.add_argument("directory", type=Path)
    parser.add_argument("--clang-source", type=Path)
    parser.add_argument("--llvm-version")
    parser.add_argument("--homebrew-info", type=Path)
    parser.add_argument("--llvm-prefix", type=Path)
    parser.add_argument("--native-origins", type=Path)
    parser.add_argument("--worker-output", type=Path)
    args = parser.parse_args()
    if args.mode == "create":
        if not args.clang_source or not args.llvm_version or not args.homebrew_info or not args.llvm_prefix or not args.native_origins:
            parser.error("Creating actual Intel provenance requires its source checkout, LLVM version and captured Homebrew inputs.")
        manifest = create(ROOT, args.directory.resolve(), args.clang_source.resolve(), args.llvm_version, args.homebrew_info.resolve(),
                          args.llvm_prefix.resolve(), args.native_origins.resolve())
    elif args.mode == "stage-worker":
        if args.worker_output is None:
            parser.error("Intel worker staging requires its explicit completed output root.")
        manifest = stage_worker(ROOT, args.directory, args.worker_output, git_head(ROOT))
    else:
        manifest = verify_supplemental(ROOT, args.directory.resolve(), git_head(ROOT))
    print("Verified actual Intel parser provenance and " + str(len(manifest["artifacts"])) + " file digests.")


if __name__ == "__main__":
    main()
