#!/usr/bin/env python3
"""Generate BGCS bindings and invoke the fixture's native C API in a headless browser."""

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


ROOT = Path(__file__).resolve().parent.parent
MODES = ("DllImport", "LibraryImport", "FunctionTable")


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


def invoke_browser(browser, site, work):
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
               "--disable-background-networking", "--disable-component-update", "--disable-sync",
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
                deadline = time.monotonic() + 120
                while not server.finished.wait(0.25):
                    if process.poll() is not None or time.monotonic() >= deadline:
                        raise RuntimeError(f"Browser did not return a report (exit={process.poll()}); see browser.log.")
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
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/wasm-acceptance")
    args = parser.parse_args()
    if not args.dotnet or not Path(args.dotnet).is_file():
        parser.error("Pass --dotnet with a .NET SDK host that has the .NET 9 wasm-tools workload.")
    browser = find_browser(args.browser)
    dotnet = str(Path(args.dotnet).resolve())
    work = args.output.resolve() / uuid.uuid4().hex
    work.mkdir(parents=True)
    report_path = work / "report.json"
    print(f"Wasm acceptance evidence: {work}", flush=True)
    report = {"success": False, "target": "emscripten-wasm32-emscripten",
              "host": platform.platform(), "browser": browser, "modes": list(MODES)}
    environment = dict(os.environ, DOTNET_ROOT=str(Path(dotnet).parent), DOTNET_HOST_PATH=dotnet,
                       DOTNET_CLI_UI_LANGUAGE="en")
    environment.pop("Platform", None)
    try:
        # Scope SDK selection to the fixture. Repository packaging still uses its own global.json.
        (work / "global.json").write_text(json.dumps({"sdk": {
            "version": "9.0.100", "rollForward": "latestFeature"}}), encoding="utf-8")
        fixture = ROOT / "tests/wasm"
        shutil.copytree(fixture / "Native", work / "Native")
        shutil.copytree(fixture / "wwwroot", work / "wwwroot")
        shutil.copyfile(fixture / "Consumer.project.xml", work / "Consumer.csproj")
        run([dotnet, "build", str(ROOT / "src/BGCS.Tool/BGCS.Tool.csproj"), "-c", "Release",
             "--disable-build-servers", "-m:1", "-nodeReuse:false"], work, work / "generator-build.log", environment)
        shutil.copyfile(ROOT / "src/BGCS.Runtime/bin/Release/net9.0/BGCS.Runtime.dll", work / "BGCS.Runtime.dll")
        raw = run([dotnet, "msbuild", "Consumer.csproj", "-nologo", "-nodeReuse:false",
                   "-getProperty:EmscriptenSdkToolsPath,EmscriptenCacheSdkCacheDir"],
                  work, work / "sdk-query.log", environment)
        sdk = json.loads(raw)["Properties"]
        sysroot = Path(sdk["EmscriptenCacheSdkCacheDir"]) / "sysroot"
        compiler = Path(sdk["EmscriptenSdkToolsPath"]) / "bin" / ("clang.exe" if os.name == "nt" else "clang")
        if not sysroot.is_dir() or not compiler.is_file():
            raise RuntimeError("The selected .NET SDK does not have a complete .NET 9 wasm-tools workload.")
        report["sdk"] = sdk
        tool = ROOT / "src/BGCS.Tool/bin/Release/net9.0/BGCS.Tool.dll"
        for mode in MODES:
            config = {"Preset": "emscripten-c", "ApiName": "NativeApi",
                      "Namespace": "WasmAcceptance." + mode, "LibName": "api",
                      "EntryFiles": ["Native/api.h"], "OutputPath": "Generated/" + mode,
                      "TargetSysRoot": str(sysroot), "CompilerPath": str(compiler),
                      "ImportType": mode, "UseCustomContext": mode == "FunctionTable",
                      "ParseSystemIncludes": False, "ParseMacros": False, "ParseComments": False,
                      "GenerateExtensions": False, "DelegatesAsVoidPointer": False,
                      "GenerateHandles": True, "GenerateRuntimeSource": False,
                      "MergeGeneratedFilesToSingleFile": True, "SingleFileOutputName": "Bindings.cs"}
            config_path = work / (mode + ".json")
            config_path.write_text(json.dumps(config, indent=2), encoding="utf-8")
            run([dotnet, str(tool), "generate", str(config_path)], work,
                work / (mode + "-generation.log"), environment)
        program = (fixture / "Program.cs.in").read_text(encoding="utf-8")
        invocation = (fixture / "Invocation.cs.in").read_text(encoding="utf-8")
        runners = "\n".join(invocation.replace("__MODE__", mode) for mode in MODES)
        (work / "Program.cs").write_text(program.replace("__RUNNERS__", runners), encoding="utf-8")
        run([dotnet, "publish", "Consumer.csproj", "-c", "Release", "-o", str(work / "published"),
             "--disable-build-servers", "-m:1", "-nodeReuse:false"],
            work, work / "publish.log", environment)
        result = invoke_browser(browser, work / "published/wwwroot", work)
        report["invocation"] = result
        expected = {mode + ":" + check for mode in MODES for check in (
            "scalar", "layout", "buffer", "capacity", "handle", "callback", "null-callback", "release")}
        expected.update(("missing-symbol", "function-table-context"))
        checks = result.get("checks", []) if result else []
        if not result or not result.get("success") or len(checks) != len(expected) or set(checks) != expected:
            raise RuntimeError("Native invocation failed or did not complete every required check.")
        if result.get("pointerSize") != 4:
            raise RuntimeError("The invocation did not execute in wasm32.")
        report["success"] = True
        print(f"Passed: {len(expected)} acceptance checks across {len(MODES)} import modes.")
    except Exception as error:
        report["error"] = str(error)
        print(f"FAILED: {error}", flush=True)
    finally:
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(f"Report: {report_path}", flush=True)
    return 0 if report["success"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
