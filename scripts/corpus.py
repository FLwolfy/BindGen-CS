#!/usr/bin/env python3
"""Restore and verify exact independent corpus commits and checked-out content."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_LOCK = ROOT / "tests/corpus/corpus.lock.json"


def git_environment() -> dict[str, str]:
    environment = dict(os.environ)
    # Git does not read the Windows Internet Settings proxy that the user already
    # enabled. Forward it only to owned Git children; explicit environment wins.
    if os.name == "nt":
        for scheme, value in urllib.request.getproxies().items():
            if scheme in ("http", "https") and not any(key.casefold() == scheme + "_proxy" for key in environment):
                environment[scheme.upper() + "_PROXY"] = value
    return environment


def git(path: Path, *arguments: str) -> str:
    return subprocess.check_output(["git", "-C", str(path), *arguments], text=True, encoding="utf-8", errors="strict", timeout=300, env=git_environment()).strip()


def content_identity(path: Path) -> dict:
    records = subprocess.check_output(["git", "-C", str(path), "ls-files", "--stage", "-z"], timeout=300, env=git_environment()).split(b"\0")
    aggregate = hashlib.sha256()
    count, total, gitlinks = 0, 0, []
    for record in sorted(record for record in records if record):
        metadata, relative_bytes = record.split(b"\t", 1)
        mode, object_id, stage = metadata.decode("ascii").split()
        if stage != "0":
            raise RuntimeError("The corpus index contains unresolved merge entries: " + str(path))
        relative = relative_bytes.decode("utf-8")
        file = path / relative
        if mode == "160000":
            payload = object_id.encode("ascii")
            gitlinks.append({"path": relative, "revision": object_id,
                             "materialized": file.is_dir() and any(file.iterdir())})
        elif not file.exists() and not file.is_symlink():
            continue  # A declared sparse checkout intentionally does not materialize this tracked path.
        elif mode == "120000" and file.is_symlink():
            payload = os.readlink(file).encode("utf-8")
        else:
            if not file.is_file():
                raise RuntimeError("A tracked corpus input is not a regular file: " + str(file))
            payload = file.read_bytes()
        aggregate.update(relative_bytes + b"\0" + mode.encode("ascii") + b"\0"
                         + hashlib.sha256(payload).hexdigest().encode("ascii") + b"\n")
        count += 1
        total += len(payload)
    return {"checkedOutContentSha256": aggregate.hexdigest(), "materializedEntries": count,
            "materializedBytes": total, "submodules": gitlinks}


def load_lock(path: Path) -> dict:
    lock = json.loads(path.read_text(encoding="utf-8-sig"))
    expected = {"miniaudio", "SDL", "cimgui", "cimguizmo", "bgfx", "bimg", "bx"}
    if lock.get("kind", "headers") == "native":
        expected |= {"cimgui/imgui", "cimguizmo/ImGuizmo"}
    elif lock.get("kind", "headers") != "headers":
        raise RuntimeError("The corpus lock kind must identify header or native-source verification.")
    if lock.get("schemaVersion") != 1:
        raise RuntimeError("The corpus lock revision is unsupported.")
    names = [item["id"] for item in lock["repositories"]]
    if len(set(names)) != len(names) or set(names) != expected:
        raise RuntimeError("The corpus lock must identify all six products and their exact declared dependencies.")
    for item in lock["repositories"]:
        if not re.fullmatch(r"[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+)?", item["id"]) or not re.fullmatch(r"[a-f0-9]{40}", item["revision"]):
            raise RuntimeError("A corpus repository identity/revision is invalid.")
        if not re.fullmatch(r"[a-f0-9]{64}", item["checkedOutContentSha256"]):
            raise RuntimeError("A corpus checkout requires a complete SHA-256 content lock.")
        if not item["repository"].startswith("https://github.com/") or not item["repository"].endswith(".git"):
            raise RuntimeError("Corpus fetches require explicit locked HTTPS GitHub repositories.")
        for relative, digest in item["entryHeaders"].items():
            if Path(relative).is_absolute() or ".." in Path(relative).parts or not re.fullmatch(r"[a-f0-9]{64}", digest):
                raise RuntimeError("A corpus header requires a safe repository-relative path and SHA-256 content lock.")
    return lock


def verify_repository(root: Path, item: dict) -> dict:
    path = root / item["id"]
    if not path.is_dir() or Path(git(path, "rev-parse", "--show-toplevel")).resolve() != path.resolve():
        raise RuntimeError("A locked corpus repository is missing: " + str(path))
    if git(path, "rev-parse", "HEAD") != item["revision"] or git(path, "remote", "get-url", "origin") != item["repository"]:
        raise RuntimeError("The corpus checkout has the wrong revision or origin: " + str(path))
    if git(path, "status", "--porcelain", "--untracked-files=all", "--ignored=matching"):
        raise RuntimeError("The locked corpus checkout contains modified, untracked or ignored input: " + str(path))
    identity = content_identity(path)
    if identity["checkedOutContentSha256"] != item["checkedOutContentSha256"]:
        raise RuntimeError("Corpus checked-out bytes differ from the locked content digest: " + str(path)
                           + ". Restore a clean dedicated checkout with core.autocrlf=false; existing files are not overwritten.")
    for relative, expected in item["entryHeaders"].items():
        header = path / relative
        if not header.resolve().is_relative_to(path.resolve()) or not header.is_file() or hashlib.sha256(header.read_bytes()).hexdigest() != expected:
            raise RuntimeError("A locked corpus entry header changed: " + str(header))
    return {"id": item["id"], "revision": item["revision"], "repository": item["repository"],
            "workingTreeClean": True, **identity}


def verify(root: Path, lock: dict) -> list[dict]:
    return [verify_repository(root, item) for item in lock["repositories"]]


def restore(root: Path, lock: dict) -> list[dict]:
    root = root.resolve()
    if root in (Path(root.anchor), ROOT, Path.home().resolve()) or ROOT.is_relative_to(root):
        raise RuntimeError("Corpus restoration requires a dedicated directory rather than a source/home/system root.")
    root.mkdir(parents=True, exist_ok=True)
    for item in lock["repositories"]:
        destination = root / item["id"]
        if not destination.resolve().is_relative_to(root):
            raise RuntimeError("A corpus destination escaped its dedicated root.")
        # A parent repository materializes an empty directory for an uninitialized
        # gitlink. Only these two exact locked gitlinks may replace that placeholder.
        if item["id"] in ("cimgui/imgui", "cimguizmo/ImGuizmo") and destination.is_dir() and not any(destination.iterdir()):
            destination.rmdir()
        if destination.exists():
            verify_repository(root, item)
            continue
        destination.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="." + item["id"].replace("/", "-") + "-", dir=root) as temporary:
            staging = Path(temporary)
            if not staging.resolve().is_relative_to(root):
                raise RuntimeError("Corpus staging escaped its dedicated root.")
            subprocess.run(["git", "init", "--quiet", str(staging)], check=True, timeout=60, env=git_environment())
            git(staging, "config", "core.autocrlf", "false")
            git(staging, "config", "core.eol", "lf")
            git(staging, "remote", "add", "origin", item["repository"])
            subprocess.run(["git", "-C", str(staging), "-c", "protocol.version=2", "fetch", "--quiet", "--depth", "1", "--filter=blob:none", "origin", item["revision"]], check=True, timeout=900, env=git_environment())
            if item["sparseDirectories"]:
                git(staging, "sparse-checkout", "set", "--cone", *item["sparseDirectories"])
            git(staging, "-c", "advice.detachedHead=false", "checkout", "--quiet", "--detach", "FETCH_HEAD")
            # Validate before moving a complete new checkout into the caller's corpus.
            if git(staging, "rev-parse", "HEAD") != item["revision"] or content_identity(staging)["checkedOutContentSha256"] != item["checkedOutContentSha256"]:
                raise RuntimeError("Fetched corpus bytes do not match the lock: " + item["id"])
            os.replace(staging, destination)
    return verify(root, lock)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("verify", "restore"))
    parser.add_argument("--root", type=Path, default=Path(os.environ.get("BGCS_REAL_LIBRARY_ROOT", ROOT / "artifacts/corpus/headers")))
    parser.add_argument("--lock", type=Path, default=DEFAULT_LOCK)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    lock = load_lock(args.lock)
    rows = restore(args.root, lock) if args.command == "restore" else verify(args.root.resolve(), lock)
    result = {"success": True, "lockSha256": hashlib.sha256(args.lock.read_bytes()).hexdigest(), "repositories": rows}
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
