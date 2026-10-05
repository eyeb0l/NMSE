#!/usr/bin/env python3
"""Build, test and package NMSE using only Python's standard library."""
import argparse
import json
import os
from pathlib import Path
import platform
import plistlib
import shlex
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
import zipfile

ROOT = Path(__file__).resolve().parent.parent
TARGETS = {
    "windows": ("win-x64",),
    "linux": ("linux-x64",),
    "macos": ("osx-arm64", "osx-x64"),
    "all": ("win-x64", "linux-x64", "osx-arm64", "osx-x64"),
}
RIDS = TARGETS["all"]
TEST_PROJECTS = (
    "NMSE.Tests/NMSE.Tests.csproj",
    "NMSE.Avalonia.Tests/NMSE.Avalonia.Tests.csproj",
    "NMSE.MacUpdater.Tests/NMSE.MacUpdater.Tests.csproj",
)


def native_rid():
    system = platform.system()
    if system == "Darwin":
        return "osx-arm64" if platform.machine().lower() in ("arm64", "aarch64") else "osx-x64"
    if system == "Windows" and platform.machine().lower() in ("amd64", "x86_64"):
        return "win-x64"
    if system == "Linux" and platform.machine().lower() in ("amd64", "x86_64"):
        return "linux-x64"
    raise ValueError("No release target for this host; choose an explicit target.")


def select_rids(target):
    if target == "native":
        return (native_rid(),)
    return TARGETS[target] if target in TARGETS else (target,)


def read_version():
    data = json.loads((ROOT / "version.json").read_text(encoding="utf-8"))
    parts = [data[name] for name in ("major", "minor", "patch")]
    if any(type(part) is not int or part < 0 for part in parts):
        raise ValueError("version.json must contain non-negative integer major/minor/patch values.")
    return ".".join(map(str, parts))


def run(*args, env=None):
    command = [str(arg) for arg in args]
    print("+ " + shlex.join(command), flush=True)
    subprocess.run(command, cwd=ROOT, env=env, check=True)


def preflight(rids):
    if not shutil.which("dotnet"):
        raise ValueError("Install the .NET 10 SDK and make dotnet available on PATH.")
    if any(rid.startswith("osx-") for rid in rids):
        if platform.system() != "Darwin":
            raise ValueError("macOS .app/ZIP/DMG packaging requires a Mac. Use windows or linux here; CI builds all platforms.")
        for tool in ("bash", "codesign", "ditto", "file", "hdiutil", "plutil", "rsync", "sips", "unzip"):
            if not shutil.which(tool):
                raise ValueError(f"macOS packaging requires {tool} on PATH.")
        if os.environ.get("MACOS_NOTARY_PROFILE"):
            if os.environ.get("MACOS_SIGN_IDENTITY", "-") in ("", "-"):
                raise ValueError("MACOS_NOTARY_PROFILE requires a Developer ID MACOS_SIGN_IDENTITY.")
            if not shutil.which("xcrun"):
                raise ValueError("Notarization requires xcrun.")
        entitlements = os.environ.get("MACOS_ENTITLEMENTS")
        if entitlements and not Path(entitlements).is_file():
            raise ValueError(f"Entitlements file not found: {entitlements}")


def test(configuration):
    for project in TEST_PROJECTS:
        if project.startswith("NMSE.Avalonia.Tests/"):
            run("dotnet", "run", "--project", project, "--configuration", configuration,
                "--", "-noColor", "-noLogo")
        else:
            run("dotnet", "test", project, "--configuration", configuration, "--verbosity", "minimal")
    if platform.system() == "Windows":
        run("dotnet", "test", "NMSE.Extractor.Tests/NMSE.Extractor.Tests.csproj",
            "--configuration", configuration, "--verbosity", "minimal")


def validate_payload(directory, rid):
    required = [
        "NMSE.exe" if rid == "win-x64" else "NMSE",
        "NMSE.dll", "NMSE.deps.json", "NMSE.runtimeconfig.json", "version.json",
        "coreclr.dll" if rid == "win-x64" else ("libcoreclr.dylib" if rid.startswith("osx-") else "libcoreclr.so"),
        "Resources/json", "Resources/images", "Resources/ui",
    ]
    for name in required:
        if not (directory / name).exists():
            raise ValueError(f"Incomplete {rid} publish: missing {name}")
    config = json.loads((directory / "NMSE.runtimeconfig.json").read_text(encoding="utf-8"))["runtimeOptions"]
    if "includedFrameworks" not in config or "framework" in config or "frameworks" in config:
        raise ValueError(f"{rid} publish is not self-contained.")
    if rid != "win-x64":
        # Windows hosts do not retain Unix executable bits on cross-published files.
        (directory / "NMSE").chmod(0o755)


