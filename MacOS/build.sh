#!/bin/bash
# Builds "Kaleido Wallpaper.app" with the Xcode Command Line Tools.
# Install those once with:  xcode-select --install
set -euo pipefail
cd "$(dirname "$0")"

APP="build/Kaleido Wallpaper.app"
rm -rf build
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

swiftc -O -parse-as-library \
  -target "$(uname -m)-apple-macos13.0" \
  -framework AppKit -framework WebKit -framework ScreenCaptureKit \
  -framework CoreMedia -framework Accelerate -framework ServiceManagement \
  Sources/*.swift -o "$APP/Contents/MacOS/KaleidoWallpaper"

cp Info.plist "$APP/Contents/Info.plist"
cp Resources/kaleido.html "$APP/Contents/Resources/"

# app icon: all macOS sizes from the 1024 px PNG
ICONSET="build/AppIcon.iconset"
mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
  sips -z $size $size Resources/AppIcon.png --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  sips -z $((size * 2)) $((size * 2)) Resources/AppIcon.png --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
rm -rf "$ICONSET"

# ad-hoc signature: needed so macOS can remember the Screen Recording permission
codesign --force --sign - "$APP"

echo
echo "Klaar: $APP"
echo "Zet hem in je map Apps met:  cp -R \"$APP\" /Applications/"
