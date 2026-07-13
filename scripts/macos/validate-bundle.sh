#!/usr/bin/env bash
# Validate an NMSE .app and optional ZIP/DMG before publishing them.

set -euo pipefail

fail() {
    echo "ERROR: $*" >&2
    exit 1
}

[[ $# -ge 2 && $# -le 4 ]] \
    || fail "usage: $0 <NMSE.app> <osx-arm64|osx-x64> [dmg] [zip]"

APP_DIR="$1"
RID="$2"
DMG_PATH="${3:-}"
ZIP_PATH="${4:-}"
PLIST="$APP_DIR/Contents/Info.plist"

[[ -d "$APP_DIR" ]] || fail "app bundle not found: $APP_DIR"
[[ -f "$PLIST" ]] || fail "Info.plist not found"
plutil -lint "$PLIST"

BUNDLE_ID="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$PLIST")"
MIN_OS="$(/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' "$PLIST")"
EXECUTABLE="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$PLIST")"
[[ "$BUNDLE_ID" == "com.vectorcmdr.nmse" ]] || fail "unexpected bundle id: $BUNDLE_ID"
[[ "$MIN_OS" == "14.0" ]] || fail "unexpected minimum macOS version: $MIN_OS"
[[ -x "$APP_DIR/Contents/MacOS/$EXECUTABLE" ]] || fail "main executable is missing or not executable"
[[ -d "$APP_DIR/Contents/Resources" ]] || fail "Contents/Resources is missing"
[[ -d "$APP_DIR/Contents/Resources/json" ]] || fail "JSON resources are missing"
[[ -d "$APP_DIR/Contents/Resources/images" ]] || fail "image resources are missing"

case "$RID" in
    osx-arm64) EXPECTED_ARCH="arm64" ;;
    osx-x64) EXPECTED_ARCH="x86_64" ;;
    *) fail "unsupported RID: $RID" ;;
esac

file "$APP_DIR/Contents/MacOS/$EXECUTABLE" | grep -q "$EXPECTED_ARCH" \
    || fail "main executable is not $EXPECTED_ARCH"
codesign --verify --deep --strict --verbose=1 "$APP_DIR"

if [[ "${REQUIRE_GATEKEEPER:-0}" == "1" ]]; then
    spctl --assess --type execute --verbose=2 "$APP_DIR"
else
    echo "[validate] Gatekeeper assessment skipped (notarization was not requested)"
fi

if [[ -n "$DMG_PATH" ]]; then
    [[ -f "$DMG_PATH" ]] || fail "DMG not found: $DMG_PATH"
    hdiutil verify "$DMG_PATH"
fi

if [[ -n "$ZIP_PATH" ]]; then
    [[ -f "$ZIP_PATH" ]] || fail "ZIP not found: $ZIP_PATH"
    ZIP_LIST="$(unzip -Z1 "$ZIP_PATH")"
    grep -q '^NMSE\.app/Contents/Info\.plist$' <<<"$ZIP_LIST" \
        || fail "ZIP does not contain NMSE.app/Contents/Info.plist"
    if grep -Eq '(^|/)\.\./|^/|^[A-Za-z]:[/\\]' <<<"$ZIP_LIST"; then
        fail "ZIP contains an unsafe path"
    fi
fi

echo "[validate] NMSE.app, $RID, and supplied archives are valid"
