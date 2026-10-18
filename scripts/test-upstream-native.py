#!/usr/bin/env python3
"""Build and invoke locked upstream implementations; optionally consume generated bindings.

The independent native probe is a comparison oracle, not proof that generated C#
calls work. A receipt reports those two stages separately.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import uuid
import xml.etree.ElementTree as ET

from acceptance_evidence import digest, publication, source_identity
from native_fixture_build import compile_shared
from corpus import load_lock, verify
from corpus_policy import selection_policy
from cpp_acceptance_fixture import execution_identity, stage_repository_generator, tool_command

ROOT = Path(__file__).resolve().parents[1]
LIBRARIES = ("miniaudio", "sdl3", "cimgui", "cimguizmo", "bgfx", "bimg")


def consumer_source(library: str, source: str) -> str:
    configuration = json.loads((ROOT / "tests/corpus" / (library + ".bindgen.json")).read_text(encoding="utf-8"))
    api = configuration["api"]
    def signature(name: str) -> tuple[str, list[str]]:
        candidates = re.findall(r"public\s+([A-Za-z0-9_@.:*]+)\s+" + re.escape(name) + r"\(([^()]*)\)", source)
        if not candidates:
            raise RuntimeError("The generated raw corpus signature is missing: " + name)
        result, parameters = candidates[0]
        return result, [parameter.strip().rsplit(" ", 1)[0] for parameter in parameters.split(",") if parameter.strip()]
    prefix = "using System;\nusing System.Runtime.InteropServices;\nusing System.Text.Json;\nusing " + api["namespace"] + ";\n"
    prefix += "public static unsafe class Program { public static void Main(string[] args) {\nusing var api = " + api["name"] + ".Load(args[0]);\n"
    finish = "\n} }\n"
    if library == "miniaudio":
        return prefix + """uint major = 0, minor = 0, patch = 0;
api.Raw.CorpusVersion(&major, &minor, &patch);
Console.WriteLine(JsonSerializer.Serialize(new { major, minor, patch, version = Text(api.Raw.CorpusVersionString()) }));
""" + finish.replace("} }", "} private static string Text(void* value) => Marshal.PtrToStringUTF8((nint)value) ?? throw new InvalidOperationException(\"The upstream returned a null version string.\"); }")
    if library == "sdl3":
        return prefix + """Console.WriteLine(JsonSerializer.Serialize(new { version = api.Raw.CorpusVersion(), absolute = api.Raw.CorpusAbsolute(-37),
revision = Marshal.PtrToStringUTF8((nint)api.Raw.CorpusRevision()) ?? throw new InvalidOperationException("The upstream returned a null revision.") }));
""" + finish
    if library == "cimgui":
        _, parameters = signature("CorpusBufferAppend")
        return prefix + """var buffer = api.Raw.CorpusBufferCreate();
if (buffer == null) throw new InvalidOperationException("The upstream returned a null text buffer.");
try {
byte[] text = "BGCS native corpus\\0"u8.ToArray();
fixed (byte* pointer = text) api.Raw.CorpusBufferAppend(buffer, (CHAR_POINTER)pointer, (CHAR_POINTER)(pointer + text.Length - 1));
int bufferSize = api.Raw.CorpusBufferSize(buffer);
bool bufferMatches = Marshal.PtrToStringUTF8((nint)api.Raw.CorpusBufferText(buffer)) == "BGCS native corpus";
string version = Marshal.PtrToStringUTF8((nint)api.Raw.CorpusVersion()) ?? throw new InvalidOperationException("The upstream returned a null version.");
Console.WriteLine(JsonSerializer.Serialize(new { version, bufferSize, bufferMatches }));
} finally { api.Raw.CorpusBufferDestroy(buffer); }
""".replace("CHAR_POINTER", parameters[1]) + finish
    if library == "cimguizmo":
        return prefix + """float* translation = stackalloc float[] { 11, 13, 17 };
