"""Desktop error handling without credentials, network, or a live model."""
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class DesktopEntryTests(unittest.TestCase):
    def test_demo_runs_through_desktop_entry(self):
        with tempfile.TemporaryDirectory() as td:
            log = Path(td) / 'error.log'
            result = subprocess.run([sys.executable, str(ROOT / 'desktop_entry.py'), str(log), 'demo', 'message'], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn('SIMULATION', result.stdout)
            self.assertFalse(log.exists())

    def test_missing_project_is_reported_to_gui(self):
        with tempfile.TemporaryDirectory() as td:
            log = Path(td) / 'error.log'
            result = subprocess.run([sys.executable, str(ROOT / 'desktop_entry.py'), str(log), 'plan', '--task', 'x', '--project', str(Path(td) / 'missing')], capture_output=True, text=True)
            self.assertEqual(result.returncode, 2)
            self.assertIn('Projektverzeichnis fehlt', log.read_text(encoding='utf-8'))

    def test_existing_error_log_is_not_overwritten(self):
        with tempfile.TemporaryDirectory() as td:
            log = Path(td) / 'error.log'; log.write_text('preserve', encoding='utf-8')
            result = subprocess.run([sys.executable, str(ROOT / 'desktop_entry.py'), str(log), 'plan', '--task', 'x', '--project', str(Path(td) / 'missing')], capture_output=True, text=True)
            self.assertEqual(result.returncode, 2)
            self.assertEqual(log.read_text(), 'preserve')
