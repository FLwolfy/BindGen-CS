"""Join authenticated workflow observations with actual installed execution archives."""
from __future__ import annotations

import hashlib
import importlib.util
from pathlib import Path
import re
import zipfile

PROOF_ID = "desktop-browser-nativeaot"
FEATURE_ID = "validation.desktop-wasm-and-nativeaot"
HOST_RIDS = {"windows-x64": "win-x64", "linux-x64": "linux-x64", "macos-x64": "osx-x64"}
SPEC = importlib.util.spec_from_file_location("global_product_verifier", Path(__file__).with_name("verify-ci-product-evidence.py"))
PRODUCT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PRODUCT)


def checked_run(value: dict, workflow: str, required_jobs: list[str], revision: str, package: bool = False) -> dict:
    if (value.get("workflow") != workflow or not isinstance(value.get("id"), int) or value["id"] <= 0
            or value.get("status") != "completed" or value.get("conclusion") != "success"
            or value.get("headBranch") != "main" or value.get("event") != ("workflow_run" if package else "push")
            or (not package and value.get("headSha") != revision)
            or (package and value.get("displayTitle") != "BGCS installed products at " + revision)):
        raise RuntimeError("Global feature evidence lacks the exact successful main-branch workflow observation: " + workflow)
    jobs = value.get("jobs", [])
    names = [job.get("name") for job in jobs]
    if (not jobs or len(set(names)) != len(names) or not set(required_jobs).issubset(names)
            or any(job.get("status") != "completed" or job.get("conclusion") != "success" for job in jobs)):
        raise RuntimeError("A required global feature job is missing, duplicated, failed, skipped or incomplete: " + workflow)
    return {job["name"]: job for job in jobs}


def verify_global_features(gates: dict, evidence: dict, archives: dict[str, Path], revision: str, content: str) -> dict:
    """Only the trusted release workflow creates observations; archive assertions are never trusted as proof."""
    if (evidence.get("schemaVersion") != 1 or evidence.get("sourceRevision") != revision
            or not re.fullmatch(r"[0-9a-f]{64}", content)):
        raise RuntimeError("Global workflow evidence belongs to a different source revision.")
    ci_jobs = checked_run(evidence.get("ci", {}), gates["workflow"], gates["requiredJobNames"], revision)
    checked_run(evidence.get("products", {}), gates["productPackageWorkflow"], gates["requiredProductPackageJobNames"], revision, package=True)
    requirements = gates.get("requiredProductEvidence", [])
    artifacts = [item.get("artifact") for item in requirements]
    if (not artifacts or len(set(artifacts)) != len(artifacts) or set(archives) != set(artifacts)
            or set(evidence.get("archiveSha256", {})) != set(artifacts)):
        raise RuntimeError("Global feature evidence requires every exact installed-product artifact, including its dedicated TaskHost host.")
    validated = {}
    for requirement in requirements:
        name = requirement["artifact"]
        path = archives[name]
        if not path.is_file() or path.stat().st_size > 64 * 1024 ** 2:
            raise RuntimeError("A global product archive is missing or exceeds its bounded input size: " + name)
        with path.open("rb") as stream:
            archive_hash = hashlib.file_digest(stream, "sha256").hexdigest()
        if evidence["archiveSha256"][name] != archive_hash:
            raise RuntimeError("A downloaded global product archive differs from its workflow observation: " + name)
        receipt = PRODUCT.read_archive(path)
        PRODUCT.validate(receipt, revision, requirement["hostRid"], requirement.get("requireVs2026", False))
        if receipt["checkedOutSourceSha256"] != content:
            raise RuntimeError("Global installed execution used different checked-out source bytes: " + name)
        with zipfile.ZipFile(path) as archive:
            receipt_hash = hashlib.sha256(archive.read("verification.json")).hexdigest()
        validated[name] = {"artifact": name, "hostRid": requirement["hostRid"], "archiveSha256": archive_hash,
            "verificationSha256": receipt_hash, "matrices": [{"reportId": matrix["name"],
                "receiptSha256": matrix["receiptSha256"], "executionIdentity": matrix["executionIdentity"]}
                for matrix in receipt["featureMatrices"]]}
    definitions = gates.get("globalFeatureProofs", {})
    if set(definitions) != {PROOF_ID}:
        raise RuntimeError("The global feature proof must name its finite reviewed proof identity.")
    definition = definitions[PROOF_ID]
    hosts = {item["host"] for item in gates["requiredFeatureEvidence"]}
    if (hosts != set(HOST_RIDS) or set(definition.get("hosts", {})) != hosts
            or set(definition.get("requiredReports", [])) != set(PRODUCT.MATRICES)
            or len(definition["requiredReports"]) != len(PRODUCT.MATRICES)):
        raise RuntimeError("Global feature proof cannot omit a launch host or installed execution report.")
    proofs = {}
    for host, requirement in definition["hosts"].items():
        jobs = requirement.get("jobs", [])
        artifact = requirement.get("artifact")
        if (not jobs or len(set(jobs)) != len(jobs) or not set(jobs).issubset(gates["requiredJobNames"])
                or not set(jobs).issubset(ci_jobs) or artifact not in validated
                or requirement.get("hostRid") != HOST_RIDS[host]
                or validated[artifact]["hostRid"] != requirement.get("hostRid")):
            raise RuntimeError("A global host lacks its exact independent gates and installed reports: " + host)
        proofs[host] = {"status": "verified", "sourceRevision": revision, "checkedOutSourceSha256": content,
            "ciRunId": evidence["ci"]["id"], "productRunId": evidence["products"]["id"],
            "jobs": jobs, **validated[artifact]}
    return {PROOF_ID: {"hosts": proofs, "installedArtifacts": list(validated.values()),
        "ciRunId": evidence["ci"]["id"], "productRunId": evidence["products"]["id"]}}
