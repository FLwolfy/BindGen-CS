#!/usr/bin/env python3
"""Create the four BGCS products from explicit, checked execution closures."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from acceptance_evidence import source_identity as repository_source_identity
import parser_runtime_provenance
from disk_budget import check_free_space, wait_for_process
from source_ownership import verify_source_ownership

MANIFEST = json.loads((ROOT / "distribution/manifests/product-packages.json").read_text(encoding="utf-8"))
RIDS = MANIFEST["toolRuntimeIdentifiers"]
PARSER_VERSION = "20.1.2"
SDK_MODULES = tuple(MANIFEST["sdkAssemblies"])
ASSEMBLY_PROJECTS = MANIFEST["assemblyProjects"]
WORKER_PROJECT = ASSEMBLY_PROJECTS["BGCS.Frontend.Clang.Worker"]
OPTIONAL_PRODUCTS = tuple(MANIFEST["optionalProducts"])


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def source_identity() -> dict:
    identity = repository_source_identity(ROOT)
    return {
        "sourceRevision": identity["revision"],
        "sourceWorktreeDirty": identity["workingTreeDirty"],
        "checkedOutSourceSha256": identity["checkedOutSourceSha256"],
    }


def verify_generator_archive(package: zipfile.ZipFile) -> None:
    forbidden = {"bgcs.contracts.dll", "bgcs.cpp2c.dll", "bgcs.platforms.dll", "bgcs.extensions.targets.dll", "bgcs.extensions.hosts.dll", "bgcs.extensions.dotnet.dll", "bgcs.nativebuild.dll", "bgcs.repositorytools.dll", "bgcs.msbuild.dll", "microsoft.build.framework.dll", "microsoft.build.utilities.core.dll", "microsoft.codeanalysis.workspaces.dll", "microsoft.codeanalysis.csharp.workspaces.dll"}
    unexpected = sorted(name for name in package.namelist() if Path(name).name.casefold() in forbidden)
    if unexpected:
        raise RuntimeError("The shipped generator contains repository/native-build tooling: " + ", ".join(unexpected))


def package_evidence(path: Path) -> dict:
    """Record the delivered binaries, rather than the current working tree's build outputs."""
    with zipfile.ZipFile(path) as package:
        verify_generator_archive(package)
        execution = [{"path": entry.filename, "bytes": entry.file_size,
                      "sha256": hashlib.sha256(package.read(entry)).hexdigest()}
                     for entry in sorted(package.infolist(), key=lambda item: item.filename)
                     if not entry.is_dir() and (entry.filename.endswith((".dll", ".so", ".dylib", ".exe", ".deps.json", ".runtimeconfig.json"))
                                                or ".so." in Path(entry.filename).name)]
    return {"file": path.name, "bytes": path.stat().st_size, "sha256": digest(path), "executionAssets": execution}


def execute(dotnet: str, arguments: list[str], *, environment_overrides: dict[str, str] | None = None) -> None:
    check_free_space(ROOT)
    # Every invocation here is a managed dotnet build/pack. The owned collector
    # uses project-default output paths; an MSVC shell's Platform=x64 must not
    # redirect them into bin/x64. Preserve native compiler/SDK discovery inputs.
    environment = {name: value for name, value in {**os.environ, **(environment_overrides or {})}.items()
                   if name.casefold() != "platform"}
    process = subprocess.Popen([dotnet, *arguments], cwd=ROOT, start_new_session=os.name != 'nt',
        stdout=sys.stdout, stderr=sys.stderr, env=environment,
        creationflags=subprocess.CREATE_NO_WINDOW | subprocess.CREATE_NEW_PROCESS_GROUP if os.name == 'nt' else 0)
    try:
        code = wait_for_process(process, ROOT)
        if code:
            raise subprocess.CalledProcessError(code, process.args)
    except BaseException:
        if process.poll() is None:
            if os.name == 'nt':
                subprocess.run([str(Path(os.environ.get('SystemRoot', r'C:\Windows')) / 'System32/taskkill.exe'),
                    '/PID', str(process.pid), '/T', '/F'], check=False, timeout=30,
                    stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=subprocess.CREATE_NO_WINDOW)
                if process.poll() is None: process.kill()
            else:
                try: os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError: pass
            process.wait(timeout=30)
        raise


def build_products(dotnet: str, configuration: str, version: str, parser_osx_x64: Path | None, source_revision: str) -> None:
    """Forward an explicitly accepted Intel resource root to the worker build alone."""
    if parser_osx_x64 is not None:
        parser_osx_x64 = parser_osx_x64.resolve()
        parser_runtime_provenance.verify_supplemental(ROOT, parser_osx_x64, source_revision)
    # Core and Runtime are independent products, not incidental generator dependencies.
    names = ("BGCS.Core", "BGCS.Runtime", "BGCS", "BGCS.Frontend.Clang.Worker", "BGCS.Tool", *OPTIONAL_PRODUCTS)
    for name in names:
        project = ASSEMBLY_PROJECTS[name] + "/" + name + ".csproj"
        overrides = {"BGCS_CLANG_RUNTIME_DIR": str(parser_osx_x64)} if name == "BGCS.Frontend.Clang.Worker" and parser_osx_x64 is not None else None
        execute(dotnet, ["build", project, "--configuration", configuration, "-m:2", "-nr:false", "-p:UseSharedCompilation=false", f"-p:Version={version}"],
                environment_overrides=overrides)


def build_tool_rids(dotnet: str, configuration: str, version: str, rids: list[str]) -> None:
    """Close every requested tool RID before capturing any restored dependency metadata."""
    # MSBuild must parse an item list. %3B would preserve one literal RID containing semicolons.
    runtime_identifiers = '"' + ";".join(rids) + '"'
    for rid in rids:
        execute(dotnet, ["build", "src/BGCS.Tool/BGCS.Tool.csproj", "--configuration", configuration,
                        "--runtime", rid, "-m:2", "-nr:false", "-p:UseSharedCompilation=false",
                        f"-p:RuntimeIdentifiers={runtime_identifiers}", f"-p:Version={version}"])


def build_output(project: str, configuration: str) -> Path:
    return ROOT / project / "bin" / configuration / "net10.0"


def graph(project: str) -> dict:
    return json.loads((ROOT / project / "obj/project.assets.json").read_text(encoding="utf-8"))


