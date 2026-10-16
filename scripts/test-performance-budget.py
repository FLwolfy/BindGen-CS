#!/usr/bin/env python3
"""Measure the neutral pipeline on 10,000 declarations and verify scoped cache restoration."""

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
import time
import uuid
import cpp_acceptance_fixture
from compiler_test_environment import isolated_native_test_environment

ROOT = Path(__file__).resolve().parent.parent


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def verify_output(output):
    owner = json.loads((output / ".bgcs-artifacts.json").read_text(encoding="utf-8-sig"))
    if owner.get("schemaVersion") != 1 or owner.get("libraryId") != "performance":
        raise RuntimeError("The generated output has no matching BGCS publication owner.")
    expected = {".bgcs-artifacts.json"}
    managed = {}
    for artifact in owner["artifacts"]:
        relative = artifact["relativePath"]
        path = output / relative
        if Path(relative).is_absolute() or any(part in (".", "..") for part in Path(relative).parts):
            raise RuntimeError("An artifact path escapes the verified output.")
        if path.is_symlink() or not path.is_file() or digest(path) != artifact["digest"]["value"]:
            raise RuntimeError(f"Generated artifact verification failed: {relative}")
        expected.add(relative)
        if artifact["mediaType"] == "text/x-csharp":
            managed[relative] = artifact["digest"]["value"]
    actual = {path.relative_to(output).as_posix() for path in output.rglob("*") if path.is_file()}
    if actual != expected or not managed:
        raise RuntimeError("The published artifact set is incomplete or contains unowned files.")
    return managed


