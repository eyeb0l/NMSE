# Native Linux packaging

NMSE uses the same native Avalonia desktop frontend on Linux as on Windows and macOS.
Build a tested, self-contained x64 tarball from any supported build host:

```bash
./scripts/build.sh publish linux
```

On Windows use `.\scripts\build.ps1 publish linux`. The tarball is written to
`Build/artifacts/NMSE-<version>-linux-x64.tar.gz`; the unpackaged payload is retained in
`Build/publish/linux-x64/`. See [Building and publishing](../../docs/dev/building.md)
for requirements, options and all-platform builds.

Extract the complete tarball and run `./NMSE`. It contains the .NET runtime and retains
executable permissions. Wine and a separate .NET installation are not required; the
host still needs the normal Linux desktop/native dependencies.

`nmse.desktop` is the FreeDesktop launcher template for a system installation. Adjust
`Exec` and `Icon` for the chosen install location before installing the launcher.