def require_tool_restore_rids(rids: list[str]) -> None:
    targets = graph("src/BGCS.Tool").get("targets", {})
    missing = [rid for rid in rids if not any(target.endswith("/" + rid) for target in targets)]
    if missing:
        raise RuntimeError("Restore the tool's exact requested RID graphs before packaging prebuilt outputs: " + ", ".join(missing))


def merge_package_graphs(projects, require_single_version: bool = False) -> dict:
    """Collect the package closure of every shipped component, independently of project-reference accidents."""
    libraries, identities, versions, folders, edges = {}, {}, {}, {}, {}
    for project in projects:
        assets = graph(project)
        folders.update(assets["packageFolders"])
        for library, details in assets["libraries"].items():
            if details["type"] != "package":
                continue
            name, version = library.rsplit("/", 1)
            identity = (name.casefold(), version)
            if require_single_version and name.casefold() in versions and versions[name.casefold()] != version:
                raise RuntimeError(f"The SDK reference modules require incompatible package versions: {name}.")
            versions[name.casefold()] = version
            if identity in identities:
                previous = libraries[identities[identity]]
                if (previous.get("path"), previous.get("sha512"), set(previous.get("files", []))) != (
                        details.get("path"), details.get("sha512"), set(details.get("files", []))):
                    raise RuntimeError(f"Shipped component restore graphs disagree about package content: {library}.")
            else:
                identities[identity] = library
                libraries[library] = details
        for target in assets.get("targets", {}).values():
            for library, details in target.items():
                if details.get("type") != "package":
                    continue
                actual = {key.rsplit("/", 1)[0].casefold(): key for key, item in target.items()
                          if item.get("type") == "package"}
                for dependency, resolved_version in details.get("dependencies", {}).items():
                    identity = actual.get(dependency.casefold())
                    if identity is None:
                        raise RuntimeError(f"Restored package dependency does not identify its exact resolved library: {library} -> {dependency}/{resolved_version}")
                    edges.setdefault(library.casefold(), set()).add(identity)
    return {"libraries": libraries, "packageFolders": folders,
            "resolvedDependencies": {library: sorted(edges.get(library.casefold(), set())) for library in libraries}}


def package_file(assets: dict, library: str, relative: str) -> Path:
    details = assets["libraries"][library]
    if details["type"] != "package":
        raise RuntimeError(f"The execution closure project file is missing: {library}/{relative}")
    for folder in assets["packageFolders"]:
        path = Path(folder) / details["path"] / relative
        if path.is_file():
            return path
    raise FileNotFoundError(f"Restored execution asset is absent: {library}/{relative}")


def execution_closure(project: str, configuration: str, name: str, destination: Path) -> dict[str, Path]:
    output = build_output(project, configuration)
    deps_path = output / f"{name}.deps.json"
    deps = json.loads(deps_path.read_text(encoding="utf-8"))
    assets = graph(project)
    target = deps["targets"][deps["runtimeTarget"]["name"]]
    collected: dict[str, Path] = {}

    def add(relative: str, source: Path) -> None:
        path = destination / relative
        if not path.resolve().is_relative_to(destination.resolve()):
            raise RuntimeError(f"Execution asset escapes its private closure: {relative}")
        if relative in collected and digest(collected[relative]) != digest(source):
            raise RuntimeError(f"Execution asset collision: {relative}")
        collected[relative] = source
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, path)

    for library, metadata in target.items():
        parser_native = library.split("/", 1)[0].lower().startswith(("libclang.runtime.", "libclangsharp.runtime."))
        for relative, details in metadata.get("runtime", {}).items():
            filename = Path(relative).name
            if filename == "_._":
                continue
            source = output / filename
            if not source.is_file():
                source = package_file(assets, library, relative)
            add(filename, source)
        for relative, details in metadata.get("resources", {}).items():
            locale = details.get("locale") or Path(relative).parent.name
            path = f"{locale}/{Path(relative).name}"
            source = output / path
            if not source.is_file():
                source = package_file(assets, library, relative)
            add(path, source)
        for relative, details in metadata.get("runtimeTargets", {}).items():
            asset_type = details.get("assetType")
            if asset_type == "native" and parser_native:
                # Parser-native resources are selected separately by host and
                # verified against the worker's per-host deployment manifest.
                continue
            if asset_type not in ("runtime", "native", "resources"):
                raise RuntimeError(f"Unknown execution asset type: {library}/{relative}: {asset_type}")
            if Path(relative).name == "_._":
                continue
            source = output / relative
            if not source.is_file():
                source = package_file(assets, library, relative)
            add(relative, source)
        if not parser_native:
            for relative in metadata.get("native", {}):
                if Path(relative).name == "_._":
                    continue
                source = output / relative
                if not source.is_file():
                    source = package_file(assets, library, relative)
                add(relative, source)
    add(deps_path.name, deps_path)
    runtime_config = output / f"{name}.runtimeconfig.json"
    add(runtime_config.name, runtime_config)
    if f"{name}.dll" not in collected:
        raise RuntimeError(f"Entry assembly is absent from its own dependency manifest: {name}")
    return collected


def native_closure(assets: dict, rid: str, supplemental: Path | None) -> dict[str, Path]:
    if rid == "osx-x64":
        if supplemental is None:
            raise RuntimeError("Full six-host packaging requires --parser-osx-x64 with independently accepted LLVM 20 assets.")
        if not (supplemental / "licenses/LLVM-LICENSE.txt").is_file():
            raise RuntimeError("The supplemental parser closure must include its LLVM license inventory.")
        parser_runtime_provenance.verify_supplemental(ROOT, supplemental, parser_runtime_provenance.git_head(ROOT))
        files = {file.name: file for file in supplemental.glob("*.dylib")}
    else:
        files = {}
        for prefix in ("libclang.runtime", "libClangSharp.runtime"):
            library = next((key for key in assets["libraries"] if key.lower() == f"{prefix}.{rid}/{PARSER_VERSION}".lower()), None)
            if library is None:
                raise RuntimeError(f"The SDK restore graph omitted parser resources for {rid}.")
            for relative in assets["libraries"][library]["files"]:
                if relative.startswith(f"runtimes/{rid}/native/"):
                    source = package_file(assets, library, relative)
                    filename = Path(relative).name
                    if filename in files and digest(files[filename]) != digest(source):
                        raise RuntimeError(f"Native parser collision for {rid}: {filename}")
                    files[filename] = source
    extension = ".dll" if rid.startswith("win-") else ".dylib" if rid.startswith("osx-") else ".so"
    for name in ("libclang", "libClangSharp"):
        if name + extension not in files:
            raise RuntimeError(f"The parser closure does not contain {name} for {rid}.")
    if rid != "osx-x64":
        parser_runtime_provenance.verify_upstream(ROOT, rid, assets, files)
    return files


