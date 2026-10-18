"""Validate disclosed corpus source selection against exact locked upstream limitations."""
from __future__ import annotations

import json
from pathlib import Path

from acceptance_evidence import digest


def selection_policy(root: Path, lock: dict, corpus: Path) -> dict:
    path = root / "tests/corpus/upstream-limitations.json"
    policy = json.loads(path.read_text(encoding="utf-8"))
    if policy.get("schemaVersion") != 1:
        raise RuntimeError("The corpus source-selection policy revision is unsupported.")
    configurations = {item.name.removesuffix(".bindgen.json"): item for item in (root / "tests/corpus").glob("*.bindgen.json")}
    if set(configurations) != set(policy["libraries"]):
        raise RuntimeError("Every corpus configuration must have one explicit source-selection policy.")
    repositories = {entry["id"]: entry for entry in lock["repositories"]}
    for library, config in configurations.items():
        value = json.loads(config.read_text(encoding="utf-8"))
        excluded = value["source"].get("selection", {}).get("excludeDeclarations", [])
        declared = policy["libraries"][library]
        if excluded != declared["excludedDeclarations"]:
            raise RuntimeError("Corpus exclusions changed without an audited exact policy update: " + library)
        for limitation in declared.get("upstreamLimitations", []) + declared.get("compilerConditionalHelpers", []):
            repository = repositories[limitation["repository"]]
            header = corpus / repository["id"] / limitation["header"]
            if repository["revision"] != limitation["revision"] or digest(header) != limitation["headerSha256"]:
                raise RuntimeError("An upstream limitation no longer matches its reviewed source revision and bytes.")
            lines = header.read_text(encoding="utf-8").splitlines()
            declaration = "\n".join(lines[limitation["line"] - 1:limitation["lastLine"]]).strip()
            if declaration != limitation["declaration"].strip() or limitation["sourceSignature"] not in excluded:
                raise RuntimeError("An upstream limitation's exact declaration or signature changed.")
            if "portableCounterpart" in limitation and limitation["portableCounterpart"] in excluded:
                raise RuntimeError("A compiler-only helper exclusion must preserve its portable public operation.")
    return {"schemaVersion": policy["schemaVersion"], "policySha256": digest(path), "scope": policy["scope"],
            "libraries": policy["libraries"]}
