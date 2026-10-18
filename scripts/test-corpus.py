#!/usr/bin/env python3
"""Generate locked upstream headers and verify exact managed consumers and snapshots.

These checks cover generation and consumer compilation. They do not certify
complete upstream applications, devices, or calls into upstream native libraries.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import re
import shutil
import sys
import tempfile
import time
import uuid
import xml.etree.ElementTree as ET

from acceptance_evidence import digest, publication
from corpus import DEFAULT_LOCK, load_lock, verify
from corpus_policy import selection_policy
from native_fixture_build import msvc_sdk_inputs
from cpp_acceptance_fixture import execution_identity, stage_repository_generator, tool_command

ROOT = Path(__file__).resolve().parents[1]
GROUPS = {"c": ("miniaudio", "sdl3", "cimgui", "cimguizmo", "bgfx"), "cpp": ("bimg",)}
BUDGETS = {"miniaudio": 60, "sdl3": 45, "cimgui": 30, "cimguizmo": 15, "bgfx": 30, "bimg": 15}
REPORT_SCOPE = "locked-upstream-generation-managed-compile-and-reviewed-source-api-snapshots"


def _validate_group_report(path: Path, group: str, status: int) -> dict:
    report = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(report, dict):
        raise RuntimeError("A corpus group report must be an object.")
    lock = load_lock(DEFAULT_LOCK)
    policy_path = ROOT / "tests/corpus/upstream-limitations.json"
    policy = json.loads(policy_path.read_text(encoding="utf-8-sig"))
    libraries = report.get("libraries")
    if (report.get("schemaVersion") != 1 or report.get("scope") != REPORT_SCOPE
            or report.get("group") != group or report.get("platform") != host_platform()
            or report.get("upstreamInvoked") is not False
            or "failure" in report or report.get("corpusLockSha256") != digest(DEFAULT_LOCK)
            or not isinstance(libraries, list) or any(not isinstance(item, dict) for item in libraries)
            or [item.get("libraryId") for item in libraries] != list(GROUPS[group])
            or report.get("selectedApiPolicy") != {"schemaVersion": policy["schemaVersion"], "policySha256": digest(policy_path),
                "scope": policy["scope"], "libraries": policy["libraries"]}):
        raise RuntimeError("The corpus collector received an incomplete or unrelated group report: " + group)
    repositories = report.get("repositories")
    expected = {item["id"]: item for item in lock["repositories"]}
    if (not isinstance(repositories, list) or len(repositories) != len(expected)
            or any(not isinstance(item, dict) for item in repositories)
            or {item.get("id") for item in repositories} != set(expected)
            or report.get("repositoriesAfter") != repositories
            or any(item.get("workingTreeClean") is not True or any(item.get(key) != expected[item["id"]][key]
                   for key in ("revision", "repository", "checkedOutContentSha256")) for item in repositories)):
        raise RuntimeError("The corpus collector lacks unchanged exact locked inputs: " + group)
    identity = report.get("executionIdentity")
    hashes = ("checkedOutSourceSha256", "generatorEntrySha256", "generatorAssemblySha256", "runtimeAssemblySha256", "executionClosureSha256")
    if (not isinstance(identity, dict) or not isinstance(identity.get("sourceRevision"), str)
            or not re.fullmatch(r"[0-9a-f]{40}", identity["sourceRevision"])
            or type(identity.get("sourceWorktreeDirty")) is not bool
            or any(not isinstance(identity.get(key), str) or not re.fullmatch(r"[0-9a-f]{64}", identity[key]) for key in hashes)
            or type(identity.get("executionAssetCount")) is not int or identity["executionAssetCount"] <= 0):
        raise RuntimeError("The corpus collector lacks its executed generator identity: " + group)
    for item in libraries:
        reviewed = item.get("reviewedSnapshots")
        syntax = item.get("nativeBridgeSyntax") if group == "cpp" else item.get("nativeSourceSyntax")
        if (item.get("generated") is not True or item.get("managedCompiled") is not True or item.get("upstreamInvoked") is not False
                or type(reviewed) is not bool or item.get("success") is not reviewed or "failure" in item
                or type(item.get("artifactCount")) is not int or item["artifactCount"] <= 0
                or item.get("omittedDeclarations") != policy["libraries"][item["libraryId"]]
                or group == "cpp" and syntax is None):
            raise RuntimeError("A corpus failure is not exclusively an unreviewed snapshot: " + group)
        if syntax is not None and (not isinstance(syntax, dict) or syntax.get("nativeLinked") is not False
                or syntax.get("upstreamInvoked") is not False or not isinstance(syntax.get("compiler"), str) or not syntax["compiler"]
                or not isinstance(syntax.get("compilerSha256"), str) or not re.fullmatch(r"[0-9a-f]{64}", syntax["compilerSha256"])
                or type(syntax.get("translationUnits")) is not int or syntax["translationUnits"] <= 0):
            raise RuntimeError("The corpus report lacks successful external native syntax checks: " + group)
    unreviewed = any(not item["reviewedSnapshots"] for item in libraries)
    if (report.get("success") is not (not unreviewed) or (status == 3) != unreviewed
            or report.get("failureKind") != ("unreviewed-snapshots" if unreviewed else None)):
        raise RuntimeError("The corpus exit code contradicts its complete snapshot result: " + group)
    return identity


def _collect_groups(run_group, output_root: Path) -> int:
    """Collect both finite groups on authenticated snapshot-only failures, never certify them."""
    output_root.mkdir(parents=True, exist_ok=True)
    unreviewed, first_identity = False, None
    for group in GROUPS:
        output = output_root / ("corpus-" + group + ".json")
        try:
            if output.is_symlink():
                raise RuntimeError("A corpus report cannot be a link.")
            output.unlink(missing_ok=True)  # A previous invocation cannot authenticate this attempt's exit3.
            status = run_group(group, output)
            if type(status) is not int or status not in (0, 3):
                return status if type(status) is int and status != 0 else 1
            identity = _validate_group_report(output, group, status)
            if first_identity is not None and identity != first_identity:
                raise RuntimeError("The corpus groups executed different source or generator closures.")
            first_identity = identity
            unreviewed = unreviewed or status == 3
        except (OSError, ValueError, RuntimeError, KeyError, TypeError) as error:
            print("[corpus] " + group + " collection failed: " + str(error), file=sys.stderr)
            return 1
    return 3 if unreviewed else 0


def host_platform() -> str:
    machine = platform.machine().lower()
    architecture = "x64" if machine in ("amd64", "x86_64") else "arm64" if machine in ("arm64", "aarch64") else None
    if architecture is None:
        raise RuntimeError("The corpus snapshot architecture is not recognized: " + machine)
    return ("windows" if os.name == "nt" else "macos" if platform.system() == "Darwin" else "linux") + "-" + architecture


def configuration(library: str, corpus: Path, output: Path, compiler_override: str | None = None) -> dict:
    template = ROOT / "tests/corpus" / (library + ".bindgen.json")
    value = json.loads(template.read_text(encoding="utf-8-sig"))
    default_corpus = (ROOT / "artifacts/corpus/headers").resolve()

    def path(relative: str) -> str:
        original = (template.parent / relative).resolve()
        if not original.is_relative_to(default_corpus):
            raise RuntimeError("A corpus configuration input is outside its locked source tree: " + relative)
        return str(corpus / original.relative_to(default_corpus))

    source = value["source"]
    for field in ("headers", "includeDirectories", "systemIncludeDirectories"):
        if field in source:
            source[field] = [path(item) for item in source[field]]
    for field in ("includeHeaders", "excludeHeaders"):
        if field in source.get("selection", {}):
            source["selection"][field] = [path(item) for item in source["selection"][field]]
    value["$schema"] = str(ROOT / "schemas/bindgen.schema.json")
    value["output"]["root"] = str(output)
    compiler = compiler_override or os.environ.get("BGCS_CORPUS_CXX") or os.environ.get("BGCS_CPP2C_CXX") or os.environ.get("CXX")
    if not compiler and os.name == "nt":
        compiler = shutil.which("cl.exe")
        if compiler is None:
            raise RuntimeError("The Windows corpus main matrix requires MSVC; initialize its developer environment or set BGCS_CORPUS_CXX to the exact cl.exe.")
    if compiler:
        resolved = shutil.which(compiler) or (compiler if Path(compiler).is_file() else None)
        if resolved is None:
            raise RuntimeError("The explicitly configured corpus compiler was not found: " + compiler)
        value["targets"] = [{"id": "host", "settings": {"compilerPath": str(Path(resolved).resolve())}}]
    return value


def compile_bridge(root: Path, manifest: dict, request: dict, run) -> dict:
    artifacts = manifest["artifacts"]
    native_sources = [item for item in artifacts if item["mediaType"] in ("text/x-c", "text/x-c++")
                      and Path(item["relativePath"]).suffix in (".c", ".cpp", ".cc", ".cxx")]
    manifests = [root / item["relativePath"] for item in artifacts if item["relativePath"].endswith("/bridge.manifest.json")]
    if not native_sources or len(manifests) != 1:
        raise RuntimeError("Generated native sources require one exact neutral source manifest.")
    native = json.loads(manifests[0].read_text(encoding="utf-8-sig"))
    if sorted(native["sources"], key=lambda item: item["relativePath"]) != sorted(native_sources, key=lambda item: item["relativePath"]):
        raise RuntimeError("The neutral source manifest differs from the authenticated native source artifacts.")
    targets = request.get("targets", [])
    if len(targets) > 1 or targets and targets[0]["id"] != "host":
        raise RuntimeError("The corpus native syntax fixture requires one explicitly selected host compiler.")
    settings = targets[0].get("settings", {}) if targets else {}
    selected = settings.get("compilerPath") or os.environ.get("BGCS_CORPUS_CXX") or os.environ.get("BGCS_CPP2C_CXX") or os.environ.get("CXX")
    compiler = (shutil.which(selected) or (selected if Path(selected).is_file() else None)) if selected else None
    if not compiler:
        raise RuntimeError("The repository corpus fixture requires an explicitly selected native compiler.")
    compiler = str(Path(compiler).resolve())
    source = request["source"]
    directories = source.get("includeDirectories", []) + source.get("systemIncludeDirectories", [])
    directories += sorted({str((root / item["relativePath"]).parent) for item in native["headers"]})
    directories += settings.get("cxxSystemIncludeDirectories", []) + settings.get("systemIncludeDirectories", [])
    defines = source.get("defines", []) + settings.get("defines", [])
    arguments = source.get("arguments", []) + settings.get("arguments", [])
    is_msvc = Path(compiler).name.casefold() in ("cl.exe", "cl", "clang-cl.exe", "clang-cl")
    if is_msvc:
        sdk_includes, _ = msvc_sdk_inputs(dict(os.environ))
        directories += list(map(str, sdk_includes))
    directories = list(dict.fromkeys(directories))
    for index, item in enumerate(native_sources):
        native_source = root / item["relativePath"]
        cpp = native_source.suffix != ".c"
        if is_msvc:
            prefix = [compiler, "/nologo", "/Zs", "/std:c++latest" if cpp else "/std:c17", "/TP" if cpp else "/TC", "/Zc:preprocessor"]
            if cpp: prefix += ["/EHsc", "/Zc:__cplusplus"]
        else:
            prefix = [compiler, "-x", "c++" if cpp else "c", "-std=" + native["languageStandard"], "-fsyntax-only"]
        prefix += [("/I" if is_msvc else "-I") + item for item in directories]
        prefix += [("/D" if is_msvc else "-D") + item for item in defines]
        run([*prefix, *arguments, str(native_source)], "bridge-syntax-" + str(index))
    return {"compiler": compiler, "compilerSha256": digest(Path(compiler)), "translationUnits": len(native_sources),
            "nativeLinked": False, "upstreamInvoked": False}


def snapshot_source_digest(path: Path, media_type: str, corpus_root: Path) -> str:
    """Compare native source across checkout relocation without rewriting artifacts."""
    if media_type not in ("text/x-c", "text/x-c++", "text/x-c-header", "text/x-c++-header"):
        return digest(path)
    corpus = corpus_root.resolve()
    content = path.read_bytes().decode("utf-8")
    def include(match):
        candidate = Path(match["path"])
        if not candidate.is_absolute():
            return match.group(0)
        resolved = candidate.resolve()
        if not resolved.is_relative_to(corpus):
            return match.group(0)
        return match["prefix"] + "$BGCS_CORPUS/" + resolved.relative_to(corpus).as_posix() + match["suffix"]
    normalized = re.sub(r'^(?P<prefix>[ \t]*#[ \t]*include[ \t]*")(?P<path>[^"\r\n]+)(?P<suffix>")',
                        include, content, flags=re.MULTILINE)
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()


def check_snapshot(library: str, manifest: dict, public_api: Path, output: Path, publication_root: Path, corpus_root: Path, policy: dict) -> bool:
    source_types = {"text/x-csharp", "text/x-c", "text/x-c++", "text/x-c-header", "text/x-c++-header"}
    snapshot = {"schemaVersion": 2, "libraryId": library, "platform": host_platform(),
                "sourceNormalization": "quoted-locked-corpus-include-root-v1",
                "selectionPolicySha256": policy["policySha256"],
                "omittedDeclarations": policy["libraries"][library],
                "sources": {item["relativePath"]: snapshot_source_digest(publication_root / item["relativePath"], item["mediaType"], corpus_root)
                            for item in sorted(manifest["artifacts"], key=lambda item: item["relativePath"])
                            if item["mediaType"] in source_types}, "publicApiSha256": digest(public_api)}
    if not snapshot["sources"]:
        raise RuntimeError("A corpus snapshot cannot certify an empty source set.")
    baseline = ROOT / "tests/corpus/snapshots" / (library + "." + host_platform() + ".json")
    matches = baseline.is_file() and json.loads(baseline.read_text(encoding="utf-8-sig")) == snapshot
    output.mkdir(parents=True, exist_ok=True)
    (output / baseline.name).write_text(json.dumps(snapshot, indent=2) + "\n", encoding="utf-8")
    return matches


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--tool-path", type=Path, help="Already closed private CLI execution closure.")
    parser.add_argument("--runtime-path", type=Path, help="Already closed Runtime assembly for managed consumers.")
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--group", choices=tuple(GROUPS))
    mode.add_argument("--collect-groups", action="store_true", help="Repository-only collection of both complete groups; unreviewed snapshots still fail.")
    parser.add_argument("--library", choices=tuple(library for group in GROUPS.values() for library in group))
    parser.add_argument("--corpus-root", type=Path, default=Path(os.environ.get("BGCS_REAL_LIBRARY_ROOT", ROOT / "artifacts/corpus/headers")))
    parser.add_argument("--output", type=Path)
    args = parser.parse_args(argv)
    if args.collect_groups:
        if args.library is not None or args.output is not None:
            parser.error("--collect-groups requires both complete groups and their original report paths.")
        def run_group(group, output):
            arguments = ["--dotnet", args.dotnet, "--configuration", args.configuration, "--group", group,
                         "--corpus-root", str(args.corpus_root), "--output", str(output)]
            for option in ("tool_path", "runtime_path"):
                if getattr(args, option) is not None:
                    arguments += ["--" + option.replace("_", "-"), str(getattr(args, option))]
            return main(arguments)
        return _collect_groups(run_group, ROOT / "artifacts/acceptance")
    libraries = (args.library,) if args.library else GROUPS[args.group]
    if any(library not in GROUPS[args.group] for library in libraries):
        parser.error("--library must belong to the selected corpus group.")
    output = (args.output or ROOT / "artifacts/acceptance" / ("corpus-" + args.group + ".json")).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    evidence = ROOT / "artifacts/corpus-acceptance" / uuid.uuid4().hex
    evidence.mkdir(parents=True)
    report = {"schemaVersion": 1, "success": False, "scope": REPORT_SCOPE,
              "upstreamInvoked": False,
              "group": args.group, "platform": host_platform(), "evidence": str(evidence), "libraries": []}
    spec = importlib.util.spec_from_file_location("corpus_consumer_helpers", ROOT / "eng/verify-packages.py")
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    corpus = args.corpus_root.resolve()
    dotnet = str(Path(args.dotnet).resolve())
    failed, unreviewed = False, False
    try:
        minimum_free = int(os.environ.get("BGCS_MINIMUM_FREE_BYTES", str(1024 ** 3)))
        if minimum_free <= 0 or shutil.disk_usage(ROOT).free < minimum_free:
            raise RuntimeError("The corpus gate requires its declared positive free-space floor.")
        lock = load_lock(DEFAULT_LOCK)
        report["corpusLockSha256"] = digest(DEFAULT_LOCK)
        report["repositories"] = verify(corpus, lock)
        policy = selection_policy(ROOT, lock, corpus)
        report["selectedApiPolicy"] = policy
        temporary = ROOT / "artifacts/temporary"
        temporary.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="corpus-consumers-", dir=temporary) as directory:
            work = Path(directory)
            tool = args.tool_path.resolve() if args.tool_path else stage_repository_generator(ROOT, work, args.configuration)
            runtime = args.runtime_path.resolve() if args.runtime_path else ROOT / "src/BGCS.Runtime/bin" / args.configuration / "net10.0/BGCS.Runtime.dll"
            if not tool.is_file() or not runtime.is_file():
                raise RuntimeError("The corpus generator closure or Runtime assembly is missing.")
            report["executionIdentity"] = execution_identity(ROOT, tool, runtime)
            environment = {"DOTNET_ROOT": str(Path(dotnet).parent), "DOTNET_HOST_PATH": dotnet}
            for library in libraries:
                item = {"libraryId": library, "success": False, "generated": False, "managedCompiled": False,
                        "upstreamInvoked": False, "reviewedSnapshots": False,
                        "omittedDeclarations": policy["libraries"][library]}
                report["libraries"].append(item)
                library_evidence = evidence / library
                library_evidence.mkdir()
                consumer = work / library
                consumer.mkdir()
                request = configuration(library, corpus, library_evidence / "Generated")
                config = library_evidence / "bindgen.json"
                config.write_text(json.dumps(request, indent=2) + "\n", encoding="utf-8")

                def run(arguments, name, **options):
                    return helper.run_process(arguments, consumer, library_evidence, name,
                                              environment_overrides=environment, **options)

                try:
                    started = time.monotonic()
                    generated = json.loads(run([*tool_command(dotnet, tool), "generate", str(config), "--json"],
                                               "generation", separate_stderr=True))
                    item["generationSeconds"] = round(time.monotonic() - started, 3)
                    if not generated.get("success") or not generated.get("published"):
                        raise RuntimeError("The public corpus request did not publish a validated complete set.")
                    if os.environ.get("BGCS_ENFORCE_GENERATION_BUDGETS") == "1" and item["generationSeconds"] > BUDGETS[library]:
                        raise RuntimeError("The existing corpus generation budget was exceeded: " + str(BUDGETS[library]))
                    root = Path(generated["outputRoot"])
                    owned = publication(root, library)
                    sources = [root / entry["relativePath"] for entry in owned["artifacts"] if entry["mediaType"] == "text/x-csharp"]
                    if len(sources) != 1 or sources[0].name != "Bindings.cs":
                        raise RuntimeError("The corpus did not obey its complete single-file managed output contract.")
                    item.update(generated=True, outputRoot=str(root), artifactCount=len(owned["artifacts"]))
                    native_sources = any(entry["mediaType"] in ("text/x-c", "text/x-c++") for entry in owned["artifacts"])
                    if args.group == "cpp" or native_sources:
                        item["nativeBridgeSyntax" if args.group == "cpp" else "nativeSourceSyntax"] = compile_bridge(root, owned, request, run)
                    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
                    properties = ET.SubElement(project, "PropertyGroup")
                    for key, value in {"TargetFramework": "net10.0", "AllowUnsafeBlocks": "true", "Nullable": "enable", "ImplicitUsings": "disable",
                                       "TreatWarningsAsErrors": "true", "EnableDefaultCompileItems": "false", "UseSharedCompilation": "false"}.items():
                        ET.SubElement(properties, key).text = value
                    items = ET.SubElement(project, "ItemGroup")
                    for source in sources:
                        ET.SubElement(items, "Compile", Include=str(source))
                    ET.SubElement(ET.SubElement(items, "Reference", Include="BGCS.Runtime"), "HintPath").text = str(runtime)
                    ET.ElementTree(project).write(consumer / "Corpus.Generated.csproj", encoding="utf-8", xml_declaration=True)
                    run([dotnet, "build", "Corpus.Generated.csproj", "-c", args.configuration, "-m:1", "-nr:false"], "managed-compile")
                    item["managedCompiled"] = True
                    api = library_evidence / "public-api.txt"
                    run([dotnet, str(ROOT / "eng/BGCS.RepositoryTools/bin" / args.configuration / "net10.0/BGCS.RepositoryTools.dll"), "validate", "api-snapshot", str(consumer / "bin" / args.configuration / "net10.0/Corpus.Generated.dll"), str(api)], "public-api")
                    item["reviewedSnapshots"] = check_snapshot(library, owned, api, ROOT / "artifacts/acceptance/candidate-snapshots/corpus", root, corpus, policy)
                    unreviewed = unreviewed or not item["reviewedSnapshots"]
                    item["success"] = item["reviewedSnapshots"]
                except Exception as error:
                    failed = True
                    item["failure"] = str(error)
                    print("[corpus] " + library + ": " + str(error), file=sys.stderr)
        report["repositoriesAfter"] = verify(corpus, lock)
        report["success"] = not failed and not unreviewed and len(report["libraries"]) == len(libraries)
        report["failureKind"] = "execution" if failed else "unreviewed-snapshots" if unreviewed else None
        return 1 if failed else 3 if unreviewed else 0
    except BaseException as error:
        report["failure"] = str(error)
        raise
    finally:
        output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    raise SystemExit(main())
