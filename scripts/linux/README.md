# Native Linux packaging

NMSE uses the same native Avalonia desktop frontend on Linux as on Windows and macOS.
Build tested, self-contained x64 and ARM64 tarballs from any supported build host:

```bash
./scripts/build.sh publish linux
```

On Windows use `.\scripts\build.ps1 publish linux`. Tarballs are written to
`Build/artifacts/NMSE-<version>-linux-x64.tar.gz` and
`Build/artifacts/NMSE-<version>-linux-arm64.tar.gz`; the unpackaged payloads are retained
in `Build/publish/linux-x64/` and `Build/publish/linux-arm64/`.
See [Building and publishing](../../docs/dev/building.md)
for requirements, options and all-platform builds.

Extract the complete tarball and run `./NMSE`. It contains the .NET runtime and retains
executable permissions. Wine and a separate .NET installation are not required; the
host still needs the normal Linux desktop/native dependencies.

Run `uname -m` on the machine where NMSE will run. Choose `linux-arm64` for
`aarch64`/`arm64` (including ARM Ubuntu VMs on Apple Silicon), or `linux-x64` for
`x86_64`. An x64 package on ARM64 Linux produces `Exec format error`. Extract the
matching archive into a fresh directory so files from different architectures do not mix.

To build only one architecture, use `./scripts/build.sh publish linux-arm64` or
`./scripts/build.sh publish linux-x64`. Running `./scripts/build.sh publish` on Linux
selects the host architecture automatically. `publish all` includes both Linux targets.

`nmse.desktop` is the FreeDesktop launcher template for a system installation. Adjust
`Exec` and `Icon` for the chosen install location before installing the launcher.
