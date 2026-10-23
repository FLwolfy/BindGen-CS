#!/usr/bin/env python3
"""Guard and owned-child controls; these tests never run a generator or install a tool."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("failed_performance_profile", ROOT / "scripts/profile-failed-performance.py")
PROFILE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROFILE)


class FailedPerformanceProfileTests(unittest.TestCase):
    def setUp(self):
        parent = ROOT / "artifacts/temporary"
        parent.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(prefix="failed-profile-", dir=parent)
        self.addCleanup(self.temporary.cleanup)
        self.work = Path(self.temporary.name)
        self.source = {"revision": "a" * 40, "checkedOutSourceSha256": "b" * 64}
        budgets = json.loads((ROOT / "eng/acceptance/performance-budgets.json").read_text())
        host = "macos-x64" if PROFILE.platform.system() == "Darwin" and PROFILE.platform.machine().lower() not in ("arm64", "aarch64") else "windows-x64" if PROFILE.os.name == "nt" else "linux-x64"
        expected = budgets["hosts"][host]
        self.failure = {"schemaVersion": 1, "success": False, "declarations": 10000,
            "budgetManifestSha256": PROFILE.digest(ROOT / "eng/acceptance/performance-budgets.json"),
            "coldBudgetSeconds": expected["coldSeconds"], "warmBudgetSeconds": expected["warmSeconds"],
            "execution": {"sourceRevision": self.source["revision"], "checkedOutSourceSha256": self.source["checkedOutSourceSha256"]}}

    def report(self, name="first", value=None):
        path = self.work / name / "report.json"
        path.parent.mkdir()
        path.write_text(json.dumps(self.failure if value is None else value), encoding="utf-8")
        return path

    def test_missing_or_passing_latest_report_never_retries_an_older_failure(self):
        self.assertEqual("skipped", PROFILE.failure_guard(self.work, self.source)[0])
        old = self.report()
        latest = self.report("latest", {**self.failure, "success": True})
        PROFILE.os.utime(old, (1, 1))
        PROFILE.os.utime(latest, (2, 2))
        status, _, selected, _ = PROFILE.failure_guard(self.work, self.source)
        self.assertEqual(("skipped", latest), (status, selected))

    def test_exact_failed_source_is_eligible_and_original_bytes_remain_unchanged(self):
        path = self.report()
        before = path.read_bytes()
        self.assertEqual("eligible", PROFILE.failure_guard(self.work, self.source)[0])
        self.assertEqual(before, path.read_bytes())

    def test_mismatched_source_or_nonoriginal_failure_is_rejected(self):
        mutations = (
            {"execution": {**self.failure["execution"], "sourceRevision": "c" * 40}},
            {"execution": {**self.failure["execution"], "checkedOutSourceSha256": "d" * 64}},
            {"diagnosticOnly": True}, {"success": None}, {"declarations": 1},
            {"coldBudgetSeconds": 180}, {"warmBudgetSeconds": 180}, {"budgetManifestSha256": "e" * 64},
        )
        for index, mutation in enumerate(mutations):
            with self.subTest(mutation=mutation):
                folder = self.work / str(index)
                folder.mkdir()
                path = folder / "one"
                path.mkdir()
                (path / "report.json").write_text(json.dumps({**self.failure, **mutation}))
                self.assertEqual("rejected", PROFILE.failure_guard(folder, self.source)[0])

    def test_no_failure_main_writes_noncertifying_noop_without_staging_or_process(self):
        output = self.work / "diagnostics"
        with patch.object(PROFILE, "source_identity", return_value=self.source), \
                patch.object(PROFILE.fixture, "stage_repository_generator") as stage, \
                patch.object(PROFILE.subprocess, "Popen") as process:
            self.assertEqual(0, PROFILE.main(["--dotnet", str(self.work / "missing-dotnet"),
                "--performance-root", str(self.work / "absent"), "--output", str(output)]))
        stage.assert_not_called()
        process.assert_not_called()
        receipt = json.loads(next(output.glob("*/report.json")).read_text())
        self.assertEqual("skipped", receipt["status"])
        self.assertTrue(receipt["diagnosticOnly"])
        self.assertFalse(receipt["releaseCertification"])
        self.assertFalse(receipt["success"])

    def test_child_environment_keeps_runtime_identity_without_mutating_parent(self):
        original = {"PATH": "discovery", "CPATH": "injected", "Platform": "x64",
                    "DOTNET_DiagnosticPorts": "caller-security-setting", "DOTNET_ROOT": "old"}
        with patch.dict(PROFILE.os.environ, original, clear=True):
            before = dict(PROFILE.os.environ)
            actual = PROFILE.child_environment(self.work / "dotnet")
            self.assertEqual(before, dict(PROFILE.os.environ))
        self.assertNotIn("CPATH", actual)
        self.assertNotIn("Platform", actual)
        self.assertEqual("discovery", actual["PATH"])
        normalized = {key.casefold(): value for key, value in actual.items()}
        self.assertEqual(original["DOTNET_DiagnosticPorts"], normalized["dotnet_diagnosticports"])
        self.assertEqual(str(self.work), actual["DOTNET_ROOT"])

    def test_start_passes_unchanged_environment_and_actual_child_pid(self):
        environment = {"PATH": "actual", "DOTNET_ROOT": "locked"}
        process, parent = Mock(pid=123), Mock()
        metrics = Mock()
        metrics.Process.return_value = parent
        children = []
        with patch.object(PROFILE.subprocess, "Popen", return_value=process) as start:
            entry = PROFILE.start_owned(["dotnet", "actual.dll"], self.work, environment, Mock(), children, metrics)
        self.assertIs(environment, start.call_args.kwargs["env"])
        self.assertEqual(["dotnet", "actual.dll"], start.call_args.args[0])
        self.assertIs(process, entry[0])
        self.assertEqual([entry], children)
        metrics.Process.assert_called_once_with(123)

    def test_timeout_reaps_only_owned_parent_and_observed_descendant_with_global_deadline(self):
        class Gone(Exception):
            pass
        metrics = Mock(NoSuchProcess=Gone)
        process = Mock(pid=123)
        process.poll.return_value = None
        parent = Mock(pid=123)
        child = Mock(pid=456)
        parent.create_time.return_value = 1
        child.create_time.return_value = 2
        parent.children.return_value = [child]
        entry = (process, parent, {})
        with patch.object(PROFILE, "check_space"):
            with self.assertRaises(TimeoutError):
                PROFILE.wait_owned(entry, self.work, 0, metrics)
        metrics.wait_procs.return_value = ([], [])
        PROFILE.reap_owned([entry], metrics)
        parent.kill.assert_called_once()
        child.kill.assert_called_once()
        process.kill.assert_called_once()
        self.assertLessEqual(metrics.wait_procs.call_args.kwargs["timeout"], 30)
        self.assertLessEqual(process.wait.call_args.kwargs["timeout"], 30)

    def test_reap_caps_floating_point_roundoff_without_relaxing_the_deadline(self):
        metrics = Mock()
        metrics.wait_procs.return_value = ([], [])
        process = Mock(pid=123)
        process.poll.return_value = None
        # This exact float reproduces the CI allowance of 30.00000000000003.
        self.assertGreater((240.1 + PROFILE.REAP_SECONDS) - 240.1, PROFILE.REAP_SECONDS)
        with patch.object(PROFILE.time, "monotonic", side_effect=[240.1, 240.1, 240.1]):
            PROFILE.reap_owned([(process, None, {})], metrics)
        self.assertEqual(PROFILE.REAP_SECONDS, metrics.wait_procs.call_args.kwargs["timeout"])
        process.wait.assert_called_once_with(timeout=PROFILE.REAP_SECONDS)

    def test_reap_does_not_restart_the_shared_budget_after_it_expires(self):
        metrics = Mock()
        metrics.wait_procs.return_value = ([], [])
        processes = [Mock(pid=123), Mock(pid=456)]
        for process in processes:
            process.poll.return_value = None
        with patch.object(PROFILE.time, "monotonic", side_effect=[1000, 1029.5, 1031, 1032]):
            PROFILE.reap_owned([(process, None, {}) for process in processes], metrics)
        self.assertEqual(0.5, metrics.wait_procs.call_args.kwargs["timeout"])
        for process in processes:
            process.kill.assert_called_once()
            process.wait.assert_called_once_with(timeout=0)

    def test_disk_floor_stops_before_any_diagnostic_child(self):
        with patch.object(PROFILE.shutil, "disk_usage", return_value=Mock(free=50 * 1024**3 - 1)):
            with self.assertRaisesRegex(RuntimeError, "50 GiB"):
                PROFILE.check_space(self.work)


if __name__ == "__main__":
    unittest.main()
