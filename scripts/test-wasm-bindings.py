#!/usr/bin/env python3
"""Generate BGCS bindings and invoke the fixtures' native C and C++ APIs in a headless browser."""

import argparse
import functools
import http.server
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import threading
import time
import uuid
import urllib.request
import cpp_acceptance_fixture


ROOT = Path(__file__).resolve().parent.parent
MODES = cpp_acceptance_fixture.BROWSER_MODES


def stop_process(process):
    if process.poll() is not None:
        return
    if os.name == "nt":
        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                       creationflags=subprocess.CREATE_NO_WINDOW, timeout=30, check=False)
    else:
        import signal
        os.killpg(process.pid, signal.SIGKILL)
    process.wait(timeout=30)


def run(command, work, evidence, environment):
    with evidence.open("w", encoding="utf-8") as log:
        process = subprocess.Popen(command, cwd=work, env=environment, stdout=log,
                                   stderr=subprocess.STDOUT, start_new_session=os.name != "nt",
                                   creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
        try:
            code = process.wait(timeout=600)
        except BaseException:
            stop_process(process)
            raise
    if code != 0:
        raise RuntimeError(f"Command exited {code}; see {evidence}")
    # Keep the original bytes in the log; localized child tools may use a different code page.
    return evidence.read_text(encoding="utf-8-sig", errors="replace")


def find_browser(requested):
    candidates = [requested] if requested else [
        shutil.which("google-chrome"), shutil.which("chromium"), shutil.which("chromium-browser"),
        shutil.which("microsoft-edge"),
        str(Path(os.environ.get("PROGRAMFILES(X86)", "")) / "Microsoft/Edge/Application/msedge.exe"),
        str(Path(os.environ.get("PROGRAMFILES", "")) / "Google/Chrome/Application/chrome.exe"),
        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    ]
    for candidate in candidates:
        if candidate and Path(candidate).is_file():
            return str(Path(candidate).resolve())
    raise RuntimeError("A Chromium browser is required. Pass --browser with its executable path.")


def acceptance_environment(dotnet):
    # Environment dictionaries are case-sensitive even when MSBuild's Windows input is not.
    environment = {key: value for key, value in os.environ.items() if key.casefold() != "platform"}
    environment.update(DOTNET_ROOT=str(Path(dotnet).parent), DOTNET_HOST_PATH=dotnet,
                       DOTNET_CLI_UI_LANGUAGE="en")
    environment["DOTNET_ROOT_ARM64" if platform.machine().lower() in ("arm64", "aarch64") else "DOTNET_ROOT_X64"] = str(Path(dotnet).parent)
    for key in tuple(environment):
        if key.upper().startswith(("MSBUILD", "DOTNET_MSBUILD_SDK_RESOLVER_")):
            environment.pop(key)
    return environment


def build_project(dotnet, project, work, evidence, environment, configuration="Release"):
    run([dotnet, "build", str(project), "-c", configuration, "--disable-build-servers",
         "-m:1", "-nodeReuse:false"], work, evidence, environment)
    output = run([dotnet, "msbuild", str(project), "-nologo", "-nodeReuse:false",
                  "-p:Configuration=" + configuration, "-getProperty:TargetPath"],
                 work, evidence.with_suffix(".target-path.log"), environment).strip()
    target = Path(output)
    if not target.is_file():
        raise RuntimeError(f"MSBuild did not produce the reported target: {target}")
    return target


class ResultHandler(http.server.SimpleHTTPRequestHandler):
    def do_POST(self):
        if self.path != "/result?token=" + self.server.token:
            self.send_error(404)
            return
        length = int(self.headers.get("Content-Length", "0"))
        if not 0 < length <= 65536:
            self.send_error(413)
            return
        try:
            self.server.result = json.loads(self.rfile.read(length))
            self.send_response(204)
            self.end_headers()
        finally:
            self.server.finished.set()

    def log_message(self, format, *args):
        pass


def invoke_browser(browser, site, work, timeout=120):
    handler = functools.partial(ResultHandler, directory=str(site))
    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), handler)
    server.token = uuid.uuid4().hex
    server.result = None
    server.finished = threading.Event()
    server.daemon_threads = True
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    url = f"http://127.0.0.1:{server.server_port}/index.html?token={server.token}"
    command = [browser, "--headless=new", "--no-first-run", "--disable-extensions", "--mute-audio",
               "--enable-logging=stderr",
               "--disable-background-networking", "--disable-component-update", "--disable-sync",
               "--remote-debugging-port=0", "--remote-debugging-address=127.0.0.1",
               "--user-data-dir=" + str(work / "browser-profile"), url]
    # Edge's Windows compatibility launcher otherwise detaches from the owned process tree.
    if os.name == "nt" and Path(browser).stem.lower() == "msedge":
        command.insert(1, "--edge-skip-compat-layer-relaunch")
    # Linux CI uses an isolated test profile and may run as root in a container.
    if os.name != "nt" and hasattr(os, "geteuid") and os.geteuid() == 0:
        command.insert(1, "--no-sandbox")
    try:
        with (work / "browser.log").open("w", encoding="utf-8") as log:
            process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT,
                                       start_new_session=os.name != "nt",
                                       creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
            try:
                deadline = time.monotonic() + timeout
                while not server.finished.wait(0.25):
                    if process.poll() is not None or time.monotonic() >= deadline:
                        raise RuntimeError(f"Browser did not return a report (exit={process.poll()}); see browser.log.")
                port_file = work / "browser-profile/DevToolsActivePort"
                port = int(port_file.read_text(encoding="utf-8").splitlines()[0])
                if not 0 < port <= 65535:
                    raise RuntimeError("The owned browser reported an invalid debugging endpoint.")
                with urllib.request.urlopen(f"http://127.0.0.1:{port}/json/version", timeout=5) as response:
                    browser_version = json.load(response)
                if not browser_version.get("Browser") or not isinstance(server.result, dict):
                    raise RuntimeError("The browser did not provide its exact version and invocation report.")
                server.result["browserVersion"] = browser_version
                return server.result
            finally:
                stop_process(process)
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=5)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=os.environ.get("DOTNET_HOST_PATH") or shutil.which("dotnet"))
    parser.add_argument("--browser", help="Path to Chrome, Chromium, or Edge; otherwise discover locally.")
    parser.add_argument("--aot", action="store_true", help="Compile managed code ahead of time instead of interpreting it.")
    parser.add_argument("--targeted", action="store_true", help="Run the exact optional browser aggregate and callback regression.")
    parser.add_argument("--tool-path", type=Path, help="Use an already deployed/installed BGCS tool instead of building repository projects.")
    parser.add_argument("--runtime-path", type=Path, help="Use the matching BGCS.Runtime product assembly with --tool-path.")
    parser.add_argument("--local-checkpoint", action="store_true", help="Mark the selected tool/runtime as private repository binaries rather than installed products.")
    parser.add_argument("--no-build", action="store_true", help="Stage closed repository outputs without rebuilding shared projects.")
    parser.add_argument("--configuration", default="Release", choices=("Debug", "Release"))
    parser.add_argument("--keep-build-output", action="store_true", help="Keep large owned compiler/browser staging after evidence is written.")
    parser.add_argument("--inject-native-error", action="store_true", help="Corrupt a native return value to prove that invocation failures fail acceptance.")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/wasm-acceptance")
    args = parser.parse_args()
    if not args.dotnet or not Path(args.dotnet).is_file():
        parser.error("Pass --dotnet with a .NET 10 SDK host that has the wasm-tools workload.")
    if bool(args.tool_path) != bool(args.runtime_path):
        parser.error("--tool-path and --runtime-path must identify the same installed product set together.")
    if args.local_checkpoint and not args.tool_path:
        parser.error("--local-checkpoint requires an explicit private tool/runtime pair.")
    browser = find_browser(args.browser)
    dotnet = str(Path(args.dotnet).resolve())
    work = args.output.resolve() / uuid.uuid4().hex
    work.mkdir(parents=True)
    report_path = work / "report.json"
    print(f"Wasm acceptance evidence: {work}", flush=True)
    report = {"success": False, "target": "emscripten-wasm32-emscripten", "managedRuntime": "dotnet.mono-wasm",
              "host": platform.platform(), "browser": browser, "modes": list(MODES), "aot": args.aot,
              "installedProducts": bool(args.tool_path) and not args.local_checkpoint, "privateRepositoryCheckpoint": args.local_checkpoint,
              "injectedNativeError": args.inject_native_error,
              "wideEnumPresentation": "generator-configured UInt64 enum; friendly and raw native uint64 typedef round trips"}
    environment = acceptance_environment(dotnet)
    try:
        # Scope SDK selection to the fixture. Repository packaging still uses its own global.json.
        (work / "global.json").write_text(json.dumps({"sdk": {
            "version": "10.0.402", "rollForward": "latestPatch", "allowPrerelease": False}}), encoding="utf-8")
        report["dotnetSdkVersion"] = run([dotnet, "--version"], work, work / "dotnet-version.log", environment).strip()
        if report["dotnetSdkVersion"] != "10.0.402":
            raise RuntimeError("The browser gate requires an actual .NET 10 SDK.")
        report["workloadSet"] = run([dotnet, "workload", "list"], work, work / "workload-list.log", environment)
        if args.targeted:
            import sys
            import wasm_typed_bridge
            wasm_typed_bridge.execute(ROOT, work, args, dotnet, environment, browser, sys.modules[__name__], report)
            return 0
        fixture = ROOT / "tests/wasm"
        shutil.copytree(ROOT / "tests/fixtures/NativeApi", work / "Native")
        if args.inject_native_error:
            native_source = work / "Native/api.c"
            native_source.write_text(native_source.read_text(encoding="utf-8").replace("return left + right;", "return left + right + 1;"), encoding="utf-8")
        shutil.copytree(fixture / "wwwroot", work / "wwwroot")
        shutil.copyfile(fixture / "Consumer.project.xml", work / "Consumer.csproj")
        if args.tool_path:
            tool, runtime = args.tool_path.resolve(), args.runtime_path.resolve()
            if not tool.is_file() or not runtime.is_file():
                raise RuntimeError("The explicitly selected installed tool/runtime product is missing.")
        else:
            if not args.no_build:
                for name, directory in (("BGCS.Tool", "src/BGCS.Tool"), ("BGCS.Frontend.Clang.Worker", "extensions/frontends/BGCS.Frontend.Clang.Worker"), ("BGCS.Runtime", "src/BGCS.Runtime"), ("BGCS.Extension.BrowserWasm", "extensions/BGCS.Extension.BrowserWasm"), ("BGCS.Extension.NativeAot", "extensions/BGCS.Extension.NativeAot")):
                    build_project(dotnet, ROOT / directory / (name + ".csproj"), work, work / (name + "-build.log"), environment, args.configuration)
            tool = cpp_acceptance_fixture.stage_repository_generator(ROOT, work, args.configuration)
            runtime = ROOT / f"src/BGCS.Runtime/bin/{args.configuration}/net10.0/BGCS.Runtime.dll"
        report.update(cpp_acceptance_fixture.execution_identity(ROOT, tool, runtime))
        shutil.copyfile(runtime, work / "BGCS.Runtime.dll")
        raw = run([dotnet, "msbuild", "Consumer.csproj", "-nologo", "-nodeReuse:false",
                   "-getProperty:EmscriptenSdkToolsPath,EmscriptenCacheSdkCacheDir"],
                  work, work / "sdk-query.log", environment)
        sdk = json.loads(raw)["Properties"]
        sysroot = Path(sdk["EmscriptenCacheSdkCacheDir"]) / "sysroot"
        compiler = Path(sdk["EmscriptenSdkToolsPath"]) / "bin" / ("clang.exe" if os.name == "nt" else "clang")
        if not sysroot.is_dir() or not compiler.is_file():
            raise RuntimeError("The selected .NET 10 SDK does not have a complete wasm-tools workload.")
        report["sdk"] = sdk
        report["nativeCompilerVersion"] = run([str(compiler), "--version"], work, work / "native-compiler-version.log", environment).strip()
        bridge = cpp_acceptance_fixture.prepare(ROOT, work, dotnet, tool, compiler, environment, run, sysroot, args.configuration)
        project = work / "Consumer.csproj"
        project.write_text(project.read_text(encoding="utf-8").replace("__BRIDGE_SOURCE__", bridge["source"].relative_to(work).as_posix()), encoding="utf-8")
        for mode in MODES:
            config = cpp_acceptance_fixture.binding_configuration(mode, "WasmAcceptance", compiler, bridge, sysroot)
            config_path = work / (mode + ".json")
            config_path.write_text(json.dumps(config, indent=2), encoding="utf-8")
            run(cpp_acceptance_fixture.tool_command(dotnet, tool) + ["generate", str(config_path)], work,
                work / (mode + "-generation.log"), environment)
        report["generatedAbiManifests"] = [cpp_acceptance_fixture.generated_abi_identity(work, mode) for mode in MODES]
        if any(item["nativeTargetId"] != report["target"] or item["nativePointerSize"] != 4
               for item in report["generatedAbiManifests"]):
            raise RuntimeError("Every browser import mode must use the same resolved wasm32 target.")
        cpp_acceptance_fixture.write_consumer(ROOT, work, "WasmAcceptance", 4)
        run([dotnet, "publish", "Consumer.csproj", "-c", "Release", "-o", str(work / "published"),
             "--disable-build-servers", "-m:1", "-nodeReuse:false",
             "-p:RunAOTCompilation=" + str(args.aot).lower(), "-p:PublishTrimmed=true"],
            work, work / "publish.log", environment)
        result = invoke_browser(browser, work / "published/wwwroot", work)
        report["browserVersion"] = result.pop("browserVersion")
        report["invocation"] = result
        expected = cpp_acceptance_fixture.expected_checks(modes=MODES, tables=False)
        checks = result.get("checks", []) if result else []
        if not result or not result.get("success") or len(checks) != len(expected) or set(checks) != expected:
            raise RuntimeError("Native invocation failed or did not complete every required check.")
        if result.get("pointerSize") != 4:
            raise RuntimeError("The invocation did not execute in wasm32.")
        lifecycle = result.get("lifecycleChecks", [])
        expected_lifecycle = cpp_acceptance_fixture.expected_lifecycle_checks(modes=MODES)
        if len(lifecycle) != len(expected_lifecycle) or set(lifecycle) != expected_lifecycle:
            raise RuntimeError("The browser did not complete every nonblocking retained/async lifecycle obligation.")
        report["nonblockingLifecycle"] = {"success": True, "checks": lifecycle, "requiredCount": len(expected_lifecycle)}
        report["success"] = True
        print(f"Passed: {len(expected)} acceptance checks and {len(expected_lifecycle)} nonblocking lifecycle checks across {len(MODES)} import modes.")
    except Exception as error:
        report["error"] = str(error)
        print(f"FAILED: {error}", flush=True)
    finally:
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(f"Report: {report_path}", flush=True)
        if not args.keep_build_output:
            cpp_acceptance_fixture.cleanup_build_outputs(work)
    return 0 if report["success"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
