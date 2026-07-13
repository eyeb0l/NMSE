# Native Linux packaging

NMSE now publishes the same Avalonia desktop frontend on Linux, without Wine.
The release workflow produces a managed self-contained `linux-x64` tarball, so
the target machine does not need a separately installed .NET runtime.

Local publish:

```bash
dotnet publish NMSE.csproj -c Release -r linux-x64 --self-contained true \
  -p:PublishAot=false -o Build/publish/linux-x64
```

`nmse.desktop` is the FreeDesktop launcher template for a system installation.
