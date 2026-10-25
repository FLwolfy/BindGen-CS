#!/usr/bin/env python3
"""Verify actual BGCS publication and executed-test evidence without trusting sentinels."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import time
import xml.etree.ElementTree as ET


def digest(path: Path) -> str:
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def source_identity(root: Path) -> dict:
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=all"], cwd=root, text=True).strip())
    names = subprocess.check_output(["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"], cwd=root).split(b"\0")
    content = hashlib.sha256()
    for relative in sorted(set(name for name in names if name)):
        path = root / relative.decode("utf-8")
        if path.is_symlink():
            payload = str(path.readlink()).encode("utf-8")
            identity = hashlib.sha256(payload).hexdigest()
        elif path.is_file():
            identity = digest(path)
        elif not path.exists():
            identity = "missing"
        else:
            raise RuntimeError("A source fingerprint entry is not a regular file: " + str(path))
        content.update(relative + b"\0" + identity.encode("ascii") + b"\n")
    return {"revision": revision, "workingTreeDirty": dirty, "checkedOutSourceSha256": content.hexdigest()}


def package_version(root: Path, *, declared: bool = False) -> str:
    """Select the formal version or one deterministic private commit/content candidate."""
    version = ET.parse(root / "Directory.Build.props").findtext("./PropertyGroup/Version")
    if not version or not re.fullmatch(r"4\.1\.2(?:-ci(?:\.[0-9A-Za-z-]+)*)?", version):
        raise RuntimeError("The current tree must declare 4.1.2 or its private -ci candidate.")
    if declared and version == "4.1.2":
        return version
    source = source_identity(root)
    # Prefix the hash segments so NuGet cannot normalize an all-numeric hash's
    # leading zeroes. Both exact full identities remain in the package receipt.
    return f"4.1.2-ci.g{source['revision'][:12]}.s{source['checkedOutSourceSha256'][:16]}"


def discover_test_projects(root: Path) -> list[Path]:
    """Discover every retained test project, including nested extension projects."""
    projects = []
    for project in sorted((root / "tests").rglob("*.csproj"), key=lambda path: path.relative_to(root).as_posix()):
        if any(part in {"bin", "obj"} for part in project.relative_to(root / "tests").parts):
            continue
        document = ET.parse(project)
        if any(item.get("Include") == "Microsoft.NET.Test.Sdk" for item in document.findall(".//PackageReference")):
            projects.append(project)
    return projects


def test_receipt(path: Path, match: str | None = None) -> dict:
    document = ET.parse(path)
    definitions = {}
    for definition in document.findall(".//{*}TestDefinitions/{*}UnitTest"):
        method = definition.find("{*}TestMethod")
        definitions[definition.get("id")] = (method.get("className", "") + "." + method.get("name", "")) if method is not None else definition.get("name", "")
    expression = re.compile(match) if match else None
    results = []
    for result in document.findall(".//{*}Results/{*}UnitTestResult"):
        name = definitions.get(result.get("testId"), result.get("testName", ""))
        if expression is None or expression.search(name):
            results.append({"name": name, "displayName": result.get("testName", ""),
                            "outcome": result.get("outcome"), "executionId": result.get("executionId")})
    passed = sum(result["outcome"] == "Passed" for result in results)
    failed = [result for result in results if result["outcome"] not in ("Passed", "NotExecuted")]
    if not passed or failed or any(result["outcome"] != "Passed" for result in results):
        raise RuntimeError(f"Required executed tests did not pass: {path}: match={match!r}, passed={passed}, failed={len(failed)}")
    return {"trx": str(path.resolve()), "sha256": digest(path), "match": match, "passed": passed,
            "notExecuted": sum(result["outcome"] == "NotExecuted" for result in results), "results": results}


def publication(root: Path, expected_library: str | None = None) -> dict:
    root = root.resolve()
    marker = root / ".bgcs-artifacts.json"
    manifest = json.loads(marker.read_text(encoding="utf-8-sig"))
    if manifest.get("schemaVersion") != 1:
        raise RuntimeError("The publication ownership revision is unsupported: " + str(root))
    if expected_library is not None and manifest.get("libraryId") != expected_library:
        raise RuntimeError("The publication belongs to a different logical library: " + str(root))
    artifacts = manifest.get("artifacts", [])
    names = [item["relativePath"] for item in artifacts]
    if not artifacts or len(set(names)) != len(names):
        raise RuntimeError("The owned artifact collection is empty or ambiguous: " + str(root))
    for item in artifacts:
        relative = Path(item["relativePath"])
        path = root / relative
        if relative.is_absolute() or not path.resolve().is_relative_to(root) or path.is_symlink():
            raise RuntimeError("An artifact escapes the verified publication root: " + str(relative))
        if not path.is_file() or digest(path) != item["digest"]["value"]:
            raise RuntimeError("A published artifact is missing or modified: " + str(path))
    actual = {path.relative_to(root).as_posix() for path in root.rglob("*") if path.is_file()}
    if actual != set(names) | {marker.name}:
        raise RuntimeError("The output directory differs from its complete owned manifest: " + str(root))
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    tests = commands.add_parser("tests")
    tests.add_argument("trx", type=Path)
    tests.add_argument("--match")
    tests.add_argument("--output", type=Path, required=True)
    discover = commands.add_parser("discover-tests")
    discover.add_argument("root", type=Path)
    version = commands.add_parser("package-version")
    version.add_argument("root", type=Path)
    version.add_argument("--declared", action="store_true", help="Select exact formal source version for accepted release packages.")
    start = commands.add_parser("start")
    start.add_argument("root", type=Path)
    start.add_argument("--output", type=Path, required=True)
    start.add_argument("--uses-prebuilt-outputs", action="store_true")
    args = parser.parse_args()
    if args.command == "package-version":
        print(package_version(args.root.resolve(), declared=args.declared))
        return
    if args.command == "start":
        result = {"schemaVersion": 1, "startedAtUnixSeconds": time.time(), "source": source_identity(args.root.resolve()),
                  "usesPrebuiltOutputs": args.uses_prebuilt_outputs}
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        return
    if args.command == "discover-tests":
        for project in discover_test_projects(args.root):
            print(project.relative_to(args.root).as_posix())
        return
    result = test_receipt(args.trx, args.match)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"[tests] {result['passed']} executed tests passed; {result['notExecuted']} were not executed: {args.trx}")


if __name__ == "__main__":
    main()
