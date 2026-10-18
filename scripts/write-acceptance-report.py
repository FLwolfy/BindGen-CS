#!/usr/bin/env python3
"""Join exact executed-test and product receipts without certifying unexecuted stages."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path

from acceptance_evidence import digest, source_identity, test_receipt
from importlib.util import module_from_spec, spec_from_file_location

ROOT = Path(__file__).resolve().parents[1]
CATEGORIES = {
    "architecture": ("architecture", "style", "documentation"),
    "executed-managed-tests": ("solution-build", "managed-tests"),
    "native-c-fixtures": ("native-c-abi", "strict-safety"),
    "native-cpp-fixtures": ("native-cpp-bridge", "modern-cpp", "advanced-cpp-semantics"),
    "runtime-lifetimes": ("callback-async-lifetime", "custom-allocator"),
    "public-cli-examples": ("examples",),
    "locked-upstream-generation": ("real-libraries", "real-cpp-libraries", "ir-native-real-libraries"),
    "upstream-native-invocation": ("upstream-native-invocation",),
    "reviewed-public-api": ("api-compatibility",),
    "installed-host-products": ("nuget-tool", "native-package"),
    "generation-performance": ("performance",),
    "dependency-policy": ("supply-chain",),
}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gate-root", type=Path, required=True)
    parser.add_argument("--test-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/acceptance")
    args = parser.parse_args()
    output = args.output.resolve()
    started = json.loads((args.gate_root / "run-start.json").read_text(encoding="utf-8-sig"))
    if source_identity(ROOT) != started["source"]:
        raise RuntimeError("Source bytes or Git revision changed during this acceptance invocation.")
    spec = spec_from_file_location("report_corpus_helpers", ROOT / "scripts/test-corpus.py")
    corpus = module_from_spec(spec)
    spec.loader.exec_module(corpus)
    platform = corpus.host_platform()
    gates = {gate for category in CATEGORIES.values() for gate in category}
    for gate in sorted(gates):
        if not (args.gate_root / gate).is_file():
            raise RuntimeError("A current invocation did not complete its required gate: " + gate)
    tests = [test_receipt(path) for path in sorted(args.test_root.glob("*.trx"))]
    if not tests:
        raise RuntimeError("An acceptance report requires actual nonzero executed-test receipts.")
    inputs = {}
    shard_path = args.test_root / "BGCS.Tests.shards.json"
    if os.environ.get("GITHUB_ACTIONS") == "true" and not shard_path.is_file():
        raise RuntimeError("CI acceptance requires the complete current eight-shard Host proof.")
    if shard_path.is_file():
        shards = json.loads(shard_path.read_text(encoding="utf-8"))
        if (shards.get("success") is not True or shards.get("shardCount") != 8
                or shards.get("certifying") is not True or shards.get("identity", {}).get("source") != started["source"]
                or shards.get("trxSha256") != digest(args.test_root / "BGCS.Tests.trx")):
            raise RuntimeError("The exact Host union is dirty, stale, incomplete or no longer matches the actual TRX.")
        inputs["host-test-shards"] = {"path": str(shard_path.resolve()), "sha256": digest(shard_path), "value": shards}

    def receipt(name: str, path: Path, success: bool = True) -> dict:
        if path.stat().st_mtime < started["startedAtUnixSeconds"]:
            raise RuntimeError("A previous invocation's stale receipt cannot certify the current gate: " + str(path))
        value = json.loads(path.read_text(encoding="utf-8-sig"))
        if success and value.get("success") is not True:
            raise RuntimeError("The declared gate has no passing structured receipt: " + str(path))
        inputs[name] = {"path": str(path.resolve()), "sha256": digest(path), "receipt": value}
        return value

    examples = receipt("examples", output / "examples.json")
    if len(examples.get("checks", [])) != 2 or not all(item.get("nativeInvoked") for item in examples["checks"]):
        raise RuntimeError("Both public package/source examples must invoke their actual native binary.")
    allocator = receipt("custom-allocator", output / "custom-allocator.json")
    if (allocator.get("source") != started["source"] or allocator.get("sourceBytesStable") is not True
            or allocator.get("nativeBuilt") is not True or allocator.get("runtimeConsumerInvoked") is not True
            or allocator.get("independentNativeChecksum") is not True
            or allocator.get("result") != {"checks": 10, "nativeChecksum": 819, "allocator": "example.instance", "nativeReleases": 1, "moduleUnloads": 1}
            or allocator.get("generatedTemporaryAllocatorInvoked") is not True
            or allocator.get("generatedTextResult") != {"checks": 8, "nativeUtf8ByteLength": 5, "allocator": "example.generated-instance", "nativeReleases": 2, "moduleUnloads": 1}):
        raise RuntimeError("The real Runtime buffer and generated text consumers must prove matching native frees and retained module lifetime.")
    selection_digest = digest(ROOT / "tests/corpus/upstream-limitations.json")
    for group, required in (("c", {"miniaudio", "sdl3", "cimgui", "cimguizmo", "bgfx"}), ("cpp", {"bimg"})):
        value = receipt("corpus-" + group, output / ("corpus-" + group + ".json"))
        if {item["libraryId"] for item in value.get("libraries", [])} != required or not all(
                item.get("generated") and item.get("managedCompiled") and item.get("reviewedSnapshots") for item in value["libraries"]):
            raise RuntimeError("The full expected corpus was not generated, compiled and reviewed: " + group)
        if value.get("selectedApiPolicy", {}).get("policySha256") != selection_digest:
            raise RuntimeError("The corpus receipt did not disclose its current exact source-selection policy: " + group)
    upstream = receipt("upstream-native", output / "upstream-native.json")
    required_upstream = {"miniaudio", "sdl3", "cimgui", "cimguizmo", "bgfx", "bimg"}
    if (upstream.get("bindingsInvoked") is not True
            or upstream.get("sourceBytesStable") is not True or upstream.get("source") != started["source"]
            or {item["libraryId"] for item in upstream.get("libraries", [])} != required_upstream
            or not all(item.get("nativeLinked") and item.get("nativeProbeInvoked") and item.get("bindingsInvoked")
                       and item.get("managedInvocation", {}).get("matchesIndependentNativeProbe")
                       for item in upstream["libraries"])):
        raise RuntimeError("All six generated bindings must match actual independently compiled upstream native probes.")
    if upstream.get("selectedApiPolicy", {}).get("policySha256") != selection_digest:
        raise RuntimeError("The native corpus did not disclose the same reviewed source-selection policy.")
    bimg = next(item for item in upstream["libraries"] if item["libraryId"] == "bimg")
    negative = bimg.get("upstreamLimitationNegativeBuild", {})
    if negative.get("nativeLinkRejected") is not True or negative.get("priorPublicationPreserved") is not True:
        raise RuntimeError("The upstream's missing exact overload must fail native linking and preserve the prior owned publication.")
    packages = receipt("installed-products", ROOT / "artifacts/verification/packages/verification.json")
    if "installed-tool-all-unified-generation-commands" not in packages.get("checks", []):
        raise RuntimeError("The installed SDK alone cannot certify the CLI tool gate.")
    performance_candidates = sorted((ROOT / "artifacts/performance").glob("*/report.json"), key=lambda path: path.stat().st_mtime)
    if not performance_candidates:
        raise RuntimeError("The performance gate did not produce an actual timing receipt.")
    performance = receipt("performance", performance_candidates[-1])
    for name, path in (("vulnerabilities", ROOT / "artifacts/supply-chain/nuget-vulnerabilities.json"),
                       ("dependency-licenses", ROOT / "artifacts/supply-chain/dependency-licenses.json")):
        receipt(name, path, success=False)
    revision = started["source"]["revision"]
    dirty = started["source"]["workingTreeDirty"]
    result = {"schemaVersion": 3, "status": "passed", "scope": "one-host-maintenance-candidate",
              "platform": platform, "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
              "source": started["source"], "usesPrebuiltOutputs": started["usesPrebuiltOutputs"], "releaseReady": False,
              "completeProductSet": packages.get("completeProductSet") is True,
              "upstreamNativeInvocationCertified": True,
              "categories": [{"name": name, "status": "passed", "gates": gates} for name, gates in CATEGORIES.items()],
              "executedTests": tests, "receiptInputs": inputs,
              "limitations": [
                  "This report certifies only its recorded host invocation; independent desktop, browser, AOT and installed-product workflows remain required.",
                  "The upstream receipt certifies its finite recorded CPU-side API fixtures; the bgfx Noop fixture does not certify GPU rendering.",
                  "A partial host package set is never full six-RID product certification.",
                  "Runtime callback/async helper tests do not certify automatic friendly projection of every callback or async protocol.",
                  "No signed publication or release attestation is executed by this local report."]}
    output.mkdir(parents=True, exist_ok=True)
    text = json.dumps(result, indent=2) + "\n"
    (output / "report.json").write_text(text, encoding="utf-8")
    markdown = "# BGCS host acceptance evidence\n\n" + "\n".join((
        "- Host: `" + platform + "`", "- Status: **passed for the recorded host scope**",
        "- Source: `" + revision + "` (dirty: `" + str(dirty).lower() + "`)",
        "- Executed passing tests: " + str(sum(item["passed"] for item in tests)),
        "- Full product set: `" + str(result["completeProductSet"]).lower() + "`",
        "- Upstream native invocation certified for the recorded fixtures: `true`", "- Release ready: `false`")) + "\n\n"
    markdown += "Every listed category completed in this invocation. Exact TRX and structured receipt hashes are in report.json.\n\n"
    markdown += "\n".join("- " + item for item in result["limitations"]) + "\n"
    (output / "report.md").write_text(markdown, encoding="utf-8")
    retained = output / "reports" / platform / args.gate_root.name
    retained.mkdir(parents=True, exist_ok=True)
    (retained / "report.json").write_text(text, encoding="utf-8")
    (retained / "report.md").write_text(markdown, encoding="utf-8")
    print("[acceptance] Recorded exact one-host evidence: " + str(output / "report.json"))


if __name__ == "__main__":
    main()
