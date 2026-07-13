# Native macOS packaging

These scripts package the self-contained Avalonia build. They do not install or invoke
Wine, Whisky, CrossOver, or a separately installed .NET runtime.

## Build locally

```bash
dotnet publish NMSE.csproj -c Release -r osx-arm64 \
  --self-contained true -p:PublishAot=false -o Build/publish/osx-arm64
scripts/macos/build-dmg.sh Build/publish/osx-arm64 osx-arm64 Build/macos
```

Use `osx-x64` on an Intel runner. The script produces an architecture-labelled updater
ZIP and drag-install DMG, and validates the app's bundle identifier, minimum OS,
architecture, signature, resources, ZIP paths, and DMG integrity.

The runtime payload is copied to `NMSE.app/Contents/MacOS`; the large application data
tree is copied to `NMSE.app/Contents/Resources`. `NMSE_RESOURCE_DIR` may override the
resource source. `NMSE_EXECUTABLE` may override executable auto-detection.

## Signing and notarization

With no signing environment, nested runtime files and the app are ad-hoc signed for CI and
local testing. For direct distribution, import a Developer ID Application certificate
and provide:

```bash
export MACOS_SIGN_IDENTITY='Developer ID Application: Example (TEAMID)'
export MACOS_NOTARY_PROFILE='nmse-notary'
xcrun notarytool store-credentials "$MACOS_NOTARY_PROFILE" \
  --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD"
```

The builder signs nested runtime files before the app, enables hardened runtime for
Developer ID builds, notarizes and staples the app, then signs, notarizes, and staples
the DMG. Gatekeeper assessment becomes a required validation gate when notarization is
requested. NMSE is intentionally not sandboxed. Set `MACOS_ENTITLEMENTS` only when a
reviewed entitlement file is required; the packaging scripts do not invent entitlements.

Useful independent checks:

```bash
scripts/macos/validate-bundle.sh /path/to/NMSE.app osx-arm64 /path/to/NMSE.dmg /path/to/NMSE.zip
codesign -dvvv --entitlements :- /path/to/NMSE.app
spctl --assess --type execute --verbose=2 /path/to/NMSE.app
xcrun stapler validate /path/to/NMSE.app
```
