# Native macOS packaging

These scripts package the self-contained Avalonia build. They do not install or invoke
Wine, Whisky, CrossOver, or a separately installed .NET runtime.

## Build locally

```bash
./scripts/build.sh publish macos                  # Both architectures
./scripts/build.sh publish osx-arm64 --smoke-test # Apple Silicon only
./scripts/build.sh publish osx-x64                # Intel only
```

Run these commands from the repository root on a Mac. They restore, test, publish,
package and validate, writing versioned ZIPs and DMGs to `Build/artifacts/`. The shared
[build guide](../../docs/dev/building.md) covers prerequisites and output options.

For an existing self-contained publish, the low-level packager remains available:

```bash
scripts/macos/build-dmg.sh /path/to/publish osx-arm64 /path/to/artifacts
```

Its legacy default output directory is `Build/macos/`; the shared publish command
always passes its artifact directory explicitly. Both paths validate the app's bundle
identifier, minimum OS, architecture, signature, resources, ZIP paths and DMG integrity.

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