def invoke(command, cwd, log, environment, timeout, sampling_seconds, psutil):
    peak_session = 0
    peak_worker = 0
    started = time.perf_counter()
    with log.open("wb") as stream:
        process = subprocess.Popen(command, cwd=cwd, env=environment, stdout=stream, stderr=subprocess.STDOUT,
                                   start_new_session=os.name != "nt",
                                   creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
        try:
            root = psutil.Process(process.pid)
            while process.poll() is None:
                if time.perf_counter() - started > timeout:
                    failure = TimeoutError("Generation exceeded its enforced elapsed-time budget.")
                    failure.measurement = {"elapsedSeconds": time.perf_counter() - started, "peakSessionRssBytes": peak_session,
                                           "peakWorkerRssBytes": peak_worker, "commandLog": str(log), "timedOut": True}
                    raise failure
                total = 0
                try:
                    descendants = root.children(recursive=True)
                except psutil.NoSuchProcess:
                    descendants = []
                for child in [root, *descendants]:
                    try:
                        rss = child.memory_info().rss
                        total += rss
                        if any("BGCS.Frontend.Clang.Worker" in argument for argument in child.cmdline()):
                            peak_worker = max(peak_worker, rss)
                    except (psutil.NoSuchProcess, psutil.AccessDenied):
                        continue
                peak_session = max(peak_session, total)
                time.sleep(sampling_seconds)
            code = process.wait()
        except BaseException:
            try:
                descendants = root.children(recursive=True)
            except psutil.NoSuchProcess:
                descendants = []
            for child in reversed(descendants):
                try:
                    child.kill()
                except psutil.NoSuchProcess:
                    pass
            if process.poll() is None:
                process.kill()
            process.wait(timeout=30)
            psutil.wait_procs(descendants, timeout=30)
            raise
    if code != 0:
        raise RuntimeError(f"Generator failed with exit {code}; see {log}")
    measurement = {"elapsedSeconds": time.perf_counter() - started, "peakSessionRssBytes": peak_session,
                   "peakWorkerRssBytes": peak_worker, "commandLog": str(log)}
    if measurement["elapsedSeconds"] > timeout:
        failure = TimeoutError("Generation completed beyond its enforced elapsed-time budget.")
        failure.measurement = measurement
        raise failure
    return measurement


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=os.environ.get("DOTNET_HOST_PATH") or shutil.which("dotnet"))
    parser.add_argument("--configuration", default=os.environ.get("CONFIGURATION", "Release"))
    parser.add_argument("--tool-path", type=Path)
    parser.add_argument("--declarations", type=int, default=int(os.environ.get("BGCS_PERF_DECLARATIONS", "10000")))
    parser.add_argument("--cold-budget", type=float)
    parser.add_argument("--warm-budget", type=float)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/performance")
    args = parser.parse_args()
    budgets = json.loads((ROOT / "eng/acceptance/performance-budgets.json").read_text(encoding="utf-8"))
    if not args.dotnet or not Path(args.dotnet).is_file() or args.declarations <= 0:
        parser.error("A real dotnet host and positive declaration count are required.")
    try:
        import psutil
    except ImportError:
        parser.error("Install the locked psutil performance-monitor prerequisite before running this gate.")
    dotnet = str(Path(args.dotnet).resolve())
    tool = (args.tool_path or ROOT / "src/BGCS.Tool/bin" / args.configuration / "net10.0/BGCS.Tool.dll").resolve()
    if not tool.is_file():
        parser.error("Build the common CLI first, or supply --tool-path to a closed tool installation.")
    intel_mac = platform.system() == "Darwin" and platform.machine().lower() not in ("arm64", "aarch64")
    host_budget = budgets["hosts"]["macos-x64" if intel_mac else "windows-x64" if os.name == "nt" else "linux-x64"]
    cold_budget = args.cold_budget if args.cold_budget is not None else float(os.environ.get("BGCS_PERF_COLD_BUDGET_SECONDS", str(host_budget["coldSeconds"])))
    warm_budget = args.warm_budget if args.warm_budget is not None else float(os.environ.get("BGCS_PERF_WARM_BUDGET_SECONDS", str(host_budget["warmSeconds"])))
    sampling_seconds = float(budgets["memorySamplingSeconds"])
    if any(not math.isfinite(value) or value <= 0 for value in (cold_budget, warm_budget, sampling_seconds)):
        parser.error("Elapsed-time budgets must be positive.")
    destination = args.output.resolve()
    destination.mkdir(parents=True, exist_ok=True)
    work = destination / uuid.uuid4().hex
    source = work / "fixture"
    source.mkdir(parents=True)
    logs = work / "logs"
    logs.mkdir()
    output = source / "Generated"
    report = {"schemaVersion": 1, "success": False, "host": platform.platform(), "declarations": args.declarations,
              "cacheScope": ["primaryFrontend", "csharpEmission"], "fullPipelineCacheCertified": False,
              "coldBudgetSeconds": cold_budget, "warmBudgetSeconds": warm_budget,
              "workerRssBudgetBytes": budgets["workerPeakResidentBytes"], "sessionRssBudgetBytes": budgets["sessionPeakResidentBytes"],
              "memorySamplingSeconds": budgets["memorySamplingSeconds"], "memoryMetric": "sum of process resident working sets",
              "budgetManifestSha256": digest(ROOT / "eng/acceptance/performance-budgets.json"),
              "psutilVersion": psutil.__version__, "hostCertification": "pending" if platform.machine().lower() in ("arm64", "aarch64") else "local checkpoint"}
    environment = {key: value for key, value in isolated_native_test_environment(os.environ).items()
                   if key.casefold() != "platform"}
    environment.update(DOTNET_ROOT=str(Path(dotnet).parent), DOTNET_HOST_PATH=dotnet, DOTNET_CLI_UI_LANGUAGE="en")
    active_phase = "staging"
    try:
        if args.tool_path is None:
            tool = cpp_acceptance_fixture.stage_repository_generator(ROOT, work, args.configuration)
        runtime_candidates = list(tool.parent.rglob("BGCS.Runtime.dll"))
        runtime = tool.parent / "BGCS.Runtime.dll"
        if not runtime.is_file():
            if len(runtime_candidates) != 1:
                raise RuntimeError("The executed generator closure must identify exactly one consumer Runtime assembly.")
            runtime = runtime_candidates[0]
        report["execution"] = cpp_acceptance_fixture.execution_identity(ROOT, tool, runtime)
        report["toolSha256"] = digest(tool)
        command = [dotnet, str(tool)] if tool.suffix.lower() == ".dll" else [str(tool)]
        (source / "large.h").write_text("".join(f"int bgcs_perf_{index}(int value);\n" for index in range(args.declarations)), encoding="utf-8")
        config = {"schemaVersion": 1, "library": {"id": "performance", "nativeName": "performance"},
                  "source": {"language": "c", "headers": ["large.h"], "parseMacros": False, "parseComments": False},
                  "api": {"namespace": "BGCS.Performance.Generated", "name": "PerformanceApi"},
                  "cache": {"enabled": True, "directory": ".cache"}}
        configuration = source / "bindgen.json"
        configuration.write_text(json.dumps(config, indent=2), encoding="utf-8")
        active_phase = "cold"
        report["cold"] = invoke([*command, "generate", str(configuration)], source, logs / "cold.log", environment, cold_budget, sampling_seconds, psutil)
        cold = verify_output(output)
        raw = "\n".join(path.read_text(encoding="utf-8") for path in output.rglob("*.Raw.g.cs"))
        emitted = {int(number) for number in re.findall(r"\bBgcsPerf(\d+)\s*\(", raw)}
        if emitted != set(range(args.declarations)):
            raise RuntimeError("The raw API does not preserve every requested declaration identity.")
        cold_receipt = json.loads((output / "generation.report.json").read_text(encoding="utf-8-sig"))
        cold_stages = {stage["stage"]: stage["status"] for stage in cold_receipt.get("cache", [])}
        if len(cold_receipt.get("cache", [])) != 2 or cold_stages != {"primaryFrontend": "miss", "csharpEmission": "miss"}:
            raise RuntimeError("The isolated cold generation did not report the two certified stage misses.")
        # Delete only this UUID-owned, fully verified generated tree, keeping the cache for restoration.
        if output.resolve().parent != source.resolve() or output.is_symlink():
            raise RuntimeError("The output cleanup target is outside this owned fixture.")
        shutil.rmtree(output)
        active_phase = "warm"
        report["warm"] = invoke([*command, "generate", str(configuration)], source, logs / "warm.log", environment, warm_budget, sampling_seconds, psutil)
        warm = verify_output(output)
        warm_receipt = json.loads((output / "generation.report.json").read_text(encoding="utf-8-sig"))
        warm_stages = {stage["stage"]: stage["status"] for stage in warm_receipt.get("cache", [])}
        if len(warm_receipt.get("cache", [])) != 2 or warm_stages != {"primaryFrontend": "hit", "csharpEmission": "hit"}:
            raise RuntimeError("The hot run did not independently reuse both certified generation stages.")
        if cold != warm:
            raise RuntimeError("The complete managed file set or a restored byte digest differs from the cold generation.")
        if any(sample["peakWorkerRssBytes"] > report["workerRssBudgetBytes"] or sample["peakSessionRssBytes"] > report["sessionRssBudgetBytes"] for sample in (report["cold"], report["warm"])):
            raise RuntimeError("The real generation process tree exceeded its resident-memory budget.")
        if report["cold"]["peakWorkerRssBytes"] <= 0 or any(sample["peakSessionRssBytes"] <= 0 for sample in (report["cold"], report["warm"])):
            raise RuntimeError("The cold parse must observe worker memory; both runs must observe actual process-tree memory.")
        entries = list((source / ".cache").glob("*.stage.json"))
        if len(entries) != 2:
            raise RuntimeError("Exactly two immutable stage entries are required for this unchanged fixture.")
        report.update(success=True, emittedNativeMethods=len(emitted), restoredAfterOutputDeletion=True,
                      restoredManagedFiles=warm, cacheEntries=len(entries))
        print(f"Passed: {args.declarations} declarations, cold={report['cold']['elapsedSeconds']:.3f}s, warm={report['warm']['elapsedSeconds']:.3f}s", flush=True)
    except Exception as error:
        report["error"] = str(error)
        if hasattr(error, "measurement"):
            report[active_phase] = error.measurement
        print(f"FAILED: {error}", flush=True)
    finally:
        path = work / "report.json"
        path.write_text(json.dumps(report, indent=2), encoding="utf-8")
        # Successful bulk outputs/cache are rebuildable; retain the exact receipt and bounded command logs.
        if report["success"] and source.resolve().parent == work.resolve() and not source.is_symlink():
            shutil.rmtree(source)
        generator = work / "generator"
        if generator.is_dir() and generator.resolve().parent == work.resolve() and not generator.is_symlink():
            shutil.rmtree(generator)
        print(f"Report: {path}", flush=True)
    return 0 if report["success"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
