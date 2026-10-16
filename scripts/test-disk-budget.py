"""Pure controls; no subprocesses are launched or killed by these fixtures."""
import os
from pathlib import Path
import subprocess
import unittest
from unittest.mock import Mock, patch
import disk_budget


class DiskBudgetTests(unittest.TestCase):
    def test_positive_floor_is_mandatory(self):
        for value in ('0', '-1', 'invalid'):
            with patch.dict(os.environ, {'BGCS_MINIMUM_FREE_BYTES': value}), self.assertRaises((ValueError, RuntimeError)):
                disk_budget.check_free_space(Path.cwd())

    def test_insufficient_space_fails_before_wait(self):
        process = Mock(args=['owned-child'])
        with patch.dict(os.environ, {'BGCS_MINIMUM_FREE_BYTES': '100'}), patch.object(disk_budget.shutil, 'disk_usage', return_value=Mock(free=99)):
            with self.assertRaises(RuntimeError): disk_budget.wait_for_process(process, Path.cwd())
        process.wait.assert_not_called()

    def test_floor_is_rechecked_during_running_child(self):
        process = Mock(args=['owned-child'])
        process.wait.side_effect = subprocess.TimeoutExpired(process.args, 1)
        with patch.dict(os.environ, {'BGCS_MINIMUM_FREE_BYTES': '100'}), patch.object(disk_budget.shutil, 'disk_usage', side_effect=[Mock(free=101), Mock(free=99)]):
            with self.assertRaises(RuntimeError): disk_budget.wait_for_process(process, Path.cwd())
        self.assertEqual(process.wait.call_count, 1)

    def test_success_and_timeout_preserve_actual_child_result(self):
        with patch.object(disk_budget, 'check_free_space'):
            process = Mock(args=['owned-child']); process.wait.return_value = 3
            self.assertEqual(disk_budget.wait_for_process(process, Path.cwd()), 3)
            with self.assertRaises(subprocess.TimeoutExpired): disk_budget.wait_for_process(process, Path.cwd(), -1)


if __name__ == '__main__': unittest.main()
