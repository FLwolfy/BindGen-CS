#!/usr/bin/env python3
"""Build the grouped public examples and invoke their exact manifest-listed bindings."""
from __future__ import annotations

import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import tempfile
import uuid
import xml.etree.ElementTree as ET

from native_fixture_build import compile_shared
from acceptance_evidence import publication
from cpp_acceptance_fixture import execution_identity, stage_repository_generator, tool_command

ROOT = Path(__file__).resolve().parents[1]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/acceptance/examples.json")
    args = parser.parse_args()
    dotnet = str(Path(args.dotnet).resolve())
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    spec = importlib.util.spec_from_file_location("example_consumer_helpers", ROOT / "eng/verify-packages.py")
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    evidence = ROOT / "artifacts/examples" / uuid.uuid4().hex
    evidence.mkdir(parents=True)
    report = {"success": False, "checks": [], "evidence": str(evidence)}
    temporary = ROOT / "artifacts/temporary"
    temporary.mkdir(parents=True, exist_ok=True)
    minimum_free = int(os.environ.get("BGCS_MINIMUM_FREE_BYTES", str(1024 ** 3)))
    if minimum_free <= 0 or shutil.disk_usage(ROOT).free < minimum_free:
        raise RuntimeError("Example consumers require the declared positive free-space floor.")
    try:
        with tempfile.TemporaryDirectory(prefix="cli-example-", dir=temporary) as folder:
            work = Path(folder)
            tool = stage_repository_generator(ROOT, work, args.configuration)
            runtime = ROOT / "src/BGCS.Runtime/bin" / args.configuration / "net10.0/BGCS.Runtime.dll"
            report["executionIdentity"] = execution_identity(ROOT, tool, runtime)
            environment = {"DOTNET_ROOT": str(Path(dotnet).parent), "DOTNET_HOST_PATH": dotnet}
            example = work / "QuickStart"
            example.mkdir()
            for name in ("bindgen.json", "standalone.json", "native.h", "native.c"):
                shutil.copyfile(ROOT / "examples/QuickStart" / name, example / name)
            for mode, configuration in (("source", "standalone.json"), ("package", "bindgen.json")):
                result_text = helper.run_process([*tool_command(dotnet, tool), "generate", str(example / configuration),
                    "--output", str(evidence / mode), "--json"], work, evidence, mode + "-build", environment_overrides=environment, separate_stderr=True)
                result = json.loads(result_text)
                if not result.get("success") or not result.get("published"):
                    raise RuntimeError("The example build did not publish a complete validated set: " + mode)
                owned = publication(Path(result["outputRoot"]), "quickstart")
                sources = [Path(result["outputRoot"]) / item["relativePath"] for item in owned["artifacts"] if item["mediaType"] == "text/x-csharp"]
                if not sources or mode == "source" and (len(sources) != 1 or sources[0].name != "Bindings.cs"):
                    raise RuntimeError("The source-runtime example did not obey its declared single-file layout.")
                def native_run(arguments, directory, logs, label):
                    return helper.run_process(arguments, directory, logs, label, environment_overrides=environment)
                binary = compile_shared(example, Path(result["outputRoot"]), work / "external-native" / mode / helper.native_filename(), [example / "native.c"], native_run, evidence)
                consumer = work / mode
                helper.write_project(consumer, "", [])
                document = ET.parse(consumer / "Consumer.csproj")
                items = ET.SubElement(document.getroot(), "ItemGroup")
                for path in sources:
                    ET.SubElement(items, "Compile", Include=str(path))
                if mode == "package":
                    ET.SubElement(ET.SubElement(items, "Reference", Include="BGCS.Runtime"), "HintPath").text = str(runtime)
                document.write(consumer / "Consumer.csproj", encoding="utf-8", xml_declaration=True)
                (consumer / "Program.cs").write_text('using System;\nusing QuickStart.Generated;\nusing NativeApi api = NativeApi.Load(' + json.dumps(str(binary)) + ');\nif (api.Add(19, 23) != 42) throw new InvalidOperationException("Public CLI example invocation failed.");\nConsole.WriteLine("Public CLI example native result: 42");\n', encoding="utf-8")
                helper.run_process([dotnet, "build", "Consumer.csproj", "-c", args.configuration, "-m:1", "-nr:false"], consumer, evidence, mode + "-consumer-build", environment_overrides=environment)
                helper.run_process([dotnet, str(consumer / "bin" / args.configuration / "net10.0/Consumer.dll")], consumer, evidence, mode + "-native-invocation", environment_overrides=environment)
                report["checks"].append({"mode": mode, "managedArtifacts": len(sources), "nativeInvoked": True,
                    "outputRoot": result["outputRoot"], "externalNativeSha256": __import__("hashlib").sha256(binary.read_bytes()).hexdigest()})
            report["success"] = True
    except BaseException as error:
        report["failure"] = str(error)
        raise
    finally:
        output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
