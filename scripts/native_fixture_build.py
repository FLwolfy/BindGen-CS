"""Repository-only native fixture compilation; never imported by shipped products."""
from __future__ import annotations

import hashlib
import os
from pathlib import Path
import shutil


def msvc_sdk_inputs(environment: dict[str, str]) -> tuple[list[Path], list[Path]]:
    """Use only the SDK/tool paths explicitly exported by the caller's developer shell."""
    values = {key.upper(): value for key, value in environment.items()}
    required = ("VCTOOLSINSTALLDIR", "WINDOWSSDKDIR", "WINDOWSSDKVERSION", "VSCMD_ARG_TGT_ARCH")
    if any(not values.get(key) for key in required):
        raise RuntimeError("External MSVC fixtures require explicit developer-shell tool/SDK paths and target architecture.")
    architecture = values["VSCMD_ARG_TGT_ARCH"]
    if architecture not in ("x86", "x64", "arm", "arm64"):
        raise RuntimeError("The explicit developer-shell target architecture is unsupported by this fixture.")
    vc, sdk = Path(values["VCTOOLSINSTALLDIR"]), Path(values["WINDOWSSDKDIR"])
    version = values["WINDOWSSDKVERSION"].strip("/\\")
    if Path(version).name != version or not version:
        raise RuntimeError("The explicit Windows SDK version must be one path component.")
    includes = [vc / "include", *(sdk / "Include" / version / role for role in ("ucrt", "shared", "um", "winrt"))]
    libraries = [vc / "lib" / architecture, *(sdk / "Lib" / version / role / architecture for role in ("ucrt", "um"))]
    if any(not path.is_dir() for path in [*includes, *libraries]):
        raise FileNotFoundError("An explicitly selected native fixture SDK directory is missing.")
    return includes, libraries


def fixture_inputs(project: Path, generated: Path, sources: list[Path]) -> tuple[list[Path], list[Path]]:
    """Read exact owned source artifacts, retaining their independently verified hashes."""
    from acceptance_evidence import publication
    manifest = publication(generated)
    owned = [generated / item["relativePath"] for item in manifest["artifacts"]]
    native = [path for path in owned if path.suffix.lower() in (".c", ".cpp", ".cxx", ".cc")]
    includes = {project.resolve(), *(path.parent.resolve() for path in owned if path.suffix.lower() in (".h", ".hpp", ".hxx"))}
    for path in owned:
        relative = path.relative_to(generated).as_posix()
        if "/native/" in relative:
            includes.add((generated / relative.split("/native/", 1)[0] / "native/include").resolve())
    return list(dict.fromkeys([path.resolve() for path in sources] + native)), sorted(includes, key=str)


def commands(compiler: str, sources: list[Path], includes: list[Path], output: Path,
             *, defines: tuple[str, ...] = (), libraries: tuple[str, ...] = (), exports: tuple[str, ...] = (), library_directories: tuple[Path, ...] = ()) -> list[list[str]]:
    """Construct finite host-fixture commands with explicit language per translation unit."""
    if not sources:
        raise ValueError("A native fixture needs at least one explicitly supplied or generated source.")
    msvc = Path(compiler).name.lower() in ("cl", "cl.exe", "clang-cl", "clang-cl.exe")
    plans, objects = [], []
    for index, source in enumerate(sources):
        cpp = source.suffix.lower() != ".c"
        obj = output.parent / (str(index) + (".obj" if msvc else ".o"))
        objects.append(str(obj))
        if msvc:
            plan = [compiler, "/nologo", "/c", "/std:c++latest" if cpp else "/std:c17", "/EHsc", "/MD",
                    *["/I" + str(path) for path in includes], *["/D" + value for value in defines], str(source), "/Fo" + str(obj)]
        else:
            plan = [compiler, "-c", "-x", "c++" if cpp else "c", "-std=c++23" if cpp else "-std=c17", "-fPIC",
                    *["-I" + str(path) for path in includes], *["-D" + value for value in defines], str(source), "-o", str(obj)]
        plans.append(plan)
    plans.append([compiler, "/nologo", "/LD", *objects, *libraries, "/link", *["/LIBPATH:" + str(path) for path in library_directories], *["/EXPORT:" + name for name in exports], "/IMPLIB:" + str(output.with_suffix(".lib")), "/OUT:" + str(output)] if msvc else
                 [compiler, "-dynamiclib" if output.suffix == ".dylib" else "-shared", *objects, *libraries, "-o", str(output)])
    return plans


def compile_shared(project: Path, generated: Path, output: Path, sources: list[Path], run, evidence: Path,
                   *, compiler: str | None = None, defines: tuple[str, ...] = (), libraries: tuple[str, ...] = (), exports: tuple[str, ...] = (), include_directories: tuple[Path, ...] = ()) -> Path:
    """Build explicit acceptance fixture inputs outside the generator publication."""
    native, includes = fixture_inputs(project, generated, sources)
    includes = list(dict.fromkeys(includes + [path.resolve() for path in include_directories]))
    chosen = compiler or os.environ.get("BGCS_CPP2C_CXX") or os.environ.get("CXX")
    if not chosen:
        chosen = shutil.which("cl") if os.name == "nt" else shutil.which("c++")
    if not chosen:
        raise FileNotFoundError("The caller must provide its native fixture compiler.")
    library_directories = []
    if Path(chosen).name.lower() in ("cl", "cl.exe", "clang-cl", "clang-cl.exe"):
        sdk_includes, library_directories = msvc_sdk_inputs(dict(os.environ))
        includes.extend(sdk_includes)
    output = output.resolve()
    if output.is_relative_to(project.resolve()) or output.is_relative_to(generated.resolve()):
        raise ValueError("Fixture binary/object outputs must stay outside source and generated-input roots.")
    before = {path: hashlib.sha256(path.read_bytes()).digest() for path in native}
    output.parent.mkdir(parents=True, exist_ok=True)
    for index, command in enumerate(commands(chosen, native, includes, output, defines=defines, libraries=libraries, exports=exports, library_directories=tuple(library_directories))):
        run(command, output.parent, evidence, output.parent.name + "-native-" + str(index))
    from acceptance_evidence import publication
    publication(generated)
    if not output.is_file() or before != {path: hashlib.sha256(path.read_bytes()).digest() for path in native}:
        raise RuntimeError("Native fixture inputs changed or its explicit output was not produced.")
    return output