def write_archive(directory, destination, rid):
    if rid == "win-x64":
        with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for path in sorted(directory.rglob("*")):
                if path.is_file():
                    archive.write(path, path.relative_to(directory).as_posix())
        with zipfile.ZipFile(destination) as archive:
            bad_file = archive.testzip()
            if bad_file:
                raise ValueError(f"Corrupt ZIP entry: {bad_file}")
    else:
        def executable_mode(info):
            if info.name == "NMSE":
                info.mode = 0o755
            return info

        # Disable macOS metadata sidecars; preserve normal permissions and symlinks.
        with tarfile.open(destination, "w:gz") as archive:
            for path in sorted(directory.iterdir()):
                archive.add(path, arcname=path.name, filter=executable_mode)
        with tarfile.open(destination, "r:gz") as archive:
            if archive.getmember("NMSE").mode & 0o111 == 0:
                raise ValueError("Linux archive lost executable permissions.")


def smoke_test(archive, rid):
    if rid != native_rid():
        print(f"[smoke] Skipping {rid}: this host runs {native_rid()}.", flush=True)
        return
    with tempfile.TemporaryDirectory(prefix="nmse-launch-") as temporary:
        run("ditto", "-x", "-k", archive, temporary)
        app = Path(temporary) / "NMSE.app"
        with (app / "Contents/Info.plist").open("rb") as source:
            executable = plistlib.load(source)["CFBundleExecutable"]
        with tempfile.TemporaryFile(mode="w+b") as log:
            process = subprocess.Popen([str(app / "Contents/MacOS" / executable)],
                                       cwd=app / "Contents/MacOS", stdout=log, stderr=subprocess.STDOUT)
            try:
                for _ in range(5):
                    time.sleep(1)
                    if process.poll() is not None:
                        log.seek(0)
                        raise ValueError("Packaged app exited during launch:\n" + log.read().decode(errors="replace"))
            finally:
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait()
    print(f"[smoke] {rid} packaged app stayed running.", flush=True)


def publish(rids, configuration, output, skip_tests=False, smoke=False):
    # Check every requested target before testing, building or writing artifacts.
    preflight(rids)
    version = read_version()
    if not skip_tests:
        test(configuration)
    else:
        print("[test] Skipped by --skip-tests.", flush=True)
    output.mkdir(parents=True, exist_ok=True)
    artifacts = output / "artifacts"
    artifacts.mkdir(exist_ok=True)
    for rid in rids:
        # Fresh staging prevents removed resources or dependencies leaking into releases.
        # Keep the previous successful publish and artifacts if the new build fails.
        with tempfile.TemporaryDirectory(prefix=f".{rid}-", dir=output) as temporary:
            staging = Path(temporary)
            payload = staging / "publish"
            run("dotnet", "publish", "NMSE.csproj", "--configuration", configuration,
                "--runtime", rid, "--self-contained", "true", "-p:PublishAot=false",
                "-p:PublishReadyToRun=false", "-p:PublishSingleFile=false", "-p:PublishTrimmed=false",
                "-p:UseAppHost=true", "-p:Version=" + version, "--output", payload)
            validate_payload(payload, rid)
            packages = staging / "artifacts"
            packages.mkdir()
            if rid.startswith("osx-"):
                # version.json is the single source of truth, even with an ambient override.
                environment = dict(os.environ, NMSE_VERSION=version)
                run("bash", ROOT / "scripts/macos/build-dmg.sh", payload, rid, packages, env=environment)
                if smoke:
                    smoke_test(packages / f"NMSE-{version}-macOS-{rid.split('-')[1]}.zip", rid)
            else:
                label = "windows-x64.zip" if rid == "win-x64" else "linux-x64.tar.gz"
                write_archive(payload, packages / f"NMSE-{version}-{label}", rid)
            publish_root = output / "publish"
            publish_root.mkdir(exist_ok=True)
            previous = publish_root / rid
            if previous.exists():
                shutil.rmtree(previous)
            shutil.move(str(payload), previous)
            for package in sorted(packages.iterdir()):
                destination = artifacts / package.name
                os.replace(package, destination)
                print(f"[done] {destination}", flush=True)
    print(f"[done] Release {version}; self-contained packages in {artifacts}", flush=True)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    subcommands = parser.add_subparsers(dest="command", required=True)
    for name in ("build", "test", "publish"):
        command = subcommands.add_parser(name)
        command.add_argument("-c", "--configuration", choices=("Debug", "Release"),
                             default="Debug" if name == "build" else "Release")
        if name == "publish":
            command.add_argument("target", nargs="?", default="native",
                                 choices=("native", *TARGETS, *RIDS))
            command.add_argument("--output", type=Path, default=ROOT / "Build",
                                 help="Output root (default: repository Build/; relative paths use your working directory).")
            command.add_argument("--skip-tests", action="store_true", help="Package without running tests.")
            command.add_argument("--smoke-test", action="store_true",
                                 help="Launch macOS ZIPs matching this host's architecture for five seconds.")
    args = parser.parse_args(argv)
    try:
        if args.command == "publish":
            publish(select_rids(args.target), args.configuration, args.output.resolve(),
                    args.skip_tests, args.smoke_test)
        else:
            preflight(())
            if args.command == "test":
                test(args.configuration)
            else:
                run("dotnet", "build", "NMSE.csproj", "--configuration", args.configuration)
    except (ValueError, OSError, KeyError, subprocess.CalledProcessError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
