"""Repository-owned optional browser regression; no compiler is called by BGCS."""
from __future__ import annotations

import json
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET

import cpp_acceptance_fixture as fixture
from acceptance_evidence import digest, publication


def execute(root: Path, work: Path, args, dotnet: str, environment: dict, browser: str, helpers, report: dict) -> None:
    """Reject unestablished calli execution, then invoke admitted imports in a trimmed consumer."""
    source = root / "tests/wasm/TypedBridge"
    shutil.copytree(source, work / "Native")
    shutil.copytree(root / "tests/wasm/wwwroot", work / "wwwroot")
    shutil.copyfile(source / "Consumer.project.xml", work / "Consumer.csproj")
    if args.tool_path:
        tool, runtime = args.tool_path.resolve(), args.runtime_path.resolve()
        if not tool.is_file() or not runtime.is_file():
            raise RuntimeError("The explicit generator and consumer Runtime must both exist.")
    else:
        if not args.no_build:
            projects = json.loads((root / "distribution/manifests/product-packages.json").read_text())["assemblyProjects"]
            for name in ("BGCS.Tool", "BGCS.Runtime", "BGCS.Frontend.Clang.Worker", "BGCS.Extension.BrowserWasm"):
                helpers.build_project(dotnet, root / projects[name] / (name + ".csproj"), work,
                    work / (name + "-build.log"), environment, args.configuration)
        tool = fixture.stage_repository_generator(root, work, args.configuration)
        runtime = root / "src/BGCS.Runtime/bin" / args.configuration / "net10.0/BGCS.Runtime.dll"
    shutil.copyfile(runtime, work / "BGCS.Runtime.dll")
    report.update(fixture.execution_identity(root, tool, runtime))
    plugin = fixture.stage_optional_extension(root, work, args.configuration, browser=True)
    report["optionalExtensionManifestSha256"] = digest(plugin)
    raw = helpers.run([dotnet, "msbuild", "Consumer.csproj", "-nologo", "-nodeReuse:false",
        "-getProperty:EmscriptenSdkToolsPath,EmscriptenCacheSdkCacheDir"], work, work / "sdk-query.log", environment)
    sdk = json.loads(raw)["Properties"]
    sysroot = Path(sdk["EmscriptenCacheSdkCacheDir"]) / "sysroot"
    compiler = Path(sdk["EmscriptenSdkToolsPath"]) / "bin" / ("clang.exe" if __import__("os").name == "nt" else "clang")
    if not sysroot.is_dir() or not compiler.is_file():
        raise RuntimeError("The exact locked SDK needs its verified wasm-tools workload.")
    report["sdk"] = sdk
    project = ET.parse(work / "Consumer.csproj")
    native = ET.SubElement(project.getroot(), "ItemGroup")
    receipts = []
    for mode in fixture.BROWSER_MODES:
        # The WebAssembly SDK registers P/Invoke modules by NativeFileReference's file stem.
        config = {"schemaVersion": 2, "library": {"id": "typed_" + mode.lower(), "nativeName": "native",
            "bridgeExportPrefix": "typed_" + mode.lower()},
            "source": {"language": "c", "languageStandard": "c17", "headers": ["Native/native.h"],
                "includeDirectories": ["Native"], "parseMacros": False, "includeTransitivelyReferencedHeaders": False},
            "targets": [fixture.target_configuration(compiler, sysroot)],
            "api": {"namespace": "WasmAcceptance." + mode, "name": "BridgeApi", "runtime": "dotnet.mono-wasm",
                "importMode": mode[0].lower() + mode[1:]},
            "semantics": {"callbackParameters": [{"function": "Invoke", "parameter": "callback", "lifetime": "call",
                "retained": False, "threading": "callerThread", "userDataParameter": "state", "callableUserDataIndex": 1}]},
            "extensions": {"plugins": [str(plugin)]}, "cache": {"enabled": False},
            "output": {"root": "Generated/" + mode}}
        request = work / (mode + ".json")
        request.write_text(json.dumps(config, indent=2), encoding="utf-8")
        helpers.run([*fixture.tool_command(dotnet, tool), "generate", str(request)], work,
            work / (mode + "-generation.log"), environment)
        output = work / "Generated" / mode
        owner = publication(output, config["library"]["id"])
        manifests = [output / artifact["relativePath"] for artifact in owner["artifacts"]
            if artifact["relativePath"].endswith("/metadata/native-source.manifest.json")]
        if len(manifests) != 1:
            raise RuntimeError("Each import must emit one current typed native source manifest.")
        path = manifests[0]
        manifest = json.loads(path.read_text(encoding="utf-8"))
        requirements = manifest.get("runtimeRequirements", {})
        if (manifest.get("schemaVersion") != 2 or manifest.get("targetId") != "emscripten-wasm32-emscripten"
                or requirements.get("execution", {}).get("runtimeId") != "dotnet.mono-wasm"
                or not manifest.get("exportBindings")):
            raise RuntimeError("The source manifest did not bind the exact optional browser execution contract.")
        for artifact in manifest["sources"]:
            ET.SubElement(native, "NativeFileReference", Include=(output / artifact["relativePath"]).relative_to(work).as_posix())
        receipts.append({"mode": mode, "manifestSha256": digest(path), "sources": len(manifest["sources"]),
            "exportCount": len(manifest["exportBindings"]), "nativePointerSize": requirements["execution"]["pointerSize"]})
    # Refusal must be actionable and preserve a previously published complete output.
    previous = {path.relative_to(output).as_posix(): digest(path) for path in output.rglob("*") if path.is_file()}
    config["api"]["importMode"] = "functionTable"
    request = work / "unsupported-function-table.json"
    request.write_text(json.dumps(config, indent=2), encoding="utf-8")
    refusal = work / "unsupported-function-table.log"
    try:
        helpers.run([*fixture.tool_command(dotnet, tool), "generate", str(request)], work, refusal, environment)
    except RuntimeError:
        details = refusal.read_text(encoding="utf-8", errors="replace")
        if not all(mode in details for mode in ("FunctionTable", "LibraryImport", "DllImport")):
            raise RuntimeError("FunctionTable refusal did not identify the supported browser alternatives.")
    else:
        raise RuntimeError("The browser extension admitted an unestablished FunctionTable execution contract.")
    if previous != {path.relative_to(output).as_posix(): digest(path) for path in output.rglob("*") if path.is_file()}:
        raise RuntimeError("An unsupported import request changed previously published output.")
    report["unsupportedFunctionTable"] = {"rejected": True, "previousOutputPreserved": True}
    project.write(work / "Consumer.csproj", encoding="utf-8", xml_declaration=True)
    runners = []
    for mode in fixture.BROWSER_MODES:
        factory = mode + ".BridgeApi.Create()"
        runners.append((source / "Invocation.cs.in").read_text(encoding="utf-8").replace("__MODE__", mode).replace("__FACTORY__", factory))
    (work / "Program.cs").write_text((source / "Program.cs.in").read_text(encoding="utf-8")
        .replace("__RUNNERS__", "\n".join(runners)), encoding="utf-8")
    if args.inject_native_error:
        path = work / "Native/native.c"
        path.write_text(path.read_text(encoding="utf-8").replace("left + right", "left + right + 1"), encoding="utf-8")
    helpers.run([dotnet, "publish", "Consumer.csproj", "-c", "Release", "-o", str(work / "published"),
        "--disable-build-servers", "-m:1", "-nodeReuse:false", "-p:RunAOTCompilation=" + str(args.aot).lower(),
        "-p:PublishTrimmed=true"], work, work / "publish.log", environment)
    result = helpers.invoke_browser(browser, work / "published/wwwroot", work)
    expected = {mode + ":" + case for mode in fixture.BROWSER_MODES for case in
        ("scalar", "aggregate-input-output", "reverse-aggregate-callback-user-data", "anonymous-storage",
         "uint64-scalar-mixed-signatures", "uint64-callback", "closed-context")}
    checks = result.get("checks", [])
    if (not result.get("success") or result.get("pointerSize") != 4
            or len(checks) != len(expected) or set(checks) != expected):
        report["invocation"] = result
        raise RuntimeError("The real browser omitted or failed a required typed aggregate/callback invocation.")
    report.update(success=True, scope="optional-browser-typed-pointer-bridge-regression",
        externalConsumerBuild=True, modes=list(fixture.BROWSER_MODES), trimmed=True,
        sourceManifests=receipts, invocation=result, requiredChecks=sorted(expected))
