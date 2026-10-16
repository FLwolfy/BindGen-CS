"""Validate the distinct nonblocking browser obligations without accepting raw-call counts as proof."""
from cpp_acceptance_fixture import expected_lifecycle_checks
import re


EXECUTION_FIELDS = ("executionClosureSha256", "generatorEntrySha256", "generatorAssemblySha256", "runtimeAssemblySha256")


def verify_execution_identity(identity):
    if (not isinstance(identity, dict)
            or any(not isinstance(identity.get(field), str) or not re.fullmatch(r"[0-9a-f]{64}", identity[field]) for field in EXECUTION_FIELDS)
            or type(identity.get("executionAssetCount")) is not int or identity["executionAssetCount"] <= 0):
        raise RuntimeError("The installed matrix lacks a complete pinned generator/runtime execution identity.")
    return {field: identity[field] for field in (*EXECUTION_FIELDS, "executionAssetCount")}


def verify_summary(summary):
    expected = expected_lifecycle_checks()
    if (not isinstance(summary, dict) or summary.get("success") is not True
            or summary.get("requiredCount") != len(expected)
            or not isinstance(summary.get("checks"), list)
            or len(summary["checks"]) != len(expected)
            or any(not isinstance(check, str) for check in summary["checks"])
            or set(summary["checks"]) != expected):
        raise RuntimeError("The browser nonblocking retained/async lifecycle matrix is incomplete.")
    return {"success": True, "checks": list(summary["checks"]), "requiredCount": len(expected)}


def verify_receipt(receipt):
    summary = verify_summary(receipt.get("nonblockingLifecycle"))
    invocation = receipt.get("invocation", {})
    actual = invocation.get("lifecycleChecks")
    if (invocation.get("success") is not True or not isinstance(actual, list)
            or len(actual) != summary["requiredCount"]
            or any(not isinstance(check, str) for check in actual)
            or set(actual) != set(summary["checks"])):
        raise RuntimeError("The browser invocation and nonblocking lifecycle subreport disagree.")
    return summary