def artifact(relative: str, file: Path) -> dict:
    return {"relativePath": relative, "mediaType": "application/json" if relative.endswith(".json") else "application/octet-stream", "digest": {"value": digest(file)}}


def copy_dependency_notices(assets: dict, destination: Path) -> None:
    inventory = []
    for library, details in sorted(assets["libraries"].items()):
        if details["type"] != "package":
            continue
        name, version = library.rsplit("/", 1)
        package_root = next((Path(folder) / details["path"] for folder in assets["packageFolders"] if (Path(folder) / details["path"]).is_dir()), None)
        if package_root is None:
            raise FileNotFoundError(f"Restored license source is unavailable: {library}")
        nuspec = next(package_root.glob("*.nuspec"))
        metadata = next(element for element in ET.parse(nuspec).iter() if element.tag.rsplit("}", 1)[-1] == "metadata")
        fields = {element.tag.rsplit("}", 1)[-1]: element for element in metadata}
        license_element = fields.get("license")
        license_text = license_element.text if license_element is not None else fields.get("licenseUrl").text if fields.get("licenseUrl") is not None else None
        if not license_text:
            raise RuntimeError(f"The shipped dependency lacks a license declaration: {library}")
        notices = [relative for relative in details["files"] if Path(relative).name.lower().startswith(("license", "notice", "copying", "third-party", "thirdparty"))]
        if license_element is not None and license_element.get("type") == "file" and license_text not in notices:
            notices.append(license_text)
        output = destination / name / version
        output.mkdir(parents=True, exist_ok=True)
        # NuGet pack filters .nuspec payloads as package metadata, even below
        # licenses/. Keep the upstream bytes under a package-safe XML filename.
        delivered_nuspec = nuspec.name + ".xml"
        shutil.copyfile(nuspec, output / delivered_nuspec)
        copied = []
        for relative in sorted(set(notices)):
            source = package_root / relative
            if not source.is_file():
                raise FileNotFoundError(f"The declared license notice is absent: {library}/{relative}")
            target = output / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
            copied.append({"path": f"{name}/{version}/{relative}", "sha256": digest(source)})
        if not details.get("sha512") or library not in assets.get("resolvedDependencies", {}):
            raise RuntimeError(f"The delivered dependency lacks its exact restored checksum or dependency graph: {library}")
        inventory.append({"id": name, "version": version, "license": license_text,
                          "licenseType": license_element.get("type") if license_element is not None else "url",
                          "packageSha512": details["sha512"],
                          "resolvedDependencies": assets["resolvedDependencies"][library],
                          "nuspec": {"path": f"{name}/{version}/{delivered_nuspec}", "sha256": digest(nuspec)},
                          "copyright": fields.get("copyright").text if fields.get("copyright") is not None else None,
                          "notices": copied})
    (destination / "license-inventory.json").write_text(json.dumps(inventory, indent=2) + "\n", encoding="utf-8")


def copy_resource_notices(destination: Path) -> None:
    source = ROOT / 'distribution/third-party/english-words'
    provenance = json.loads((source / 'embedded-resource-provenance.json').read_text())
    resource = ROOT / 'src/BGCS.CSharp/Resources/en_EN.txt'
    license_path = source / provenance['license']['path']
    if license_path.resolve().parent != source.resolve():
        raise RuntimeError('The redistributed license path escapes its audited resource notice directory.')
    content = resource.read_bytes()
    upstream = content.replace(b'\n', b'\r\n')
    if (resource.stat().st_size != provenance['bytes'] or digest(resource) != provenance['sha256']
            or digest(license_path) != provenance['license']['sha256']
            or provenance['transformation'] != 'crlf-to-lf' or b'\r' in content
            or len(upstream) != provenance['sourceBytes'] or hashlib.sha256(upstream).hexdigest() != provenance['sourceSha256']
            or content.count(b'\n') != provenance['words']):
        raise RuntimeError('The redistributed embedded vocabulary or original license changed from its audited provenance.')
    shutil.copytree(source, destination / 'resources/english-words')
    shutil.copyfile(ROOT / 'THIRD-PARTY-NOTICES.md', destination / 'THIRD-PARTY-NOTICES.md')


def pack_nuspec(dotnet: str, project: str, template: Path, stage: Path, output: Path, version: str, dependencies: list[tuple[str, str]] = ()) -> None:
    document = ET.parse(template)
    metadata = document.getroot().find("metadata")
    metadata.find("version").text = version
    repository = metadata.find("repository")
    if repository is not None:
        repository.set("commit", source_identity()["sourceRevision"])
    group = metadata.find("dependencies/group")
    if group is not None:
        for name, dependency_version in dependencies:
            ET.SubElement(group, "dependency", {"id": name, "version": f"[{dependency_version}]"})
    nuspec = stage.parent / f"{metadata.findtext('id')}.nuspec"
    document.write(nuspec, encoding="utf-8", xml_declaration=True)
    execute(dotnet, ["pack", project, "--output", str(output), "-m:1", "-nr:false", "-p:UseSharedCompilation=false", f"-p:NuspecFile={nuspec}", f"-p:NuspecBasePath={stage}", f"-p:Version={version}", f"-p:PackageId={metadata.findtext('id')}"])


