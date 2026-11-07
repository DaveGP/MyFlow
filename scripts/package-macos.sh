#!/usr/bin/env bash
# Builds dist/MyFlow.app — a self-contained macOS menu-bar app.
#
#   scripts/package-macos.sh            build dist/MyFlow.app
#   scripts/package-macos.sh --install  …and copy it to /Applications
#
# Code signing: macOS remembers Microphone/Accessibility permission per signing identity.
# With a stable identity (an "Apple Development" certificate from Xcode) permissions survive
# rebuilds. Override with MYFLOW_SIGN_IDENTITY="..." or MYFLOW_SIGN_IDENTITY=- for ad-hoc
# (ad-hoc means you must re-grant Accessibility after every rebuild).
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$PWD"

ARCH="$(uname -m)"
RID="osx-$([[ "$ARCH" == "arm64" ]] && echo arm64 || echo x64)"
APP="$ROOT/dist/MyFlow.app"
PUBLISH="$ROOT/build/publish-$RID"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"

echo "==> Native helpers"
scripts/build-native-macos.sh

echo "==> dotnet publish ($RID)"
rm -rf "$PUBLISH"
dotnet publish src/MyFlow.App/MyFlow.App.csproj -c Release -r "$RID" --self-contained true \
  -p:UseAppHost=true -o "$PUBLISH" -nologo -v quiet

echo "==> Assembling $APP"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/bin"
cp -R "$PUBLISH"/. "$APP/Contents/MacOS/"
cp native/macos/build/macos-globe-listener native/macos/build/macos-fast-paste "$APP/Contents/Resources/bin/"
cp LICENSE THIRD_PARTY_NOTICES.md "$APP/Contents/Resources/"

# App icon: assets/MyFlow.png -> MyFlow.icns
ICONSET="$ROOT/build/MyFlow.iconset"
rm -rf "$ICONSET" && mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  sips -z $s $s assets/MyFlow.png --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  sips -z $((s*2)) $((s*2)) assets/MyFlow.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/MyFlow.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>MyFlow</string>
  <key>CFBundleDisplayName</key><string>MyFlow</string>
  <key>CFBundleIdentifier</key><string>com.davegp.myflow</string>
  <key>CFBundleExecutable</key><string>MyFlow</string>
  <key>CFBundleIconFile</key><string>MyFlow</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSMicrophoneUsageDescription</key><string>MyFlow listens while you hold the dictation shortcut and turns your speech into text on this Mac.</string>
  <key>NSHumanReadableCopyright</key><string>MyFlow — MIT License. Based on OpenWhispr (MIT).</string>
</dict>
</plist>
PLIST

echo "==> Signing"
IDENTITY="${MYFLOW_SIGN_IDENTITY:-}"
if [[ -z "$IDENTITY" ]]; then
  IDENTITY="$(security find-identity -v -p codesigning | sed -n 's/.*"\(Apple Development:[^"]*\)".*/\1/p' | sort | head -1)"
fi
IDENTITY="${IDENTITY:--}"
echo "    identity: $IDENTITY"
# Sign everything in Contents/MacOS (codesign treats all of it as code, .NET .dll files
# included) and the helpers, then the bundle itself.
find "$APP/Contents/MacOS" "$APP/Contents/Resources/bin" -type f ! -path "$APP/Contents/MacOS/MyFlow" -print0 | while IFS= read -r -d '' f; do
  codesign --force --timestamp=none -s "$IDENTITY" "$f" 2>/dev/null
done
codesign --force --timestamp=none -s "$IDENTITY" "$APP"
codesign --verify --deep --strict "$APP" && echo "    signature OK"

if [[ "${1:-}" == "--install" ]]; then
  echo "==> Installing to /Applications"
  pkill -x MyFlow 2>/dev/null || true
  rm -rf /Applications/MyFlow.app
  cp -R "$APP" /Applications/
  echo "    /Applications/MyFlow.app"
fi

echo "Done: $APP"
