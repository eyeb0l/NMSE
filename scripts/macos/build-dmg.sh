#!/usr/bin/env bash
# Build native, self-contained NMSE macOS ZIP and DMG artifacts.
#
# Usage: build-dmg.sh <publish-directory> <osx-arm64|osx-x64> [output-directory]
#
# The publish directory must be produced by NMSE.csproj. The runtime
# payload is installed in Contents/MacOS and the large Resources tree is moved
# to the bundle's read-only Contents/Resources directory.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

usage() {
    echo "Usage: $0 <publish-directory> <osx-arm64|osx-x64> [output-directory]" >&2
}

fail() {
    echo "ERROR: $*" >&2
    exit 1
}

[[ $# -ge 2 && $# -le 3 ]] || { usage; exit 64; }
[[ "$(uname -s)" == "Darwin" ]] || fail "native app/DMG packaging requires macOS"

PUBLISH_DIR="$(cd "$1" 2>/dev/null && pwd)" || fail "publish directory does not exist: $1"
RID="$2"
OUTPUT_DIR="${3:-$REPO_ROOT/Build/macos}"

case "$RID" in
    osx-arm64) ARCH="arm64" ;;
    osx-x64) ARCH="x64" ;;
    *) fail "unsupported RID '$RID' (expected osx-arm64 or osx-x64)" ;;
esac

for command in codesign ditto file hdiutil plutil rsync sips; do
    command -v "$command" >/dev/null 2>&1 || fail "required tool not found: $command"
done

read_version_part() {
    local name="$1"
    sed -nE "s/.*\"${name}\"[[:space:]]*:[[:space:]]*([0-9]+).*/\\1/p" "$REPO_ROOT/version.json" | head -n 1
}

VERSION="${NMSE_VERSION:-}"
if [[ -z "$VERSION" ]]; then
    VERSION="$(read_version_part major).$(read_version_part minor).$(read_version_part patch)"
fi
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] \
    || fail "invalid version '$VERSION'"

find_executable() {
    local candidate
    if [[ -n "${NMSE_EXECUTABLE:-}" ]]; then
        candidate="$PUBLISH_DIR/$NMSE_EXECUTABLE"
        [[ -f "$candidate" ]] || fail "NMSE_EXECUTABLE not found: $candidate"
        basename "$candidate"
        return
    fi

    for candidate in NMSE; do
        if [[ -f "$PUBLISH_DIR/$candidate" && -x "$PUBLISH_DIR/$candidate" ]]; then
            echo "$candidate"
            return
        fi
    done

    while IFS= read -r candidate; do
        if file "$candidate" | grep -q 'Mach-O'; then
            basename "$candidate"
            return
        fi
    done < <(find "$PUBLISH_DIR" -maxdepth 1 -type f -perm -111 -name 'NMSE*' | sort)

    fail "no NMSE Mach-O executable found in $PUBLISH_DIR"
}

EXECUTABLE="$(find_executable)"
RESOURCE_SOURCE="${NMSE_RESOURCE_DIR:-$PUBLISH_DIR/Resources}"
if [[ ! -d "$RESOURCE_SOURCE" ]]; then
    RESOURCE_SOURCE="$REPO_ROOT/Resources"
fi
[[ -d "$RESOURCE_SOURCE" ]] || fail "resource directory not found"

BUILD_DIR="$(mktemp -d "${TMPDIR:-/tmp}/nmse-macos.XXXXXX")"
cleanup() {
    if [[ "${NMSE_KEEP_BUILD_DIR:-0}" == "1" ]]; then
        echo "[debug] Preserving $BUILD_DIR" >&2
    else
        rm -rf "$BUILD_DIR"
    fi
}
trap cleanup EXIT

APP_DIR="$BUILD_DIR/NMSE.app"
CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR" "$OUTPUT_DIR"

echo "[bundle] Copying self-contained $RID runtime payload"
rsync -a --exclude '/Resources' "$PUBLISH_DIR/" "$MACOS_DIR/"
chmod +x "$MACOS_DIR/$EXECUTABLE"

echo "[bundle] Copying read-only application resources"
rsync -a "$RESOURCE_SOURCE/" "$RESOURCES_DIR/"

sed \
    -e "s/__EXECUTABLE__/$EXECUTABLE/g" \
    -e "s/__VERSION__/$VERSION/g" \
    "$SCRIPT_DIR/Info.plist.in" > "$CONTENTS_DIR/Info.plist"

build_icon() {
    local source="${NMSE_ICON:-$REPO_ROOT/Resources/app/NMSE.ico}"

    [[ -f "$source" ]] || return 1
    if [[ "$source" == *.icns ]]; then
        cp "$source" "$RESOURCES_DIR/NMSE.icns"
        return 0
    fi

    sips -s format icns "$source" --out "$RESOURCES_DIR/NMSE.icns" >/dev/null 2>&1
}

if build_icon; then
    /usr/libexec/PlistBuddy -c 'Add :CFBundleIconFile string NMSE' "$CONTENTS_DIR/Info.plist"
    echo "[bundle] Created NMSE.icns"
else
    echo "[bundle] Warning: application icon conversion failed; continuing without CFBundleIconFile" >&2
fi