float* rotation = stackalloc float[] { 0, 0, 0 };
float* scale = stackalloc float[] { 2, 3, 4 };
float* matrix = stackalloc float[16];
float* actualTranslation = stackalloc float[3];
float* actualRotation = stackalloc float[3];
float* actualScale = stackalloc float[3];
api.Raw.CorpusRecompose(translation, rotation, scale, matrix);
api.Raw.CorpusDecompose(matrix, actualTranslation, actualRotation, actualScale);
Console.WriteLine(JsonSerializer.Serialize(new { translation = new[] { actualTranslation[0], actualTranslation[1], actualTranslation[2] },
scale = new[] { actualScale[0], actualScale[1], actualScale[2] }, matrixTranslation = new[] { matrix[12], matrix[13], matrix[14] } }));
""" + finish
    if library == "bimg":
        _, parameters = signature("CorpusFormat")
        _, result = signature("CorpusName")
        if not parameters[1].endswith("*") or not parameters[2].endswith("**") or not result[1].endswith("**"):
            raise RuntimeError("The raw C++ facade lost its explicit result/error pointer contract.")
        return prefix + """void Check(int status, ERROR_TYPE* nativeError) {
if (status == 0 && nativeError == null) return;
try { throw new InvalidOperationException(nativeError == null ? "Native bridge failed without an error object."
    : Marshal.PtrToStringUTF8((nint)api.Raw.BimgErrorMessage(nativeError)) ?? "Native bridge error."); }
finally { if (nativeError != null) api.Raw.BimgErrorRelease(nativeError); }
}
byte[] name = "RGBA8\\0"u8.ToArray();
uint* pixels = stackalloc uint[4];
FORMAT_TYPE format = default;
ERROR_TYPE* error = null;
NAME_TYPE nativeName = null;
fixed (byte* pointer = name) {
Check(api.Raw.CorpusFormat((CHAR_POINTER)pointer, &format, &error), error);
Check(api.Raw.CorpusSolid(pixels, 2, 2, 0xaabbccddU, &error), error);
Check(api.Raw.CorpusName(format, &nativeName, &error), error);
Console.WriteLine(JsonSerializer.Serialize(new { format = Marshal.PtrToStringUTF8((nint)nativeName),
bitsPerPixel = api.CorpusBits(format), mips = api.CorpusMips(format, 8, 4, 1), pixel = pixels[3] }));
}
""".replace("CHAR_POINTER", parameters[0]).replace("FORMAT_TYPE", parameters[1][:-1]) \
    .replace("ERROR_TYPE", parameters[2][:-2]).replace("NAME_TYPE", result[1][:-1]) + finish
    if library == "bgfx":
        layout, _ = signature("CorpusLayoutBegin")
        _, add = signature("CorpusLayoutAdd")
        _, calculate = signature("CorpusTextureSize")
        if not layout.endswith("*") or not calculate[0].endswith("*"):
            raise RuntimeError("The generated raw layout and texture APIs lost their native record pointers.")
        # Enum members come from the generated enum, rather than copying their native
        # integer values into the consumer. Signature discovery only names the emitted
        # storage types and handles the generator's deterministic collision suffixes.
        _, begin = signature("CorpusLayoutBegin")
        body = layout[:-1] + " layout = default;\n" + calculate[0][:-1] + " texture = default;\n"
        body += "api.Raw.CorpusLayoutBegin(&layout, " + begin[1] + ".BGFX_RENDERER_TYPE_NOOP);\n"
        body += "api.Raw.CorpusLayoutAdd(&layout, " + add[1] + ".BGFX_ATTRIB_POSITION, 3, " + add[3] + ".BGFX_ATTRIB_TYPE_FLOAT, default, default);\n"
        body += "api.Raw.CorpusLayoutAdd(&layout, " + add[1] + ".BGFX_ATTRIB_TEXCOORD0, 2, " + add[3] + ".BGFX_ATTRIB_TYPE_FLOAT, default, default);\n"
        body += "api.Raw.CorpusLayoutEnd(&layout);\napi.Raw.CorpusTextureSize(&texture, 8, 4, 1, default, default, 1, " + calculate[-1] + ".BGFX_TEXTURE_FORMAT_RGBA8);\n"
        body += "Console.WriteLine(JsonSerializer.Serialize(new { stride = api.Raw.CorpusLayoutStride(&layout), texcoordOffset = api.Raw.CorpusLayoutOffset(&layout, " + add[1] + ".BGFX_ATTRIB_TEXCOORD0),\n"
        body += "sevenVertices = api.Raw.CorpusLayoutSize(&layout, 7), textureBytes = texture.storageSize, renderer = Marshal.PtrToStringUTF8((nint)api.Raw.CorpusRendererName(" + begin[1] + ".BGFX_RENDERER_TYPE_NOOP)) }));\n"
        return prefix + body + finish
    raise ValueError("An upstream managed consumer was not registered: " + library)


def invoke_generated(library: str, generated: Path, binary: Path, runtime: Path, dotnet: str,
                     work: Path, logs: Path, native_result: dict, helper, configuration: str) -> dict:
    manifest = publication(generated, library)
    sources = [generated / item["relativePath"] for item in manifest["artifacts"] if item["mediaType"] == "text/x-csharp"]
    if len(sources) != 1 or sources[0].name != "Bindings.cs":
        raise RuntimeError("The native corpus consumer requires its complete single-file generated binding.")
    consumer = work / (library + "-managed")
    consumer.mkdir()
    source = consumer_source(library, sources[0].read_text(encoding="utf-8"))
    (consumer / "Program.cs").write_text(source, encoding="utf-8")
    (logs / "managed-consumer.cs").write_text(source, encoding="utf-8")
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    properties = ET.SubElement(project, "PropertyGroup")
    for name, value in {"OutputType": "Exe", "TargetFramework": "net10.0", "AllowUnsafeBlocks": "true", "Nullable": "enable",
                        "ImplicitUsings": "disable", "TreatWarningsAsErrors": "true", "UseSharedCompilation": "false"}.items():
        ET.SubElement(properties, name).text = value
    items = ET.SubElement(project, "ItemGroup")
    ET.SubElement(items, "Compile", Include=str(sources[0]))
    ET.SubElement(ET.SubElement(items, "Reference", Include="BGCS.Runtime"), "HintPath").text = str(runtime.resolve())
    ET.ElementTree(project).write(consumer / "Consumer.csproj", encoding="utf-8", xml_declaration=True)
    environment = {"DOTNET_ROOT": str(Path(dotnet).parent), "DOTNET_HOST_PATH": dotnet}
    helper.run_process([dotnet, "build", "Consumer.csproj", "-c", configuration, "-m:1", "-nr:false"], consumer, logs, "managed-consumer-build", environment_overrides=environment)
    result = json.loads(helper.run_process([dotnet, str(consumer / "bin" / configuration / "net10.0/Consumer.dll"), str(binary)],
                                         consumer, logs, "generated-bindings-invocation", environment_overrides=environment))
    if result != native_result:
        raise RuntimeError("The generated C# result differs from the independent upstream native probe: " + repr(result) + " != " + repr(native_result))
    return {"result": result, "managedSourceSha256": digest(sources[0]), "runtimeSha256": digest(runtime),
            "consumerSha256": digest(consumer / "bin" / configuration / "net10.0/Consumer.dll"), "matchesIndependentNativeProbe": True}


def resolve_cmake(explicit: str | None) -> Path:
    candidate = explicit or shutil.which("cmake")
    if candidate:
        path = Path(candidate).resolve()
        if not path.is_file():
            raise RuntimeError("The explicitly selected CMake executable does not exist: " + str(path))
        return path
    if os.name == "nt":
        installer = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft Visual Studio/Installer/vswhere.exe"
        if installer.is_file():
            matches = subprocess.check_output([str(installer), "-latest", "-products", "*", "-find",
                "Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe"], text=True, timeout=30).splitlines()
            if len(matches) == 1 and Path(matches[0]).is_file():
                return Path(matches[0]).resolve()
    raise RuntimeError("CMake is required for actual upstream builds; install it or pass --cmake.")


def generated_native_inputs(generated: Path, library: str, required: bool = False) -> tuple[list[str], list[str]]:
    manifest = publication(generated, library)
    sources = [(generated / item["relativePath"]).as_posix() for item in manifest["artifacts"]
               if item["mediaType"] in ("text/x-c", "text/x-c++")
               and Path(item["relativePath"]).suffix in (".c", ".cpp", ".cc", ".cxx")]
    directories = sorted({(generated / item["relativePath"]).parent.as_posix() for item in manifest["artifacts"]
                          if item["mediaType"] in ("text/x-c", "text/x-c++", "text/x-c-header", "text/x-c++-header")
                          and Path(item["relativePath"]).suffix in (".h", ".hpp", ".hxx")})
    if required and (not sources or not directories):
        raise RuntimeError("The C++ upstream invocation requires its complete validated generated bridge sources and headers.")
    return sources, directories


def verify_missing_upstream_definition(root: Path, generated: Path, assets: dict, dotnet: str, tool: Path,
                                       runtime: Path, work: Path, logs: Path, policy: dict, helper) -> dict:
    """Select the disclosed real declaration and require transactional public-build link failure."""
    from importlib.util import module_from_spec, spec_from_file_location
    spec = spec_from_file_location("negative_corpus_configuration", ROOT / "scripts/test-corpus.py")
    configuration = module_from_spec(spec)
    spec.loader.exec_module(configuration)
    limitation, = policy["libraries"]["bimg"]["upstreamLimitations"]
    static_libraries = [Path(value) for value in assets["upstreamStaticLibraries"]]
    if not static_libraries or not all(path.is_file() for path in static_libraries):
        raise RuntimeError("The limitation probe requires the real just-built upstream static libraries.")
    previous = work / "bimg-negative-publication"
    shutil.copytree(generated, previous)
    publication(previous, "bimg")
    before = {path.relative_to(previous).as_posix(): digest(path) for path in previous.rglob("*") if path.is_file()}
    headers = Path(os.environ.get("BGCS_REAL_LIBRARY_ROOT", ROOT / "artifacts/corpus/headers")).resolve()
    request = configuration.configuration("bimg", headers, previous, compiler_override=assets["cppCompiler"])
    request["source"]["selection"]["includeDeclarations"] = [limitation["sourceSignature"]]
    request["source"]["selection"]["excludeDeclarations"] = []
    request["targets"] = [{"id": "host", "settings": {"compilerPath": assets["cppCompiler"]}}]
    request.setdefault("api", {})["projectionInputs"] = {"references": [str(runtime.resolve())]}
    request["cache"] = {"enabled": False}
    candidate = work / "bimg-negative-generated"
    request["output"]["root"] = str(candidate)
    libraries = [str(path) for path in static_libraries]
    if os.name == "nt": libraries.append("psapi.lib")
    else:
        libraries += ["-lpthread", "-lm"]
        if assets["system"] == "Linux": libraries.append("-ldl")
    config = work / "bimg-negative.bindgen.json"
    config.write_text(json.dumps(request, indent=2) + "\n", encoding="utf-8")
    name = "missing-definition-negative-build"
    helper.run_process([*tool_command(dotnet, tool), "generate", str(config), "--json"], work, logs, "missing-definition-generation",
                       environment_overrides={"DOTNET_ROOT": str(Path(dotnet).parent)}, separate_stderr=True)
    def native_run(arguments, directory, evidence, label):
        return helper.run_process(arguments, directory, evidence, label, environment_overrides={"DOTNET_ROOT": str(Path(dotnet).parent)})
    _, includes = generated_native_inputs(candidate, "bimg", True)
    try:
        compile_shared(work, candidate, logs / "external-native-negative" / helper.native_filename(), [], native_run, logs,
                       compiler=assets["cppCompiler"], libraries=tuple(libraries), include_directories=tuple(Path(value) for value in [*includes, *request["source"].get("includeDirectories", []), *request["source"].get("systemIncludeDirectories", [])]))
    except RuntimeError:
        diagnostics = "\n".join(path.read_text(encoding="utf-8-sig", errors="replace") for path in logs.glob("external-native-negative-native-*.log"))
        if "imageConvert" not in diagnostics or not re.search(r"unresolved|undefined|LNK2019|LNK1120|Undefined symbols", diagnostics, re.IGNORECASE):
            raise RuntimeError("The external native negative failed before the required unresolved-symbol witness: " + diagnostics[-2000:])
    else:
        raise RuntimeError("The external fixture link incorrectly accepted the upstream's undefined exact overload.")
    publication(previous, "bimg")
    after = {path.relative_to(previous).as_posix(): digest(path) for path in previous.rglob("*") if path.is_file()}
    if after != before:
        raise RuntimeError("The failed upstream link replaced or modified its prior complete owned publication.")
    return {"limitationId": limitation["id"], "sourceSignature": limitation["sourceSignature"],
            "nativeLinkRejected": True, "priorPublicationPreserved": True, "priorFileCount": len(before),
            "staticLibraries": [{"path": str(path), "sha256": digest(path)} for path in static_libraries],
            "diagnostics": diagnostics, "executionIdentity": execution_identity(root, tool, runtime)}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--library", choices=LIBRARIES, action="append")
    parser.add_argument("--corpus-root", type=Path, default=ROOT / "artifacts/real-library-native-corpus")
    parser.add_argument("--cmake")
    parser.add_argument("--configuration", default="Release", choices=("Debug", "Release"))
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/acceptance/upstream-native.json")
    parser.add_argument("--generated-root", type=Path, help="Parent containing one owned publication directory per product.")
    parser.add_argument("--generation-receipt", type=Path, action="append", help="Current test-corpus.py receipt containing exact owned output roots.")
    parser.add_argument("--dotnet", help="Explicit .NET host for generated C# consumer execution.")
    parser.add_argument("--runtime", type=Path, help="Exact BGCS.Runtime assembly referenced by generated C# consumers.")
    parser.add_argument("--tool-path", type=Path, help="Closed CLI execution closure for the required upstream-limitation negative build.")
    args = parser.parse_args()
    if args.generated_root and args.generation_receipt:
        parser.error("Select --generated-root or --generation-receipt, not both.")
    consume_bindings = args.generated_root is not None or bool(args.generation_receipt)
    if consume_bindings and (not args.dotnet or not args.runtime):
        parser.error("Generated binding consumption requires --dotnet and --runtime.")
    generated_roots = {}
    requested_libraries = args.library or LIBRARIES
    if args.generated_root:
        generated_roots = {library: args.generated_root.resolve() / library for library in args.library or LIBRARIES}
    generation_receipts = []
    receipt_libraries = set()
    for receipt in args.generation_receipt or []:
        data = json.loads(receipt.read_text(encoding="utf-8"))
        generation_receipts.append({"path": str(receipt.resolve()), "sha256": digest(receipt), "executionIdentity": data["executionIdentity"]})
        for item in data["libraries"]:
            library = item["libraryId"]
            if library in receipt_libraries or library not in LIBRARIES:
                raise RuntimeError("The generation receipts contain ambiguous or unknown corpus products.")
            receipt_libraries.add(library)
            if library not in requested_libraries:
                continue
            if not item.get("generated") or not item.get("managedCompiled"):
                raise RuntimeError("The upstream invocation requires successful generation and managed compilation: " + library)
            generated_roots[library] = Path(item["outputRoot"]).resolve()
    if consume_bindings and any(library not in generated_roots for library in args.library or LIBRARIES):
        raise RuntimeError("The invocation inputs omit a requested product's complete generated binding.")
    spec = importlib.util.spec_from_file_location("native_corpus_process_helpers", ROOT / "eng/verify-packages.py")
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    evidence = ROOT / "artifacts/corpus-native-acceptance" / uuid.uuid4().hex
    evidence.mkdir(parents=True)
    lock_path = ROOT / "tests/corpus/native-corpus.lock.json"
    report = {"schemaVersion": 1, "success": False, "scope": "upstream-native-build-and-independent-probe",
              "source": source_identity(ROOT), "nativeCorpusLockSha256": digest(lock_path), "evidence": str(evidence),
              "generatedBindingsRequired": consume_bindings, "generationReceipts": generation_receipts, "bindingsInvoked": False, "libraries": []}
    try:
        minimum_free = int(os.environ.get("BGCS_MINIMUM_FREE_BYTES", str(2 * 1024 ** 3)))
        if minimum_free <= 0 or shutil.disk_usage(ROOT).free < minimum_free:
            raise RuntimeError("Upstream compilation requires its declared positive free-space floor.")
        corpus = args.corpus_root.resolve()
        lock = load_lock(lock_path)
        report["repositories"] = verify(corpus, lock)
        report["selectedApiPolicy"] = selection_policy(ROOT, lock, corpus)
        cmake = resolve_cmake(args.cmake)
        report["cmake"] = {"path": str(cmake), "sha256": digest(cmake)}
        report["fixtureInputs"] = {path.relative_to(ROOT).as_posix(): digest(path)
                                   for path in sorted((ROOT / "tests/corpus/native").glob("*")) if path.is_file()}
        temporary = ROOT / "artifacts/temporary"
        temporary.mkdir(parents=True, exist_ok=True)
        if not temporary.resolve().is_relative_to(ROOT.resolve()):
            raise RuntimeError("The disposable corpus build directory escaped the workspace through a link.")
        with tempfile.TemporaryDirectory(prefix="upstream-native-", dir=temporary) as directory:
            work = Path(directory)
            tool = None
            if consume_bindings and "bimg" in requested_libraries:
                tool = args.tool_path.resolve() if args.tool_path else stage_repository_generator(ROOT, work, args.configuration)
            for library in args.library or LIBRARIES:
                item = {"libraryId": library, "success": False, "nativeLinked": False, "nativeProbeInvoked": False,
                        "bindingsInvoked": False, "buildProfile": "upstream-noop-renderer" if library == "bgfx" else "upstream-library"}
                report["libraries"].append(item)
                logs = evidence / library
                logs.mkdir()
                build = work / library
                build.mkdir()
                def run(arguments, name, **options):
                    return helper.run_process(arguments, work, logs, name, timeout=1800, **options)
                try:
                    configure = [str(cmake), "-S", str(ROOT / "tests/corpus/native"), "-B", str(build),
                                 "-DBGCS_CORPUS_ROOT:PATH=" + str(corpus), "-DBGCS_CORPUS_LIBRARY=" + library,
                                 "-DCMAKE_BUILD_TYPE=" + args.configuration]
                    if consume_bindings:
                        sources, directories = generated_native_inputs(generated_roots[library], library, library == "bimg")
                        if sources:
                            prefix = "BGCS_BRIDGE" if library == "bimg" else "BGCS_ADDITIONAL"
                            configure += ["-D" + prefix + "_SOURCES=" + ";".join(sources),
                                          "-D" + prefix + "_INCLUDE_DIRECTORIES=" + ";".join(directories)]
                        item["generatedNativeInputs"] = [{"path": source, "sha256": digest(Path(source))} for source in sources]
                    run(configure, "configure")
                    run([str(cmake), "--build", str(build), "--config", args.configuration, "--parallel", "2"], "build")
                    assets = json.loads((build / ("native-assets-" + args.configuration + ".json")).read_text(encoding="utf-8"))
                    binary, probe = Path(assets["nativeBinary"]), Path(assets["probe"])
                    item["compilerFacts"] = {name: assets[name] for name in ("cCompiler", "cppCompiler", "cppCompilerVersion", "system", "processor")}
                    item["compilerFacts"]["cCompilerSha256"] = digest(Path(assets["cCompiler"]))
                    item["compilerFacts"]["cppCompilerSha256"] = digest(Path(assets["cppCompiler"]))
                    item["nativeLinked"] = binary.is_file() and probe.is_file()
                    if not item["nativeLinked"]:
                        raise RuntimeError("The upstream build did not produce its complete declared native outputs.")
                    item["nativeBinarySha256"], item["probeSha256"] = digest(binary), digest(probe)
                    native_result = json.loads(run([str(probe)], "native-probe", separate_stderr=True))
                    item["nativeResult"] = native_result
                    item["nativeProbeInvoked"] = True
                    if consume_bindings:
                        item["managedInvocation"] = invoke_generated(library, generated_roots[library], binary,
                            args.runtime, str(Path(args.dotnet).resolve()), work, logs, native_result, helper, args.configuration)
                        item["bindingsInvoked"] = True
                        if library == "bimg":
                            item["upstreamLimitationNegativeBuild"] = verify_missing_upstream_definition(ROOT, generated_roots[library],
                                assets, str(Path(args.dotnet).resolve()), tool, args.runtime, work, logs, report["selectedApiPolicy"], helper)
                    item["success"] = True
                except Exception as error:
                    item["failure"] = str(error)
                    print("[upstream-native] " + library + ": " + str(error), flush=True)
        report["repositoriesAfter"] = verify(corpus, lock)
        report["sourceAtCompletion"] = source_identity(ROOT)
        report["sourceBytesStable"] = report["sourceAtCompletion"] == report["source"]
        report["success"] = bool(report["libraries"]) and all(item["success"] for item in report["libraries"])
        report["bindingsInvoked"] = consume_bindings and all(item["bindingsInvoked"] for item in report["libraries"])
        if consume_bindings:
            report["scope"] = "upstream-native-build-independent-probe-and-generated-csharp-invocation"
        return 0 if report["success"] else 1
    finally:
        output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    raise SystemExit(main())
