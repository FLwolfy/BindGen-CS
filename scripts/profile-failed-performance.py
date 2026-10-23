#!/usr/bin/env python3
"""Collect one non-certifying warm trace only after a current-source strict performance failure."""
from __future__ import annotations

import argparse
import datetime
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import time
import uuid

import cpp_acceptance_fixture as fixture
from acceptance_evidence import digest, source_identity
from compiler_test_environment import isolated_native_test_environment

ROOT = Path(__file__).resolve().parents[1]
TRACE_VERSION = "10.0.750501"
MINIMUM_FREE_BYTES = 50 * 1024**3
PHASE_SECONDS = 180
REAP_SECONDS = 30


def failure_guard(directory: Path, source: dict) -> tuple[str, str, Path | None, dict | None]:
    reports = list(directory.glob("*/report.json"))
    if not reports:
        return "skipped", "No original performance report exists; no diagnostic was started.", None, None
    latest = max(reports, key=lambda path: (path.stat().st_mtime_ns, str(path)))
    if latest.is_symlink() or not latest.resolve().is_relative_to(directory.resolve()):
        return "rejected", "The latest original report escapes its performance directory.", latest, None
    original = json.loads(latest.read_text(encoding="utf-8-sig"))
    if original.get("success") is True:
        return "skipped", "The latest original performance gate passed; no diagnostic was started.", latest, original
    execution = original.get("execution", {})
    budgets = json.loads((ROOT / "eng/acceptance/performance-budgets.json").read_text(encoding="utf-8"))
    host = "macos-x64" if platform.system() == "Darwin" and platform.machine().lower() not in ("arm64", "aarch64") else "windows-x64" if os.name == "nt" else "linux-x64"
    expected = budgets["hosts"][host]
    if (original.get("success") is not False or original.get("diagnosticOnly")
            or original.get("schemaVersion") != 1 or original.get("declarations") != 10000
            or original.get("budgetManifestSha256") != digest(ROOT / "eng/acceptance/performance-budgets.json")
            or (original.get("coldBudgetSeconds"), original.get("warmBudgetSeconds")) != (expected["coldSeconds"], expected["warmSeconds"])
            or execution.get("sourceRevision") != source["revision"]
            or execution.get("checkedOutSourceSha256") != source["checkedOutSourceSha256"]):
        return "rejected", "The latest report is not an original 10k failure for these exact checked-out sources.", latest, original
    return "eligible", "A same-source original strict failure was preserved.", latest, original


def owned_directory(path: Path) -> Path:
    resolved = path.resolve()
    if not resolved.is_relative_to((ROOT / "artifacts").resolve()):
        raise RuntimeError("Diagnostic storage must remain inside this repository's owned artifacts directory.")
    return resolved


def check_space(work: Path) -> None:
    if shutil.disk_usage(work).free < MINIMUM_FREE_BYTES:
        raise RuntimeError("Diagnostic stopped at the mandatory 50 GiB free-space floor.")


def child_environment(dotnet: Path) -> dict[str, str]:
    environment = {key: value for key, value in isolated_native_test_environment(os.environ).items()
                   if key.casefold() != "platform"}
    environment.update(DOTNET_ROOT=str(dotnet.parent), DOTNET_HOST_PATH=str(dotnet), DOTNET_CLI_UI_LANGUAGE="en")
    return environment


