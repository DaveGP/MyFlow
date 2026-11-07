#!/usr/bin/env bash
# Compiles the two small Swift command-line helpers MyFlow uses on macOS:
#   macos-globe-listener  - reports Globe/Fn key presses (FN_DOWN / FN_UP on stdout)
#   macos-fast-paste      - posts ⌘V (paste) or, with --copy, ⌘C to the focused app
# Both come from OpenWhispr (MIT). Output: native/macos/build/
set -euo pipefail
cd "$(dirname "$0")/.."

OUT=native/macos/build
mkdir -p "$OUT/.module-cache"
ARCH="$(uname -m)"   # arm64 or x86_64
TARGET="${ARCH}-apple-macosx11.0"

for helper in macos-globe-listener macos-fast-paste; do
  src="native/macos/${helper}.swift"
  bin="$OUT/${helper}"
  if [[ -f "$bin" && "$bin" -nt "$src" ]]; then
    echo "  $helper is up to date"
    continue
  fi
  echo "  compiling $helper ($TARGET)"
  xcrun swiftc "$src" -O -target "$TARGET" -module-cache-path "$OUT/.module-cache" -o "$bin"
done
