import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("check_runtime", Path(__file__).resolve().parents[2] / ".github/scripts/check_runtime.py")
runtime = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runtime)


class RuntimeTests(unittest.TestCase):
    def setUp(self):
        self.metadata = {"channel-version": "10.0", "latest-runtime": "10.0.15", "support-phase": "active",
                         "releases": [{"windowsdesktop": {"version": "10.0.15"}}]}

    def test_current_patch_passes_and_old_patch_fails(self):
        self.assertIn("are current", runtime.check_version("10.0.15", self.metadata))
        with self.assertRaisesRegex(ValueError, "latest 10.0.15"):
            runtime.check_version("10.0.14", self.metadata)

    def test_wrong_channel_unsupported_and_missing_desktop_fail(self):
        for key, value in [("channel-version", "9.0"), ("latest-runtime", "preview"),
                           ("support-phase", "eol"), ("releases", [])]:
            with self.subTest(key=key), self.assertRaises(ValueError):
                runtime.check_version("10.0.15", {**self.metadata, key: value})

    def test_reads_project_pin_and_rejects_floating_versions(self):
        with tempfile.TemporaryDirectory() as folder:
            project = Path(folder) / "app.csproj"
            for version in ["10.0.15", "10.0.*", "", "10.0.16-preview"]:
                project.write_text(f"<Project><PropertyGroup><RuntimeFrameworkVersion>{version}</RuntimeFrameworkVersion></PropertyGroup></Project>")
                if version == "10.0.15":
                    self.assertEqual(version, runtime.pinned_version(project))
                else:
                    with self.assertRaises(ValueError):
                        runtime.pinned_version(project)