def prepare_sdk(dotnet: str, stage: Path, configuration: str, version: str, supplemental: Path | None, selected_rids: list[str]) -> tuple[list[tuple[str, str]], Path]:
    sdk = stage / "sdk"
    worker = stage / "worker"
    common = worker / "common"
    execution_closure(WORKER_PROJECT, configuration, "BGCS.Frontend.Clang.Worker", common)
    reference_graph = merge_package_graphs((ASSEMBLY_PROJECTS[name] for name in SDK_MODULES), require_single_version=True)
    dependencies = [("BGCS.Core", version), ("BGCS.Runtime", version)]
    dependencies += sorted((key.rsplit("/", 1)[0], key.rsplit("/", 1)[1]) for key in reference_graph["libraries"])
    if supplemental is not None:
        dependencies.append(("BGCS.ParserRuntime.osx-x64", version))
    library = sdk / "lib/net10.0"
    library.mkdir(parents=True)
    for name in SDK_MODULES:
        output = build_output(ASSEMBLY_PROJECTS[name], configuration)
        for extension in (".dll", ".xml", ".pdb"):
            path = output / (name + extension)
            if not path.is_file():
                raise FileNotFoundError(f"The complete SDK module output is missing: {path}")
            shutil.copyfile(path, library / path.name)
    assets = sdk / "contentFiles/any/net10.0/bgcs/worker-assets"
    assets.mkdir(parents=True)
    with zipfile.ZipFile(assets / "worker-common.zip", "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for file in sorted(common.rglob("*")):
            if file.is_file():
                info = zipfile.ZipInfo(file.relative_to(common).as_posix(), (1980, 1, 1, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(info, file.read_bytes())
    manifests = assets / "manifests"
    manifests.mkdir(parents=True)
    common_artifacts = [artifact(file.relative_to(common).as_posix(), file) for file in sorted(common.rglob("*")) if file.is_file()]
    parser_assets = graph(WORKER_PROJECT)
    for rid in selected_rids:
        files = native_closure(parser_assets, rid, supplemental)
        native_directory = worker / "native" / rid
        native_directory.mkdir(parents=True)
        for name, source in files.items():
            shutil.copyfile(source, native_directory / name)
        items = common_artifacts + [artifact(f"runtimes/{rid}/native/{name}", file) for name, file in sorted(files.items())]
        manifest = {"schemaVersion": 1, "hostRid": rid, "parserIdentity": "ClangSharp/20.1.2.4;LLVM/20", "artifacts": items}
        (manifests / f"{rid}.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    shutil.copytree(manifests, worker / "manifests")
    schema_directory = sdk / "schemas"
    shutil.copytree(ROOT / "schemas", schema_directory)
    # The packaging CLI may have been built before a subsequently rebuilt SDK module.
    # Export through a private CLI closure whose BGCS assemblies are the exact SDK payload,
    # without replacing shared build outputs or trusting stale copied references.
    schema_exporter = stage / "schema-exporter"
    execution_closure("src/BGCS.Tool", configuration, "BGCS.Tool", schema_exporter)
    for payload in library.glob("*.dll"):
        shutil.copyfile(payload, schema_exporter / payload.name)
    for name in ("BGCS.Core", "BGCS.Runtime"):
        source = build_output(ASSEMBLY_PROJECTS[name], configuration) / (name + ".dll")
        shutil.copyfile(source, schema_exporter / source.name)
    tool = schema_exporter / "BGCS.Tool.dll"
    for name, kind in (("bindgen.schema.json", "binding"), ("workspace.schema.json", "workspace"), ("extension.schema.json", "extension")):
        execute(dotnet, [str(tool), "schema", str(schema_directory / name), "--kind", kind])
    notices = sdk / "licenses/clang-resource"
    notices.mkdir(parents=True)
    for name in ("LICENSE.txt", "README.md"):
        shutil.copyfile(ROOT / "extensions/frontends/BGCS.Frontend.Clang/Resources/Clang" / name, notices / name)
    shutil.copyfile(ROOT / "README.md", sdk / "README.md")
    shutil.copyfile(ROOT / "LICENSE", sdk / "LICENSE")
    notice_graph = merge_package_graphs([*(ASSEMBLY_PROJECTS[name] for name in SDK_MODULES), WORKER_PROJECT])
    copy_dependency_notices(notice_graph, sdk / "licenses/dependencies")
    copy_resource_notices(sdk / 'licenses/dependencies')
    return dependencies, worker



def prepare_optional_packages(dotnet: str, stage: Path, output: Path, configuration: str, version: str) -> None:
    for product in OPTIONAL_PRODUCTS:
        package = stage / product
        library = package / "lib/net10.0"
        library.mkdir(parents=True)
        for assembly in MANIFEST["optionalPackageAssemblies"][product]:
            source = build_output(ASSEMBLY_PROJECTS[assembly], configuration)
            for extension in (".dll", ".xml", ".pdb"):
                file = source / (assembly + extension)
                if not file.is_file(): raise FileNotFoundError("Optional extension closure is incomplete: " + str(file))
                shutil.copyfile(file, library / file.name)
        for name in ("README.md", "LICENSE", "THIRD-PARTY-NOTICES.md"):
            shutil.copyfile(ROOT / name, package / name)
        document = ET.Element("package")
        metadata = ET.SubElement(document, "metadata")
        for key, value in (("id", product), ("version", version), ("authors", "FLwolfy"),
                ("description", "Explicit optional source-generation extension for BGCS; consumer builds remain external."), ("readme", "README.md")):
            ET.SubElement(metadata, key).text = value
        ET.SubElement(metadata, "license", type="expression").text = "MIT"
        ET.SubElement(metadata, "repository", type="git", url="https://github.com/FLwolfy/BindGen-CS")
        group = ET.SubElement(ET.SubElement(metadata, "dependencies"), "group", targetFramework="net10.0")
        ET.SubElement(group, "dependency", id="BGCS.Core", version="[" + version + "]")
        ET.SubElement(ET.SubElement(document, "files"), "file", src="**/*", target="")
        template = stage / (product + "-template.nuspec")
        ET.ElementTree(document).write(template, encoding="utf-8", xml_declaration=True)
        pack_nuspec(dotnet, "distribution/BGCS.Sdk.Package/BGCS.Sdk.Package.csproj", template, package, output, version)


def verify_extension_archive(package: zipfile.ZipFile, product: str, version: str) -> None:
    expected = set(MANIFEST["optionalPackageAssemblies"][product])
    assemblies = {Path(name).stem for name in package.namelist() if name.startswith("lib/") and name.endswith(".dll")}
    if assemblies != expected: raise RuntimeError("Optional extension has an incomplete or coupled managed closure: " + product)
    metadata = ET.fromstring(package.read(next(name for name in package.namelist() if name.endswith(".nuspec"))))
    dependencies = {(node.get("id"), node.get("version")) for node in metadata.iter() if node.tag.rsplit("}", 1)[-1] == "dependency"}
    if dependencies != {("BGCS.Core", "[" + version + "]")}:
        raise RuntimeError("Optional extensions must depend only on the exact Core product: " + product)
    if any(name.startswith(("build/", "buildTransitive/", "runtimes/", "tools/")) for name in package.namelist()):
        raise RuntimeError("An optional extension contains forbidden build/runtime tooling: " + product)

def verify_sdk_worker_archive(package: zipfile.ZipFile, expected_rids: list[str] | None = None) -> None:
    """Bind standard NuGet content copy metadata to the exact managed worker closure."""
    prefix = "contentFiles/any/net10.0/bgcs/worker-assets/"
    names = package.namelist()
    required_schemas = {'bindgen.schema.json', 'workspace.schema.json', 'extension.schema.json',
                        'native-source-manifest.schema.json', 'capability-report.schema.json'}
    if {Path(name).name for name in names if name.startswith('schemas/') and name.endswith('.json')} != required_schemas:
        raise RuntimeError('The SDK must deliver exactly the five current source-generation schemas.')
    if any(name.startswith(("tools/msbuild/", "build/", "buildTransitive/")) for name in names):
        raise RuntimeError("The generator SDK must not deliver MSBuild Tasks, props or targets.")
    nuspecs = [name for name in names if name.endswith(".nuspec")]
    if len(nuspecs) != 1:
        raise RuntimeError("SDK worker content needs one exact NuGet metadata document.")
    document = ET.fromstring(package.read(nuspecs[0]))
    rules = [element for element in document.iter() if element.tag.rsplit("}", 1)[-1] == "files"
             and element.get("include") == "any/net10.0/bgcs/worker-assets/**/*"]
    if (len(rules) != 1 or rules[0].get("buildAction") != "None" or rules[0].get("copyToOutput") != "true"
            or rules[0].get("flatten") != "false"):
        raise RuntimeError("SDK worker assets require standard None/copyToOutput/flatten=false contentFiles metadata.")
    import io
    with zipfile.ZipFile(io.BytesIO(package.read(prefix + "worker-common.zip"))) as common:
        files = [entry for entry in common.infolist() if not entry.is_dir()]
        actual = {entry.filename: hashlib.sha256(common.read(entry)).hexdigest() for entry in files}
        if (not actual or len(actual) != len(files) or any("\\" in name or ":" in name
                or any(part in ("", ".", "..") for part in name.split("/")) for name in actual)):
            raise RuntimeError("SDK worker archive has duplicate or unsafe relative entries.")
        manifests = [name for name in names if name.startswith(prefix + "manifests/") and name.endswith(".json")]
        if not manifests:
            raise RuntimeError("SDK worker content lacks its per-host manifest.")
        if expected_rids is not None and {Path(name).stem for name in manifests} != set(expected_rids):
            raise RuntimeError("SDK worker content omits a declared host manifest.")
        for name in manifests:
            manifest = json.loads(package.read(name))
            rid = Path(name).stem
            if (rid not in RIDS or manifest.get("schemaVersion") != 1 or manifest.get("hostRid") != rid
                    or manifest.get("parserIdentity") != "ClangSharp/20.1.2.4;LLVM/20"):
                raise RuntimeError("SDK worker manifest has a different host or parser identity.")
            rows = manifest.get("artifacts", [])
            expected = {row["relativePath"]: row["digest"]["value"] for row in rows
                        if not row["relativePath"].startswith(f"runtimes/{rid}/native/")}
            if (len({row["relativePath"] for row in rows}) != len(rows) or actual != expected):
                raise RuntimeError("SDK common worker archive differs from its exact manifest set or hashes.")
            if not {"BGCS.Frontend.Clang.Worker.dll", "BGCS.Frontend.Clang.Worker.deps.json", "BGCS.Frontend.Clang.Worker.runtimeconfig.json", "BGCS.Hosting.Standard.dll"}.issubset(actual):
                raise RuntimeError("SDK worker content lacks its managed entry, dependency map or runtime configuration.")
            extension = ".dll" if rid.startswith("win-") else ".dylib" if rid.startswith("osx-") else ".so"
            if not {f"runtimes/{rid}/native/{name}{extension}" for name in ("libclang", "libClangSharp")}.issubset({row["relativePath"] for row in rows}):
                raise RuntimeError("SDK worker manifest lacks its required native parser pair.")


def verify_tool_worker_archive(package: zipfile.ZipFile, rid: str) -> None:
    """Require the delivered tool worker to match its locked manifest, including absence of retired files."""
    prefix = f"tools/net10.0/{rid}/bgcs/worker/"
    manifest_name = prefix + "worker-assets.manifest.json"
    files = [entry for entry in package.infolist() if not entry.is_dir()]
    if len({entry.filename.casefold() for entry in files}) != len(files):
        raise RuntimeError("The tool package has ambiguous duplicate or case-colliding entries.")
    entries = {entry.filename: entry for entry in files}
    if any(name.casefold().startswith(prefix.casefold()) and not name.startswith(prefix) for name in entries):
        raise RuntimeError("The tool worker archive uses a noncanonical worker path.")
    if manifest_name not in entries or entries[manifest_name].file_size > 16 * 1024 * 1024:
        raise RuntimeError(f"The tool worker has no bounded locked manifest for {rid}.")
    manifest = json.loads(package.read(manifest_name))
    if (not isinstance(manifest, dict) or set(manifest) != {"schemaVersion", "hostRid", "parserIdentity", "artifacts"}
            or manifest["schemaVersion"] != 1 or manifest["hostRid"] != rid
            or manifest["parserIdentity"] != "ClangSharp/20.1.2.4;LLVM/20"
            or not isinstance(manifest["artifacts"], list) or not manifest["artifacts"]):
        raise RuntimeError(f"The tool worker manifest does not identify the exact host/parser closure: {rid}.")
    expected, identities = {}, set()
    for artifact in manifest["artifacts"]:
        if not isinstance(artifact, dict) or set(artifact) != {"relativePath", "mediaType", "digest"}:
            raise RuntimeError("The tool worker manifest has an invalid artifact descriptor.")
        relative, digest_value = artifact["relativePath"], artifact["digest"]
        if (not isinstance(relative, str) or "\\" in relative
                or any(part in ("", ".", "..") or any(char in part for char in ':\0<>"|?*')
                       or part.endswith((" ", ".")) for part in relative.split("/"))
                or not isinstance(artifact["mediaType"], str) or not artifact["mediaType"]
                or not isinstance(digest_value, dict) or set(digest_value) != {"value"}
                or not isinstance(digest_value["value"], str) or not re.fullmatch(r"[0-9a-f]{64}", digest_value["value"])):
            raise RuntimeError("The tool worker manifest has an invalid portable path or content digest.")
        if relative.casefold() in identities or relative.casefold() == "worker-assets.manifest.json":
            raise RuntimeError("The tool worker manifest has a duplicate or reserved artifact identity.")
        identities.add(relative.casefold())
        expected[relative] = digest_value["value"]
    required = {"BGCS.Frontend.Clang.Worker.dll", "BGCS.Frontend.Clang.Worker.deps.json",
                "BGCS.Frontend.Clang.Worker.runtimeconfig.json", "BGCS.Core.dll", "BGCS.Infrastructure.dll",
                "BGCS.Frontend.Clang.dll", "BGCS.Hosting.Standard.dll", "ClangSharp.dll", "ClangSharp.Interop.dll"}
    extension = ".dll" if rid.startswith("win-") else ".dylib" if rid.startswith("osx-") else ".so"
    required |= {f"runtimes/{rid}/native/{name}{extension}" for name in ("libclang", "libClangSharp")}
    if not required <= expected.keys():
        raise RuntimeError(f"The tool worker manifest omits required execution assets: {sorted(required - expected.keys())}")
    actual = {name[len(prefix):] for name in entries if name.startswith(prefix)}
    if actual != expected.keys() | {"worker-assets.manifest.json"}:
        raise RuntimeError(f"The archived tool worker differs from its locked file set: {rid}: "
                           f"missing={sorted(expected.keys() - actual)}, unexpected={sorted(actual - expected.keys() - {'worker-assets.manifest.json'})}")
    for relative, expected_digest in expected.items():
        with package.open(prefix + relative) as stream:
            if hashlib.file_digest(stream, "sha256").hexdigest() != expected_digest:
                raise RuntimeError(f"The archived tool worker content digest differs: {rid}/{relative}")


def verify_parser_runtime_archive(package: zipfile.ZipFile) -> None:
    """Check NuGet's actual native asset paths and every relocated provenance payload byte."""
    entries = {entry.filename: entry for entry in package.infolist() if not entry.is_dir()}
    if "runtime-provenance.json" not in entries or entries["runtime-provenance.json"].file_size > 16 * 1024 * 1024:
        raise RuntimeError("The parser package lacks its bounded resource provenance.")
    manifest = json.loads(package.read("runtime-provenance.json"))
    expected = {}
    for item in manifest.get("artifacts", []):
        relative = item.get("path")
        if not isinstance(relative, str) or "\\" in relative or any(part in ("", ".", "..") for part in relative.split("/")):
            raise RuntimeError("The parser package provenance contains an invalid resource path.")
        if "/" not in relative and relative.endswith(".dylib"):
            target = "runtimes/osx-x64/native/" + relative
        elif relative.startswith(("licenses/", "build-inputs/")):
            target = relative
        else:
            raise RuntimeError("The parser package provenance contains an unexpected resource: " + relative)
        if target in expected:
            raise RuntimeError("The parser package provenance contains duplicate resource paths.")
        expected[target] = item
    required = {"runtimes/osx-x64/native/libclang.dylib", "runtimes/osx-x64/native/libClangSharp.dylib",
                "licenses/LLVM-LICENSE.txt", "licenses/ClangSharp-LICENSE.md", "licenses/ClangSharp-NOTICE.md",
                "build-inputs/homebrew-llvm.json"}
    if not required.issubset(expected):
        raise RuntimeError("The parser package provenance omits a required native asset, input or license.")
    payload = {name for name in entries if name not in ("runtime-provenance.json", "_rels/.rels", "[Content_Types].xml", ".signature.p7s")
               and not name.endswith(".nuspec") and not name.startswith("package/services/metadata/core-properties/")}
    if payload != set(expected):
        raise RuntimeError("The parser package asset layout differs from its provenance: "
                           + ", ".join(sorted(payload.symmetric_difference(expected))[:12]))
    for target, item in expected.items():
        if entries[target].file_size != item.get("bytes"):
            raise RuntimeError("The parser package resource size differs: " + target)
        with package.open(target) as stream:
            if hashlib.file_digest(stream, "sha256").hexdigest() != item.get("sha256"):
                raise RuntimeError("The parser package resource digest differs: " + target)


def verify_packages(output: Path, version: str, full: bool, partial_tool_rid: str | None = None,
                    *, include_osx_x64_parser: bool = False) -> None:
    expected = {f"{name}.{version}.nupkg" for name in MANIFEST["products"] + MANIFEST["optionalProducts"] if name != "BindGen-CS"}
    if full or include_osx_x64_parser:
        expected.add(f"BGCS.ParserRuntime.osx-x64.{version}.nupkg")
    if full:
        expected.add(f"BindGen-CS.{version}.nupkg")
        expected |= {f"BindGen-CS.{rid}.{version}.nupkg" for rid in RIDS}
    elif partial_tool_rid:
        expected |= {f"BindGen-CS.{version}.nupkg", f"BindGen-CS.{partial_tool_rid}.{version}.nupkg"}
    packages = {file.name for file in output.glob(f"*.{version}.nupkg")}
    if packages != expected:
        raise RuntimeError(f"The package set differs from the declared product closure: missing={sorted(expected-packages)}, unexpected={sorted(packages-expected)}")
    expected_symbols = {f"{name}.{version}.snupkg" for name in MANIFEST["symbolProducts"]}
    symbols = {file.name for file in output.glob(f"*.{version}.snupkg")}
    if symbols != expected_symbols:
        raise RuntimeError(f"The symbol package set differs from the declared products: missing={sorted(expected_symbols-symbols)}, unexpected={sorted(symbols-expected_symbols)}")
    repeated_assemblies = {}
    for name in sorted(packages | symbols):
        file = output / name
        if file.stat().st_size >= MANIFEST["maximumPackageBytes"]:
            raise RuntimeError(f"Package exceeds the 250 MiB limit: {name}")
        with zipfile.ZipFile(file) as package:
            entries = [entry.filename for entry in package.infolist() if not entry.is_dir()]
            if len(set(entries)) != len(entries):
                raise RuntimeError(f"The package has ambiguous duplicate entries: {name}")
            bad = package.testzip()
            if bad:
                raise RuntimeError(f"Corrupt package entry: {name}/{bad}")
            if name.startswith("BGCS.Runtime.") and any("clang" in item.lower() for item in package.namelist()):
                raise RuntimeError("Runtime-only consumers must not receive parser assets.")
            if name == f"BGCS.Runtime.{version}.nupkg":
                notice = "THIRD-PARTY-NOTICES.md"
                if notice not in entries or package.read(notice) != (ROOT / notice).read_bytes():
                    raise RuntimeError("The independent Runtime package must retain its exact MIT license notice.")
            for independent in ("BGCS.Core", "BGCS.Runtime"):
                if name == f"{independent}.{version}.nupkg":
                    libraries = {Path(entry).stem for entry in entries if entry.startswith("lib/") and entry.endswith(".dll")}
                    if libraries != {independent}: raise RuntimeError("Independent product leaks another managed assembly: " + independent)
                    metadata = ET.fromstring(package.read(next(entry for entry in entries if entry.endswith(".nuspec"))))
                    if any(node.tag.rsplit("}", 1)[-1] == "dependency" for node in metadata.iter()):
                        raise RuntimeError("Independent product must not carry generator, frontend, runtime or third-party dependencies: " + independent)
            if name.endswith(".snupkg") and not any(entry.endswith(".pdb") for entry in entries):
                raise RuntimeError("The declared symbol package contains no debugging symbols: " + name)
            if name == f"BGCS.{version}.nupkg":
                verify_sdk_worker_archive(package, RIDS if full else None)
                delivered = {Path(entry).stem for entry in entries if entry.startswith("lib/") and entry.endswith(".dll")}
                if delivered != set(SDK_MODULES): raise RuntimeError("The default SDK assembly closure differs from its explicit composition.")
                if any("BGCS.Extension." in entry for entry in entries): raise RuntimeError("Default SDK includes an optional extension.")
                metadata = ET.fromstring(package.read(next(entry for entry in entries if entry.endswith(".nuspec"))))
                dependencies = {node.get("id") for node in metadata.iter() if node.tag.rsplit("}", 1)[-1] == "dependency"}
                if any(identity.startswith("BGCS.") and identity not in {"BGCS.Core", "BGCS.Runtime", "BGCS.ParserRuntime.osx-x64"} for identity in dependencies):
                    raise RuntimeError("Default SDK exposes an internal or optional product dependency.")
            for product in OPTIONAL_PRODUCTS:
                if name == f"{product}.{version}.nupkg": verify_extension_archive(package, product, version)
            if not name.endswith(".snupkg"):
                for entry in entries:
                    if entry.startswith("lib/") and entry.endswith(".dll"):
                        identity = Path(entry).name
                        fingerprint = hashlib.sha256(package.read(entry)).hexdigest()
                        if identity in repeated_assemblies and repeated_assemblies[identity] != fingerprint:
                            raise RuntimeError("The same foundation assembly has divergent bytes across packages: " + identity)
                        repeated_assemblies[identity] = fingerprint
            if name == f"BGCS.ParserRuntime.osx-x64.{version}.nupkg":
                verify_parser_runtime_archive(package)
            for rid in RIDS:
                if name == f"BindGen-CS.{rid}.{version}.nupkg":
                    verify_tool_worker_archive(package, rid)


def archive_payload(path: Path) -> dict[str, str]:
    """Hash every named archive entry; ZIP transport timestamps are not payload bytes."""
    with zipfile.ZipFile(path) as package:
        entries = [entry for entry in package.infolist() if not entry.is_dir()]
        if len({entry.filename for entry in entries}) != len(entries):
            raise RuntimeError(f"The package has ambiguous duplicate entries: {path.name}")
        return {entry.filename: hashlib.sha256(package.read(entry)).hexdigest() for entry in sorted(entries, key=lambda item: item.filename)}


def verify_repeat_content(args, version: str, output: Path, parent: Path) -> dict:
    """Repack the same closed binaries and compare the complete package file/content set."""
    with tempfile.TemporaryDirectory(prefix="package-repeat-", dir=parent) as temporary:
        repeated = Path(temporary)
        command = [sys.executable, str(Path(__file__).resolve()), "--dotnet", args.dotnet,
                   "--configuration", args.configuration, "--version", version,
                   "--output", str(repeated), "--no-build"]
        if args.parser_osx_x64 is not None:
            command.extend(["--parser-osx-x64", str(args.parser_osx_x64)])
        if args.host_only:
            command.extend(["--host-only", args.host_only])
        if args.include_host_tool:
            command.append("--include-host-tool")
        subprocess.run(command, cwd=ROOT, check=True)
        first = {file.name: file for file in output.glob(f"*.{version}.*nupkg")}
        second = {file.name: file for file in repeated.glob(f"*.{version}.*nupkg")}
        if first.keys() != second.keys():
            raise RuntimeError("Repacking changed the complete physical package/symbol file set.")
        checks = []
        for name in sorted(first):
            left, right = archive_payload(first[name]), archive_payload(second[name])
            if left != right:
                changes = sorted(key for key in left.keys() | right.keys() if left.get(key) != right.get(key))
                raise RuntimeError(f"Repacking changed package payload content: {name}: {', '.join(changes[:16])}")
            canonical = json.dumps(left, sort_keys=True, separators=(",", ":")).encode("utf-8")
            checks.append({"file": name, "entryCount": len(left), "payloadManifestSha256": hashlib.sha256(canonical).hexdigest(),
                           "firstArchiveSha256": digest(first[name]), "repeatedArchiveSha256": digest(second[name])})
        return {"success": True, "scope": "all-archive-entry-names-and-uncompressed-content", "packages": checks}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=os.environ.get("DOTNET_HOST_PATH", "dotnet"))
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--version")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/packages")
    parser.add_argument("--parser-osx-x64", type=Path, default=Path(os.environ["BGCS_OSX_X64_PACKAGE_RUNTIME_DIR"]) if os.environ.get("BGCS_OSX_X64_PACKAGE_RUNTIME_DIR") else None)
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--host-only", choices=RIDS, help="Local SDK closure test only; excludes the release tool/package matrix.")
    parser.add_argument("--include-host-tool", action="store_true", help="With --host-only, also pack that host's real tool shard and a partial pointer package. Not full certification.")
    parser.add_argument("--verify-repeat-content", action="store_true", help="Repack the same binaries and require identical complete archive entry names/content, including symbols and metadata.")
    args = parser.parse_args()
    check_free_space(ROOT)
    verify_source_ownership(ROOT)
    source = source_identity()
    version = args.version or ET.parse(ROOT / "Directory.Build.props").findtext("./PropertyGroup/Version")
    if not re.fullmatch(r"4\.1\.0(?:-ci(?:\.[0-9A-Za-z-]+)*)?", version):
        parser.error("This source tree may create only 4.1.0 or private 4.1.0-ci candidate packages.")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    full = args.host_only is None
    if args.include_host_tool and full:
        parser.error("--include-host-tool requires an explicit --host-only RID; full distribution already includes every tool shard.")
    if full and args.parser_osx_x64 is None:
        parser.error("Full package distribution requires accepted --parser-osx-x64 assets.")
    if args.parser_osx_x64 is not None:
        args.parser_osx_x64 = args.parser_osx_x64.resolve()
    tool_rids = RIDS if full else [args.host_only] if args.include_host_tool else []
    if not args.no_build:
        build_products(args.dotnet, args.configuration, version, args.parser_osx_x64, source["sourceRevision"])
        build_tool_rids(args.dotnet, args.configuration, version, tool_rids)
    elif args.parser_osx_x64 is not None:
        parser_runtime_provenance.verify_supplemental(ROOT, args.parser_osx_x64, source["sourceRevision"])
    if full or args.include_host_tool:
        require_tool_restore_rids(RIDS if full else [args.host_only])
    temporary_parent = ROOT / "artifacts/temporary"
    temporary_parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="package-stage-", dir=temporary_parent) as temporary:
        stage = Path(temporary)
        dependencies, worker = prepare_sdk(args.dotnet, stage, args.configuration, version, args.parser_osx_x64, RIDS if full else [args.host_only])
        for project in ("src/BGCS.Core/BGCS.Core.csproj", "src/BGCS.Runtime/BGCS.Runtime.csproj"):
            execute(args.dotnet, ["pack", project, "--configuration", args.configuration, "--no-build", "--output", str(output), f"-p:Version={version}"])
        if args.parser_osx_x64 is not None:
            supplement = stage / "supplement"
            native = supplement / "runtimes/osx-x64/native"
            native.mkdir(parents=True)
            for file in args.parser_osx_x64.glob("*.dylib"):
                shutil.copyfile(file, native / file.name)
            shutil.copytree(args.parser_osx_x64 / "licenses", supplement / "licenses")
            shutil.copyfile(args.parser_osx_x64 / "runtime-provenance.json", supplement / "runtime-provenance.json")
            shutil.copytree(args.parser_osx_x64 / "build-inputs", supplement / "build-inputs")
            pack_nuspec(args.dotnet, "distribution/BGCS.ParserRuntime.osx-x64.Package/BGCS.ParserRuntime.osx-x64.Package.csproj", ROOT / "distribution/BGCS.ParserRuntime.osx-x64.Package/BGCS.ParserRuntime.osx-x64.nuspec", supplement, output, version)
        # Tools consume native preparation inputs. Exclude these files from SDK payload via a copied nuspec filter.
        sdk_template = stage / "sdk-template.nuspec"
        document = ET.parse(ROOT / "distribution/BGCS.Sdk.Package/BGCS.nuspec")
        document.write(sdk_template, encoding="utf-8", xml_declaration=True)
        pack_nuspec(args.dotnet, "distribution/BGCS.Sdk.Package/BGCS.Sdk.Package.csproj", sdk_template, stage / "sdk", output, version, dependencies)
        prepare_optional_packages(args.dotnet, stage, output, args.configuration, version)
        if full or args.include_host_tool:
            tool_notices = stage / "tool-notices"
            copy_dependency_notices(merge_package_graphs(["src/BGCS.Tool", WORKER_PROJECT]), tool_notices)
            copy_resource_notices(tool_notices)
            if args.parser_osx_x64 is not None:
                shutil.copytree(args.parser_osx_x64 / "licenses", tool_notices / "parser-osx-x64/licenses")
                shutil.copytree(args.parser_osx_x64 / "build-inputs", tool_notices / "parser-osx-x64/build-inputs")
                shutil.copyfile(args.parser_osx_x64 / "runtime-provenance.json", tool_notices / "parser-osx-x64/runtime-provenance.json")
            # RID compilation and restore must finish before SDK notices are captured.
            # Packing may publish the closed outputs, but cannot mutate those graphs.
            tool_arguments = ["pack", "src/BGCS.Tool/BGCS.Tool.csproj", "--configuration", args.configuration, "--no-build", "--output", str(output), "-m:1", "-nr:false", "-p:UseSharedCompilation=false", f"-p:Version={version}", f"-p:BGCSWorkerPackageRoot={worker}", f"-p:BGCSToolNoticesRoot={tool_notices}"]
            if not full:
                tool_arguments.extend([f"-p:ToolPackageRuntimeIdentifiers={args.host_only}", f"-p:RuntimeIdentifiers={args.host_only}"])
            execute(args.dotnet, tool_arguments)
        verify_packages(output, version, full, args.host_only if args.include_host_tool else None,
                        include_osx_x64_parser=args.parser_osx_x64 is not None)
        report = {"schemaVersion": 1, "version": version, **source, "configuration": args.configuration,
                  "usedPrebuiltOutputs": args.no_build, "completeProductSet": full,
                  "workerHostRids": RIDS if full else [args.host_only],
                  "toolHostRids": RIDS if full else [args.host_only] if args.include_host_tool else [],
                  "packages": [package_evidence(file) for file in sorted(output.glob(f"*.{version}.*nupkg"))]}
    if args.verify_repeat_content:
        report["repeatedPackageContent"] = verify_repeat_content(args, version, output, temporary_parent)
    report["sourceAtCompletion"] = source_identity()
    report["sourceRevisionStable"] = report["sourceAtCompletion"]["sourceRevision"] == source["sourceRevision"]
    report["sourceBytesStable"] = report["sourceAtCompletion"] == source
    (output / "package-verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
