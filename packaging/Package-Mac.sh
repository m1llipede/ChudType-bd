#!/bin/zsh
set -euo pipefail

repo="${0:A:h:h}"
version="${VERSION:-0.2.0}"
release="$repo/artifacts/mac/release"
app="$repo/artifacts/mac/ChudType.app"

DOTNET_BIN="${DOTNET_BIN:-dotnet}" "$repo/packaging/Build-Mac.sh"
rm -rf "$release"
mkdir -p "$release"

/usr/bin/ditto -c -k --sequesterRsrc --keepParent "$app" "$release/ChudType-$version-macOS-arm64.zip"
hdiutil create -quiet -volname "ChudType" -srcfolder "$app" -ov -format UDZO "$release/ChudType-$version-macOS-arm64.dmg"

shasum -a 256 "$release/ChudType-$version-macOS-arm64.zip" "$release/ChudType-$version-macOS-arm64.dmg" \
  > "$release/SHA256SUMS.txt"
echo "$release"
