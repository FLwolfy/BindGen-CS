#!/usr/bin/env python3
"""Exercise real native allocator pairs through Runtime buffers and generated friendly text."""
from __future__ import annotations
from native_fixture_build import compile_shared

import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import uuid

from acceptance_evidence import digest, publication, source_identity
from cpp_acceptance_fixture import execution_identity, stage_repository_generator, tool_command

ROOT = Path(__file__).resolve().parents[1]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--tool-path", type=Path)
    parser.add_argument("--runtime-path", type=Path)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/acceptance/custom-allocator.json")
    args = parser.parse_args()
    dotnet = str(Path(args.dotnet).resolve())
    output = args.output.resolve()
    evidence = ROOT / "artifacts/custom-allocator-acceptance" / uuid.uuid4().hex
    evidence.mkdir(parents=True)
    before = source_identity(ROOT)
    report = {"schemaVersion": 1, "success": False, "scope": "real-native-runtime-and-generated-functiontable-allocator-consumers",
              "source": before, "evidence": str(evidence), "generatedTemporaryAllocatorInvoked": False}
    spec = importlib.util.spec_from_file_location("allocator_acceptance_helpers", ROOT / "eng/verify-packages.py")
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    try:
        minimum_free = int(os.environ.get("BGCS_MINIMUM_FREE_BYTES", str(1024 ** 3)))
        if minimum_free <= 0 or shutil.disk_usage(ROOT).free < minimum_free:
            raise RuntimeError("Custom allocator acceptance requires its positive free-space floor.")
        temporary = ROOT / "artifacts/temporary"
        temporary.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="native-allocator-", dir=temporary) as directory:
            work = Path(directory)
            tool = args.tool_path.resolve() if args.tool_path else stage_repository_generator(ROOT, work, args.configuration)
            runtime = args.runtime_path.resolve() if args.runtime_path else ROOT / "src/BGCS.Runtime/bin" / args.configuration / "net10.0/BGCS.Runtime.dll"
            if not tool.is_file() or not runtime.is_file():
                raise RuntimeError("The closed generator or Runtime input is missing.")
            report["executionIdentity"] = execution_identity(ROOT, tool, runtime)
            example = ROOT / "examples/CustomAllocator"
            for name in ("native.h", "native.cpp"):
                shutil.copyfile(example / name, work / name)
            config = json.loads((example / "bindgen.json").read_text(encoding="utf-8"))
            config["output"]["root"] = str(work / "Generated")
            compiler = os.environ.get("BGCS_CORPUS_CXX") or os.environ.get("BGCS_CPP2C_CXX") or os.environ.get("CXX")
            if compiler:
                resolved = shutil.which(compiler) or (compiler if Path(compiler).is_file() else None)
                if resolved is None:
                    raise RuntimeError("The explicitly selected native allocator compiler is missing: " + compiler)
                config["targets"] = [{"id": "host", "settings": {"compilerPath": str(Path(resolved).resolve())}}]
            config_path = work / "bindgen.json"
            config_path.write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8")
            environment = {"DOTNET_ROOT": str(Path(dotnet).parent), "DOTNET_HOST_PATH": dotnet,
                           "NUGET_PACKAGES": str(work / "nuget-packages"), "NUGET_HTTP_CACHE_PATH": str(work / "nuget-http")}
            build = json.loads(helper.run_process([*tool_command(dotnet, tool), "generate", str(config_path), "--json"],
                work, evidence, "native-public-build", environment_overrides=environment, separate_stderr=True))
            if build.get("success") is not True or build.get("published") is not True:
                raise RuntimeError("The public native allocator build did not publish a validated complete candidate.")
            owned = publication(work / "Generated", "custom_allocator")
            def native_run(arguments, directory, logs, label):
                return helper.run_process(arguments, directory, logs, label, environment_overrides=environment)
            binaries = [compile_shared(work, work / "Generated", evidence / "external-native" / helper.native_filename(), [work / "native.cpp"], native_run, evidence, compiler=compiler)]
            consumer = work / "Consumer"
            consumer.mkdir()
            for name in ("Program.cs", "Consumer.csproj"):
                shutil.copyfile(example / "Consumer" / name, consumer / name)
            helper.run_process([dotnet, "build", "Consumer.csproj", "-c", args.configuration, "-m:1", "-nr:false",
                "-p:UseSharedCompilation=false", "-p:BGCSRuntimeAssembly=" + str(runtime)], consumer, evidence,
                "runtime-only-consumer-build", environment_overrides=environment)
            consumer_binary = consumer / "bin" / args.configuration / "net10.0/Consumer.dll"
            result = json.loads(helper.run_process([dotnet, str(consumer_binary), str(binaries[0])], consumer, evidence,
                "native-runtime-allocation-invocation", environment_overrides=environment))
            if result != {"checks": 10, "nativeChecksum": 819, "allocator": "example.instance", "nativeReleases": 1, "moduleUnloads": 1}:
                raise RuntimeError("The native allocator consumer returned unexpected real-call results: " + repr(result))
            generated_consumer = work / "GeneratedConsumer"
            generated_consumer.mkdir()
            for name in ("Program.cs", "GeneratedConsumer.csproj"):
                shutil.copyfile(example / "GeneratedConsumer" / name, generated_consumer / name)
            helper.run_process([dotnet, "build", "GeneratedConsumer.csproj", "-c", args.configuration, "-m:1", "-nr:false",
                "-p:UseSharedCompilation=false", "-p:BGCSRuntimeAssembly=" + str(runtime),
                "-p:BGCSGeneratedRoot=" + str(work / "Generated")], generated_consumer, evidence,
                "generated-text-consumer-build", environment_overrides=environment)
            generated_binary = generated_consumer / "bin" / args.configuration / "net10.0/GeneratedConsumer.dll"
            generated_result = json.loads(helper.run_process([dotnet, str(generated_binary), str(binaries[0])], generated_consumer,
                evidence, "native-generated-text-allocation-invocation", environment_overrides=environment))
            if generated_result != {"checks": 8, "nativeUtf8ByteLength": 5, "allocator": "example.generated-instance", "nativeReleases": 2, "moduleUnloads": 1}:
                raise RuntimeError("The generated allocator consumer returned unexpected real-call results: " + repr(generated_result))
            report.update(success=True, nativeBuilt=True, runtimeConsumerInvoked=True, independentNativeChecksum=True,
                result=result, nativeBinarySha256=digest(binaries[0]), consumerSha256=digest(consumer_binary),
                nativeInputs={name: digest(example / name) for name in ("native.h", "native.cpp")},
                consumerSourceSha256=digest(example / "Consumer/Program.cs"), generatedTemporaryAllocatorInvoked=True,
                generatedTextResult=generated_result, generatedConsumerSha256=digest(generated_binary),
                generatedConsumerSourceSha256=digest(example / "GeneratedConsumer/Program.cs"),
                generatedTextScope={"importMode": "functionTable", "runtimeMode": "package", "framework": "net10.0"})
    except Exception as failure:
        report["failure"] = str(failure)
        print("[custom-allocator] " + str(failure), file=sys.stderr)
    finally:
        report["sourceAtCompletion"] = source_identity(ROOT)
        report["sourceBytesStable"] = report["sourceAtCompletion"] == before
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return 0 if report["success"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
