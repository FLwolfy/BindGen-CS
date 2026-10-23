#!/usr/bin/env python3
"""Verify public-feed evidence logic without a publish, download or installation."""
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import urllib.error
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("public_product_verifier", ROOT / "scripts/verify-published-products.py")
VERIFIER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFIER)


def archive_bytes(value=b"accepted assembly", signature=False):
    target = io.BytesIO()
    with zipfile.ZipFile(target, "w") as archive:
        archive.writestr("lib/net10.0/Accepted.dll", value)
        if signature:
            archive.writestr(".signature.p7s", b"repository signature transport difference")
    return target.getvalue()


class PublishedProductEvidenceTests(unittest.TestCase):
    def setUp(self):
        parent = ROOT / "artifacts/temporary"
        parent.mkdir(parents=True, exist_ok=True)
        if not parent.resolve().is_relative_to(ROOT):
            raise RuntimeError("The public evidence regression root escaped its workspace.")
        self.temporary = tempfile.TemporaryDirectory(prefix="published-product-proof-", dir=parent)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.local = self.root / "Accepted.3.0.0.nupkg"
        self.local.write_bytes(archive_bytes())
        self.download = self.root / "owned-current-download.nupkg"

    def verify(self, data):
        with patch.object(VERIFIER.urllib.request, "urlopen", return_value=io.BytesIO(data)):
            return VERIFIER.verify_public_payload(self.local, "Accepted", "3.0.0", self.download, 1024 * 1024)

    def test_repository_signature_addition_preserves_every_accepted_payload(self):
        self.assertEqual(1, self.verify(archive_bytes(signature=True))["payloadEntries"])
        self.assertFalse(self.download.exists())

    def test_existing_version_with_changed_payload_is_not_successful_publication(self):
        with self.assertRaisesRegex(RuntimeError, "different package payload bytes"):
            self.verify(archive_bytes(b"different assembly", signature=True))
        self.assertFalse(self.download.exists())

    def test_all_declared_package_indexes_must_be_available(self):
        requested, counts = [], {}
        def query(identity, timeout):
            requested.append(identity)
            counts[identity] = counts.get(identity, 0) + 1
            if identity == "BGCS.Runtime" and counts[identity] == 1:
                raise urllib.error.HTTPError("https://example.invalid", 404, "not indexed", None, None)
            return {"3.0.0"}
        VERIFIER.wait_for_index(["BGCS", "BGCS.Runtime", "BindGen-CS"], "3.0.0", 2, query=query, pause=lambda seconds: None)
        self.assertEqual({"BGCS", "BGCS.Runtime", "BindGen-CS"}, set(requested))
        self.assertEqual(2, counts["BGCS.Runtime"])

    def test_no_indexed_version_fails_within_its_declared_deadline(self):
        with self.assertRaisesRegex(RuntimeError, "indexing did not complete"):
            VERIFIER.wait_for_index(["BGCS"], "3.0.0", 0.001, query=lambda identity, timeout: set(), pause=lambda seconds: None)

    def test_public_consumer_uses_the_current_contract_schema_instead_of_a_package_version(self):
        schema = self.root / "extension.schema.json"
        for major in (4, 9):
            schema.write_text(json.dumps({"properties": {"plugin": {"properties": {
                "contractsMajor": {"const": major}}}}}), encoding="utf-8")
            self.assertEqual(major, VERIFIER.expected_contract_major(schema))

    def test_public_consumer_rejects_missing_or_invalid_contract_identity(self):
        schema = self.root / "extension.schema.json"
        for value in (None, True, 0, -1, 4.5, "4"):
            with self.subTest(value=value):
                schema.write_text(json.dumps({"properties": {"plugin": {"properties": {
                    "contractsMajor": {"const": value}}}}}), encoding="utf-8")
                with self.assertRaisesRegex(RuntimeError, "positive Core contract major"):
                    VERIFIER.expected_contract_major(schema)
        schema.write_text("{}", encoding="utf-8")
        with self.assertRaises(KeyError):
            VERIFIER.expected_contract_major(schema)


if __name__ == "__main__":
    unittest.main(verbosity=2)
