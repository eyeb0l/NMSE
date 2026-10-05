# Building and publishing NMSE

Use one entry point for local development, tests and release packages. CI calls the
same Python script on Windows, Linux, Apple Silicon macOS and Intel macOS.

## Requirements

- .NET 10 SDK.
- Python 3.10 or later; the script uses only the standard library.
- macOS packaging requires a Mac with its standard command-line tools.
- A network connection for the initial NuGet restore.

Users of the published app do not need Python or .NET installed. All desktop packages
contain the native Avalonia frontend and a self-contained .NET runtime. Native AOT,
trimming and single-file publishing are disabled.

## Development and tests

From the repository root:

```bash
./scripts/build.sh build
dotnet run --project NMSE.csproj
./scripts/build.sh test
```

On Windows, use the PowerShell entry point:

```powershell
.\scripts\build.ps1 build
.\scripts\build.ps1 test
```

Both wrappers forward arguments to `scripts/build.py`. You can also call it directly:

```bash
python3 scripts/build.py build
python3 scripts/build.py test
python3 scripts/build.py publish --help
```

Use `python` or `py -3` on Windows. The scripts find the repository from their own
location, so they work from any current directory. Build defaults to Debug; tests and
publish default to Release. Override with `-c Debug` or `-c Release`.

Tests cover the core, Avalonia headless UI and macOS updater. Windows additionally runs
the extractor tests. Avalonia's xUnit executable is invoked with `dotnet run`, so its
headless tests are included. Use these commands instead of a bare solution build/test:
the solution also contains the Windows-only extractor.

## Publish packages

Each command restores, tests, publishes and packages the selected targets:

```bash
./scripts/build.sh publish windows   # Windows x64 ZIP
./scripts/build.sh publish linux     # Linux x64 tar.gz
./scripts/build.sh publish macos     # Apple Silicon AND Intel ZIPs and DMGs
./scripts/build.sh publish all       # All four runtimes; run this on a Mac
./scripts/build.sh publish           # This host's supported runtime
```

The same commands work in PowerShell, for example:

```powershell
.\scripts\build.ps1 publish windows
.\scripts\build.ps1 publish linux
```

Windows and Linux packages can be built on any supported host. Complete macOS
packaging requires macOS for signing and disk-image creation. Selecting `all` or a
macOS target on another OS fails before any build starts; use CI for a complete set
from Windows or Linux. Automatic host selection supports x64 Windows/Linux and arm64
or x64 macOS. Linux ARM64 and Windows ARM64 release packages are not currently provided.

To select a single runtime, use `win-x64`, `linux-x64`, `osx-arm64` or `osx-x64`:

```bash
./scripts/build.sh publish osx-arm64 --smoke-test
./scripts/build.sh publish all --output /path/to/release-build
./scripts/build.sh publish windows --skip-tests
```

Tests run once per invocation, even when publishing several runtimes. `--skip-tests`
is for packaging after separate validation; CI always runs tests. `--smoke-test`
launches the packaged macOS app for five seconds and checks that it stays running.
Foreign macOS architectures are reported as skipped; CI tests each architecture on a
matching runner. This checks startup, not every UI action.

## Outputs

Default outputs are under the repository's ignored `Build/` directory:

| Path | Contents |
|------|----------|
| `Build/bin/<project>/` and `Build/obj/<project>/` (relative to each project directory) | Development binaries and SDK intermediates |
| `Build/publish/<rid>/` | Latest successful self-contained publish for that runtime |
| `Build/artifacts/NMSE-<version>-windows-x64.zip` | Windows executable, runtime and resources |
| `Build/artifacts/NMSE-<version>-linux-x64.tar.gz` | Linux executable, runtime and resources |
| `Build/artifacts/NMSE-<version>-macOS-arm64.zip` and `.dmg` | Apple Silicon `NMSE.app` |
| `Build/artifacts/NMSE-<version>-macOS-x64.zip` and `.dmg` | Intel `NMSE.app` |

`--output` changes the publish/artifact root, leaving SDK intermediates in their normal
locations. A relative output path resolves from your current working directory.
`version.json` supplies package names, bundle versions and the published assembly
version. Archive names remain compatible with the updater.

Every publish starts in a fresh staging directory. Tests, publish and packaging must
succeed before replacing that runtime's previous outputs. Other runtimes and older
versioned archives are retained. If a multi-target command fails, already completed
targets remain available; the command exits unsuccessfully and can be rerun.

Windows ZIPs and Linux tarballs contain the executable at the archive root. Extract
the whole archive and launch `NMSE.exe` or `./NMSE`; retain the runtime and Resources
beside the executable. Linux executable permissions are preserved, including when
cross-publishing on Windows. Linux still needs its normal desktop/native libraries;
the bundled .NET runtime does not replace OS dependencies.

## macOS signing

The publish command calls the existing
[macOS packager](../../scripts/macos/README.md), which validates bundle metadata,
architecture, resources, signatures and archive integrity. It creates ad-hoc signed
packages by default. These packages are not notarized.

For Developer ID distribution, configure the signing certificate in your keychain:

```bash
export MACOS_SIGN_IDENTITY='Developer ID Application: Example (TEAMID)'
export MACOS_NOTARY_PROFILE='nmse-notary'
./scripts/build.sh publish macos
```

The notary profile must already exist in the keychain. The packager signs, notarizes and
staples the app and DMG, and requires Gatekeeper validation when notarization is enabled.
See the macOS README for credential setup and optional entitlements.

## GitHub Actions

- `build-platforms.yml` owns the four-runner build/test/package matrix. Both callers
  upload the same versioned packages, and both macOS runners perform launch checks.
- `validate-pr.yml` calls it without signing secrets. Script, app, resource and test
  changes trigger validation, including Windows-only extractor changes.
- `build-nmse.yml` calls it with release signing enabled when secrets are configured.
  A `version.json` push to `main`, or manual dispatch, builds all targets. In the
  canonical `vectorcmdr/NMSE` repository it then creates the versioned GitHub release,
  or uploads only missing assets to an existing release. Existing assets, title and
  notes are preserved. Forks build/upload workflow artifacts without creating releases.

The release uses `release-notes.md`, adds commit/run metadata and tags the built commit.
Only its release job receives `contents: write`; build and PR jobs receive read access.
Release runs for the same ref are serialized.

Optional repository secrets for macOS release signing are
`MACOS_CERTIFICATE_P12` (base64 P12), `MACOS_CERTIFICATE_PASSWORD` and
`MACOS_SIGN_IDENTITY`. For notarization also provide `APPLE_ID`, `APPLE_TEAM_ID`
and `APPLE_APP_PASSWORD`. Partial signing or notarization configuration fails clearly;
temporary CI signing credentials are removed at the end of the job.

Local `publish` creates files only; it does not upload assets, create a release, tag or
push commits.

To test the build tooling itself without .NET:

```bash
python3 -m unittest discover -s scripts/tests -v
```

## References

- [.NET deployment modes](https://learn.microsoft.com/en-us/dotnet/core/deploying/)
- [Reusable GitHub Actions workflows](https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows)
- [GitHub-hosted runner architectures](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