sign_macho_files() {
    local identity="$1"
    local sign_args=(--force --sign "$identity")
    if [[ "$identity" == "-" ]]; then
        sign_args+=(--timestamp=none)
    else
        sign_args+=(--options runtime --timestamp)
    fi

    local sign_list="$BUILD_DIR/sign-files"
    # The app host resolves back to its containing bundle once Info.plist is
    # present, so signing it here would prematurely attempt to seal the whole
    # bundle. Leave that one file for the final deep bundle pass.
    find "$MACOS_DIR" -type f ! -path "$MACOS_DIR/$EXECUTABLE" -print0 > "$sign_list"
    while IFS= read -r -d '' file_path <&3; do
        # Everything under Contents/MacOS is interpreted as nested code by
        # codesign. Managed PE assemblies and the .NET runtime JSON/PDB files
        # receive generic signatures; native Mach-O files receive normal code
        # signatures. The large data tree is under Contents/Resources and is
        # sealed by the outer bundle signature instead of being signed per file.
        # Do not let codesign consume the while-loop's process-substitution
        # input; otherwise only an arbitrary prefix of nested files is signed.
        codesign "${sign_args[@]}" "$file_path" 3<&- </dev/null
    done 3< "$sign_list"

    if [[ -n "${MACOS_ENTITLEMENTS:-}" ]]; then
        [[ -f "$MACOS_ENTITLEMENTS" ]] || fail "entitlements file not found: $MACOS_ENTITLEMENTS"
        sign_args+=(--entitlements "$MACOS_ENTITLEMENTS")
    fi
    # `dotnet publish --self-contained` places managed PE assemblies beside the
    # Mach-O host. macOS treats every file in Contents/MacOS as nested code.
    # The explicit pass above gives each file its own signature; --deep on the
    # final bundle signature is a belt-and-braces fallback for files whose
    # generic signature is not retained by a particular codesign release. Some
    # codesign releases materialise those generic nested signatures lazily and
    # require a second sealing pass, so retry the exact operation a bounded
    # number of times and only surface the final diagnostic.
    local sign_log="$BUILD_DIR/codesign-bundle.log"
    local attempt
    for attempt in 1 2 3; do
        if codesign --deep "${sign_args[@]}" "$APP_DIR" >"$sign_log" 2>&1; then
            return 0
        fi
    done
    cat "$sign_log" >&2
    fail "unable to sign app bundle after $attempt attempts"
}

SIGN_IDENTITY="${MACOS_SIGN_IDENTITY:--}"
if [[ "$SIGN_IDENTITY" == "-" ]]; then
    echo "[sign] Applying ad-hoc signatures"
else
    echo "[sign] Applying Developer ID signature: $SIGN_IDENTITY"
fi
sign_macho_files "$SIGN_IDENTITY"

if [[ -n "${MACOS_NOTARY_PROFILE:-}" ]]; then
    [[ "$SIGN_IDENTITY" != "-" ]] || fail "notarization requires MACOS_SIGN_IDENTITY"
    NOTARY_ZIP="$BUILD_DIR/NMSE-notary.zip"
    ditto -c -k --sequesterRsrc --keepParent "$APP_DIR" "$NOTARY_ZIP"
    echo "[notary] Submitting app bundle"
    xcrun notarytool submit "$NOTARY_ZIP" --keychain-profile "$MACOS_NOTARY_PROFILE" --wait
    xcrun stapler staple "$APP_DIR"
fi

ARTIFACT_BASE="NMSE-$VERSION-macOS-$ARCH"
ZIP_PATH="$OUTPUT_DIR/$ARTIFACT_BASE.zip"
DMG_PATH="$OUTPUT_DIR/$ARTIFACT_BASE.dmg"

echo "[archive] Creating updater ZIP"
rm -f "$ZIP_PATH"
ditto -c -k --sequesterRsrc --keepParent "$APP_DIR" "$ZIP_PATH"

DMG_STAGING="$BUILD_DIR/dmg"
mkdir -p "$DMG_STAGING"
ditto "$APP_DIR" "$DMG_STAGING/NMSE.app"
ln -s /Applications "$DMG_STAGING/Applications"

echo "[archive] Creating drag-install DMG"
rm -f "$DMG_PATH"
hdiutil create -volname "NMSE $VERSION" -srcfolder "$DMG_STAGING" -ov -format UDZO "$DMG_PATH"

if [[ "$SIGN_IDENTITY" != "-" ]]; then
    codesign --force --sign "$SIGN_IDENTITY" --timestamp "$DMG_PATH"
fi
if [[ -n "${MACOS_NOTARY_PROFILE:-}" ]]; then
    echo "[notary] Submitting DMG"
    xcrun notarytool submit "$DMG_PATH" --keychain-profile "$MACOS_NOTARY_PROFILE" --wait
    xcrun stapler staple "$DMG_PATH"
    xcrun stapler validate "$APP_DIR"
    xcrun stapler validate "$DMG_PATH"
    export REQUIRE_GATEKEEPER=1
fi

"$SCRIPT_DIR/validate-bundle.sh" "$APP_DIR" "$RID" "$DMG_PATH" "$ZIP_PATH"

echo "[done] $ZIP_PATH"
echo "[done] $DMG_PATH"
