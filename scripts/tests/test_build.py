"""Regression tests for release packaging and failure handling (no SDK required)."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import zipfile

SPEC = importlib.util.spec_from_file_location("nmse_build", Path(__file__).parents[1] / "build.py")
build = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(build)


def payload(directory, rid):
    directory.mkdir(parents=True, exist_ok=True)
    names = ["NMSE.exe" if rid == "win-x64" else "NMSE", "NMSE.dll", "NMSE.deps.json", "version.json"]
    names += ["coreclr.dll" if rid == "win-x64" else ("libcoreclr.dylib" if rid.startswith("osx-") else "libcoreclr.so")]
    for name in names:
        (directory / name).write_text("fixture", encoding="utf-8")
    (directory / "NMSE.runtimeconfig.json").write_text(
        json.dumps({"runtimeOptions": {"includedFrameworks": [{"name": "Microsoft.NETCore.App", "version": "10.0.0"}]}}),
        encoding="utf-8")
    for name in ("json", "images", "ui"):
        target = directory / "Resources" / name
        target.mkdir(parents=True)
        (target / "data.txt").write_text(name, encoding="utf-8")


class BuildTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="nmse-build-test-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "version.json").write_text('{"major":1,"minor":4,"patch":29}', encoding="utf-8")
        self.root_patch = patch.object(build, "ROOT", self.root)
        self.root_patch.start()
        self.addCleanup(self.root_patch.stop)

    def test_all_targets_include_both_macos_architectures(self):
        self.assertEqual(build.select_rids("all"), ("win-x64", "linux-x64", "osx-arm64", "osx-x64"))

    def test_platform_and_explicit_runtime_selection(self):
        self.assertEqual(build.select_rids("macos"), ("osx-arm64", "osx-x64"))
        self.assertEqual(build.select_rids("windows"), ("win-x64",))
        self.assertEqual(build.select_rids("linux-x64"), ("linux-x64",))

    def test_native_macos_architectures(self):
        with patch.object(build.platform, "system", return_value="Darwin"):
            for architecture, rid in (("arm64", "osx-arm64"), ("x86_64", "osx-x64")):
                with patch.object(build.platform, "machine", return_value=architecture):
                    self.assertEqual(build.select_rids("native"), (rid,))

    def test_native_linux_arm_requires_explicit_supported_target(self):
        with patch.object(build.platform, "system", return_value="Linux"), patch.object(build.platform, "machine", return_value="aarch64"):
            with self.assertRaisesRegex(ValueError, "explicit target"):
                build.select_rids("native")

    def test_version_comes_from_repository(self):
        self.assertEqual(build.read_version(), "1.4.29")

    def test_version_rejects_boolean_negative_and_string(self):
        for bad in (True, -1, "29"):
            (self.root / "version.json").write_text(json.dumps({"major": 1, "minor": 4, "patch": bad}), encoding="utf-8")
            with self.assertRaises(ValueError):
                build.read_version()

    def test_missing_sdk_is_actionable(self):
        with patch.object(build.shutil, "which", return_value=None):
            with self.assertRaisesRegex(ValueError, ".NET 10 SDK"):
                build.preflight(())

    def test_all_on_linux_fails_before_tests_or_output(self):
        output = self.root / "output"
        with patch.object(build.platform, "system", return_value="Linux"), patch.object(build.shutil, "which", return_value="/tool"), patch.object(build, "test") as tests:
            with self.assertRaisesRegex(ValueError, "requires a Mac"):
                build.publish(build.RIDS, "Release", output)
        tests.assert_not_called()
        self.assertFalse(output.exists())

    def test_notarization_requires_signing_identity(self):
        with patch.object(build.platform, "system", return_value="Darwin"), patch.object(build.shutil, "which", return_value="/tool"), patch.dict(build.os.environ, {"MACOS_NOTARY_PROFILE": "profile"}, clear=True):
            with self.assertRaisesRegex(ValueError, "Developer ID"):
                build.preflight(("osx-arm64",))

    def test_cross_platform_test_projects(self):
        with patch.object(build.platform, "system", return_value="Linux"), patch.object(build, "run") as run:
            build.test("Release")
        self.assertEqual(run.call_count, 3)
        self.assertIn("NMSE.Tests/NMSE.Tests.csproj", run.call_args_list[0].args)
        self.assertEqual(run.call_args_list[1].args[:2], ("dotnet", "run"))
        self.assertIn("NMSE.MacUpdater.Tests/NMSE.MacUpdater.Tests.csproj", run.call_args_list[2].args)

    def test_windows_includes_extractor_tests(self):
        with patch.object(build.platform, "system", return_value="Windows"), patch.object(build, "run") as run:
            build.test("Debug")
        self.assertEqual(run.call_count, 4)
        self.assertIn("NMSE.Extractor.Tests/NMSE.Extractor.Tests.csproj", run.call_args.args)

    def test_payload_requires_runtime(self):
        directory = self.root / "publish"
        payload(directory, "linux-x64")
        (directory / "libcoreclr.so").unlink()
        with self.assertRaisesRegex(ValueError, "libcoreclr.so"):
            build.validate_payload(directory, "linux-x64")

    def test_payload_rejects_framework_dependent_config(self):
        directory = self.root / "publish"
        payload(directory, "win-x64")
        (directory / "NMSE.runtimeconfig.json").write_text(
            json.dumps({"runtimeOptions": {"framework": {"name": "Microsoft.NETCore.App"}}}), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "not self-contained"):
            build.validate_payload(directory, "win-x64")

    def test_zip_has_executable_and_resources_at_root(self):
        directory = self.root / "publish"
        payload(directory, "win-x64")
        archive = self.root / "release.zip"
        build.write_archive(directory, archive, "win-x64")
        with zipfile.ZipFile(archive) as source:
            self.assertIsNone(source.testzip())
            self.assertIn("NMSE.exe", source.namelist())
            self.assertIn("Resources/json/data.txt", source.namelist())
            self.assertFalse(any(name.startswith("/") or ".." in Path(name).parts for name in source.namelist()))

    def test_linux_tar_preserves_executable_mode_after_windows_publish(self):
        directory = self.root / "publish"
        payload(directory, "linux-x64")
        (directory / "NMSE").chmod(0o644)
        archive = self.root / "release.tar.gz"
        build.write_archive(directory, archive, "linux-x64")
        with tarfile.open(archive) as source:
            self.assertEqual(source.getmember("NMSE").mode, 0o755)
            self.assertIn("Resources/images/data.txt", source.getnames())

    def fake_publish(self, *args, **kwargs):
        if args[:2] == ("dotnet", "publish"):
            rid = args[args.index("--runtime") + 1]
            destination = Path(args[args.index("--output") + 1])
            payload(destination, rid)

    def test_publish_runs_tests_once_for_multiple_targets(self):
        with patch.object(build, "preflight"), patch.object(build, "test") as tests, patch.object(build, "run", side_effect=self.fake_publish):
            build.publish(("win-x64", "linux-x64"), "Release", self.root / "output")
        tests.assert_called_once_with("Release")
        names = {path.name for path in (self.root / "output/artifacts").iterdir()}
        self.assertEqual(names, {"NMSE-1.4.29-windows-x64.zip", "NMSE-1.4.29-linux-x64.tar.gz"})

    def test_skip_tests_is_explicit(self):
        with patch.object(build, "preflight"), patch.object(build, "test") as tests, patch.object(build, "run", side_effect=self.fake_publish):
            build.publish(("win-x64",), "Release", self.root / "output", skip_tests=True)
        tests.assert_not_called()

    def test_publish_does_not_archive_stale_files(self):
        output = self.root / "output"
        previous = output / "publish/win-x64"
        previous.mkdir(parents=True)
        (previous / "removed-resource.txt").write_text("old", encoding="utf-8")
        with patch.object(build, "preflight"), patch.object(build, "test"), patch.object(build, "run", side_effect=self.fake_publish) as run:
            build.publish(("win-x64",), "Release", output)
        with zipfile.ZipFile(output / "artifacts/NMSE-1.4.29-windows-x64.zip") as archive:
            self.assertNotIn("removed-resource.txt", archive.namelist())
        self.assertFalse((previous / "removed-resource.txt").exists())
        self.assertIn("-p:Version=1.4.29", run.call_args.args)
        self.assertIn("-p:PublishAot=false", run.call_args.args)
        self.assertIn("-p:UseAppHost=true", run.call_args.args)

    def test_failed_publish_preserves_previous_payload_and_archive(self):
        output = self.root / "output"
        previous = output / "publish/win-x64"
        previous.mkdir(parents=True)
        (previous / "old.txt").write_text("old", encoding="utf-8")
        artifacts = output / "artifacts"
        artifacts.mkdir()
        archive = artifacts / "NMSE-1.4.29-windows-x64.zip"
        archive.write_bytes(b"previous release")
        with patch.object(build, "preflight"), patch.object(build, "test"), patch.object(build, "run", side_effect=subprocess.CalledProcessError(1, "dotnet")):
            with self.assertRaises(subprocess.CalledProcessError):
                build.publish(("win-x64",), "Release", output)
        self.assertEqual((previous / "old.txt").read_text(), "old")
        self.assertEqual(archive.read_bytes(), b"previous release")
        self.assertFalse(list(output.glob(".win-x64-*")))

    def test_failed_tests_prevent_publish(self):
        with patch.object(build, "preflight"), patch.object(build, "test", side_effect=subprocess.CalledProcessError(1, "dotnet test")), patch.object(build, "run") as run:
            with self.assertRaises(subprocess.CalledProcessError):
                build.publish(("win-x64",), "Release", self.root / "output")
        run.assert_not_called()
        self.assertFalse((self.root / "output").exists())

    def test_foreign_macos_architecture_is_not_launched(self):
        with patch.object(build, "native_rid", return_value="osx-arm64"), patch.object(build, "run") as run:
            build.smoke_test(self.root / "release.zip", "osx-x64")
        run.assert_not_called()

    def test_main_propagates_subprocess_failure(self):
        with patch.object(build, "preflight"), patch.object(build, "run", side_effect=subprocess.CalledProcessError(7, "dotnet")), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(build.main(["build"]), 1)

    def test_help_works_from_another_directory_without_sdk(self):
        result = subprocess.run([sys.executable, str(Path(__file__).parents[1] / "build.py"), "publish", "--help"],
                                cwd=self.root, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0)
        self.assertIn("all", result.stdout)


if __name__ == "__main__":
    unittest.main()
