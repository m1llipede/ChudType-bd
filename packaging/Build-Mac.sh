#!/bin/zsh
set -euo pipefail

repo="${0:A:h:h}"
dotnet_bin="${DOTNET_BIN:-dotnet}"
configuration="${CONFIGURATION:-Release}"
runtime="${RUNTIME:-osx-arm64}"
publish="$repo/artifacts/mac/publish"
app="$repo/artifacts/mac/ChudType.app"

"$dotnet_bin" publish "$repo/src/ChudType.Mac/ChudType.Mac.csproj" \
  --configuration "$configuration" --runtime "$runtime" --self-contained true \
  -p:PublishSingleFile=false -o "$publish"

rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish/"* "$app/Contents/MacOS/"
cp "$repo/packaging/Info.Mac.plist" "$app/Contents/Info.plist"
helper="$app/Contents/Helpers/ChudType.AudioCapture.app"
mkdir -p "$helper/Contents/MacOS"
cp "$repo/packaging/Info.Mac.AudioCapture.plist" "$helper/Contents/Info.plist"
xcrun swiftc -O -framework AVFoundation -framework CoreMedia \
  "$repo/packaging/MacAudioCapture.swift" -o "$helper/Contents/MacOS/ChudType.AudioCapture"
chmod +x "$app/Contents/MacOS/ChudType.Mac"
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"
echo "$app"