def start_owned(command, cwd, environment, log, children, psutil):
    process = subprocess.Popen([str(value) for value in command], cwd=cwd, env=environment,
        stdout=log, stderr=subprocess.STDOUT, start_new_session=os.name != "nt",
        creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
    try:
        parent = psutil.Process(process.pid)
    except psutil.NoSuchProcess:
        parent = None
    entry = (process, parent, {})
    children.append(entry)
    return entry


def wait_owned(entry, work, timeout, psutil):
    process, parent, observed = entry
    started = time.monotonic()
    while process.poll() is None:
        if parent is not None:
            try:
                for child in parent.children(recursive=True):
                    observed[(child.pid, child.create_time())] = child
            except psutil.NoSuchProcess:
                pass
        check_space(work)
        if time.monotonic() - started >= timeout:
            raise TimeoutError(f"Diagnostic child {process.pid} exceeded its {timeout}s phase limit.")
        time.sleep(0.05)
    return process.wait()


def reap_owned(children, psutil):
    deadline = time.monotonic() + REAP_SECONDS
    known = {}
    for process, parent, observed in children:
        known.update(observed)
        if parent is not None:
            try:
                for child in parent.children(recursive=True):
                    known[(child.pid, child.create_time())] = child
                known[(parent.pid, parent.create_time())] = parent
            except psutil.NoSuchProcess:
                pass
    for child in reversed(list(known.values())):
        try:
            if child.is_running():
                child.kill()
        except psutil.NoSuchProcess:
            pass
    _, alive = psutil.wait_procs(list(known.values()), timeout=max(0, deadline - time.monotonic()))
    for process, _, _ in children:
        if process.poll() is None:
            process.kill()
        process.wait(timeout=max(0.001, deadline - time.monotonic()))
    if alive:
        raise RuntimeError("An owned diagnostic process did not exit during the bounded reap: " + str([item.pid for item in alive]))


def cache_receipt(source: Path, expected: str) -> dict:
    receipt = json.loads((source / "Generated/generation.report.json").read_text(encoding="utf-8-sig"))
    stages = receipt.get("cache", [])
    if len(stages) != 2 or {entry["stage"]: entry["status"] for entry in stages} != {
            "primaryFrontend": expected, "csharpEmission": expected}:
        raise RuntimeError("Diagnostic generation did not report the required two cache " + expected + " stages.")
    return receipt


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--performance-root", type=Path, default=ROOT / "artifacts/performance")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/performance-diagnostics")
    parser.add_argument("--trace-tool-path", type=Path, default=ROOT / "artifacts/toolchains/performance-diagnostics")
    args = parser.parse_args(argv)
    destination = owned_directory(args.output)
    destination.mkdir(parents=True, exist_ok=True)
    work = destination / uuid.uuid4().hex
    work.mkdir()
    report = {"schemaVersion": 1, "diagnosticOnly": True, "releaseCertification": False,
        "success": False, "status": "starting", "phaseLimitSeconds": PHASE_SECONDS, "reapLimitSeconds": REAP_SECONDS,
        "scope": "One normal cache fill and one PID-attached warm trace; original acceptance failure remains unchanged.",
        "startedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(), "commands": []}
    children, psutil = [], None
    result = 0
    try:
        source_before = source_identity(ROOT)
        status, reason, original_path, original = failure_guard(owned_directory(args.performance_root), source_before)
        report.update(status=status, reason=reason, sourceBefore=source_before)
        if original_path is not None:
            report["originalFailure"] = {"path": str(original_path), "sha256": digest(original_path), "report": original}
        if status != "eligible":
            return 0
        check_space(work)
        import psutil as metrics
        psutil = metrics
        if psutil.__version__ != "7.0.0":
            raise RuntimeError("The diagnostic requires the locked psutil 7.0.0 prerequisite.")
        report["psutilVersion"] = psutil.__version__
        dotnet = args.dotnet.resolve(strict=True)
        environment = child_environment(dotnet)
        report["childEnvironmentSha256"] = hashlib.sha256(json.dumps(environment, sort_keys=True).encode()).hexdigest()

        def run(name, command, cwd=ROOT, limit=PHASE_SECONDS):
            started = time.monotonic()
            row = {"name": name, "argv": [str(value) for value in command], "log": str(work / (name + ".log"))}
            report["commands"].append(row)
            with Path(row["log"]).open("wb") as log:
                entry = start_owned(command, cwd, environment, log, children, psutil)
                row["pid"] = entry[0].pid
                row["exitCode"] = wait_owned(entry, work, limit, psutil)
            row["elapsedSeconds"] = time.monotonic() - started
            if row["exitCode"] != 0:
                raise RuntimeError(name + " failed; see " + row["log"])

        tool = fixture.stage_repository_generator(ROOT, work, args.configuration)
        runtime = ROOT / "src/BGCS.Runtime/bin" / args.configuration / "net10.0/BGCS.Runtime.dll"
        report["executionBefore"] = fixture.execution_identity(ROOT, tool, runtime)
        for key in ("executionClosureSha256", "generatorEntrySha256", "runtimeAssemblySha256"):
            if report["executionBefore"].get(key) != original["execution"].get(key):
                raise RuntimeError("The compiled generator closure differs from the original failure: " + key)
        trace_root = owned_directory(args.trace_tool_path)
        trace_root.mkdir(parents=True, exist_ok=True)
        trace = trace_root / ("dotnet-trace.exe" if os.name == "nt" else "dotnet-trace")
        if not trace.is_file():
            run("install-trace", [dotnet, "tool", "install", "dotnet-trace", "--version", TRACE_VERSION, "--tool-path", trace_root])
        run("trace-version", [trace, "--version"])
        version = (work / "trace-version.log").read_text(encoding="utf-8-sig").strip()
        if version.split("+", 1)[0] != TRACE_VERSION:
            raise RuntimeError("The installed dotnet-trace does not match exact version " + TRACE_VERSION)
        report["traceTool"] = {"version": version, "path": str(trace), "sha256": digest(trace)}
        source = work / "fixture"
        source.mkdir()
        (source / "large.h").write_text("".join(f"int bgcs_perf_{i}(int value);\n" for i in range(10000)), encoding="utf-8")
        config = source / "bindgen.json"
        config.write_text(json.dumps({"schemaVersion": 2, "library": {"id": "performance", "nativeName": "performance"},
            "source": {"language": "c", "headers": ["large.h"], "parseMacros": False, "parseComments": False},
            "api": {"namespace": "BGCS.Performance.Generated", "name": "PerformanceApi", "runtime": "dotnet.coreclr"},
            "cache": {"enabled": True, "directory": ".cache"}}, indent=2), encoding="utf-8")
        command = [dotnet, tool, "generate", config]
        run("cache-fill", command, source)
        report["cacheFill"] = cache_receipt(source, "miss")
        specification = importlib.util.spec_from_file_location("strict_performance_output", ROOT / "scripts/test-performance-budget.py")
        strict = importlib.util.module_from_spec(specification)
        specification.loader.exec_module(strict)
        output = source / "Generated"
        if output.is_symlink() or output.resolve().parent != source.resolve():
            raise RuntimeError("Diagnostic output deletion escaped its fresh UUID-owned fixture.")
        cold_files = strict.verify_output(output)
        shutil.rmtree(output)
        trace_file = work / "warm.nettrace"
        started = time.monotonic()
        # Launch normally, then attach. Launching THROUGH dotnet-trace injects
        # diagnostic environment variables into the worker's cache-key inputs.
        with (work / "warm.log").open("wb") as child_log, (work / "collector.log").open("wb") as collector_log:
            warm = start_owned(command, source, environment, child_log, children, psutil)
            collector_command = [trace, "collect", "-p", str(warm[0].pid), "--profile", "dotnet-sampled-thread-time",
                                 "--buffersize", "64", "--format", "Speedscope", "--output", trace_file]
            collector = start_owned(collector_command, ROOT, environment, collector_log, children, psutil)
            code = wait_owned(warm, work, PHASE_SECONDS, psutil)
            report["warm"] = {"pid": warm[0].pid, "exitCode": code, "instrumentedSeconds": time.monotonic() - started}
            collector_code = wait_owned(collector, work, REAP_SECONDS, psutil)
            report["collector"] = {"pid": collector[0].pid, "argv": [str(value) for value in collector_command], "exitCode": collector_code}
            if code != 0 or collector_code != 0 or not trace_file.is_file() or trace_file.stat().st_size == 0:
                raise RuntimeError("The PID-attached diagnostic failed; see warm.log and collector.log.")
        report["warmReceipt"] = cache_receipt(source, "hit")
        if strict.verify_output(output) != cold_files:
            raise RuntimeError("The warm diagnostic did not restore the identical managed artifact set.")
        for mode, options in (("exclusive", []), ("inclusive", ["--inclusive"])):
            run("top-" + mode, [trace, "report", trace_file, "topN", "-n", "60", "--verbose", *options])
        report.update(success=True, status="diagnostic-completed")
    except Exception as failure:
        report.update(status="diagnostic-failed", error=str(failure))
        result = 1
    finally:
        if psutil is not None:
            try:
                reap_owned(children, psutil)
            except Exception as failure:
                report.update(success=False, status="diagnostic-failed", cleanupError=str(failure))
                result = 1
        if "executionBefore" in report:
            try:
                report["executionAfter"] = fixture.execution_identity(ROOT, tool, runtime)
                if report["executionAfter"] != report["executionBefore"]:
                    raise RuntimeError("Source or executed generator closure changed during the diagnostic.")
                if digest(original_path) != report["originalFailure"]["sha256"]:
                    raise RuntimeError("The original failure report changed during the diagnostic.")
            except Exception as failure:
                report.update(success=False, status="diagnostic-failed", identityError=str(failure))
                result = 1
        (work / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({"diagnosticOnly": True, "releaseCertification": False, "status": report["status"],
                          "reason": report.get("reason"), "error": report.get("error"), "report": str(work / "report.json")}))
    return result


if __name__ == "__main__":
    raise SystemExit(main())
