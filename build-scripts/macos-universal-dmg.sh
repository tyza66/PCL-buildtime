#!/bin/bash
# Assemble the universal (x86_64 + arm64) PCL2-R.app and wrap it in a
# drag-to-install DMG with a /Applications alias.
#
# Usage: macos-universal-dmg.sh VERSION X64_PUBLISH ARM64_PUBLISH
#
# Layout rationale: CoreCLR loads System.Private.CoreLib.dll from the app
# root by hardcoded convention, and that file is a PE R2R image that cannot
# be lipo-merged across architectures. One shared payload therefore cannot
# serve both Macs. Instead the bundle ships two complete self-contained
# publishes and dispatches by architecture:
#   Contents/MacOS/PCL2.Avalonia    fat Mach-O launcher, picks by uname -m
#   Contents/MacOS/osx-x64/         full x86_64 self-contained publish
#   Contents/MacOS/osx-arm64/       full arm64 self-contained publish
set -euo pipefail

if [ "$#" -ne 3 ]; then
  echo "usage: macos-universal-dmg.sh VERSION X64_PUBLISH ARM64_PUBLISH" >&2
  exit 2
fi
version="$1"
x64_dir="$2"
arm64_dir="$3"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
name="PCL2-R-${version}-osx-universal"
dmg_name="${name}.dmg"
app="PCL2-R.app"
app_exe="PCL2.Avalonia" # CFBundleExecutable in PCL.Avalonia/Assets/Info.plist

work="$(mktemp -d)"
detach_dmg() { :; }
trap 'rm -rf "$work"' EXIT
approot="$work/$app"
mkdir -p "$approot/Contents/MacOS" "$approot/Contents/Resources"
cp "$repo_root/PCL.Avalonia/Assets/Info.plist" "$approot/Contents/Info.plist"

# Two complete per-architecture payloads
cp -R "$x64_dir" "$approot/Contents/MacOS/osx-x64"
cp -R "$arm64_dir" "$approot/Contents/MacOS/osx-arm64"

# Fat Mach-O launcher: resolves its own directory, unames the arch, and
# execs the matching payload's apphost so appbase carries the right runtime.
launcher_src="$work/launcher.c"
cat > "$launcher_src" << 'LAUNCHER_EOF'
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <sys/utsname.h>

int main(int argc, const char *argv[]) {
    char exe[4096];
    uint32_t size = sizeof(exe);
    if (_NSGetExecutablePath(exe, &size) != 0) {
        fprintf(stderr, "PCL2-R launcher: executable path too long\n");
        return 70;
    }
    char real[4096];
    if (realpath(exe, real) == NULL) {
        fprintf(stderr, "PCL2-R launcher: cannot resolve %s\n", exe);
        return 70;
    }
    char *slash = strrchr(real, '/');
    if (slash == NULL) {
        fprintf(stderr, "PCL2-R launcher: unexpected path %s\n", real);
        return 70;
    }
    *slash = '\0';

    struct utsname u;
    if (uname(&u) != 0) {
        fprintf(stderr, "PCL2-R launcher: uname failed\n");
        return 70;
    }
    const char *arch = "x64";
    if (strcmp(u.machine, "arm64") == 0 || strcmp(u.machine, "arm64e") == 0) {
        arch = "arm64";
    }

    char child[4096];
    int n = snprintf(child, sizeof(child), "%s/osx-%s/PCL2.Avalonia", real, arch);
    if (n <= 0 || (size_t)n >= sizeof(child)) {
        fprintf(stderr, "PCL2-R launcher: path too long\n");
        return 70;
    }
    argv[0] = child;
    execv(child, (char *const *)argv);
    fprintf(stderr, "PCL2-R launcher: cannot exec %s\n", child);
    return 127;
}
LAUNCHER_EOF
clang_bin="clang"
if ! command -v "$clang_bin" > /dev/null 2>&1; then
  clang_bin="xcrun clang"
fi
# shellcheck disable=SC2086
$clang_bin -arch x86_64 -arch arm64 -Os -o "$approot/Contents/MacOS/$app_exe" "$launcher_src"
chmod +x "$approot/Contents/MacOS/$app_exe"

# Generate .icns from the PCL icon
iconset="$work/$name.iconset"
src_base="$repo_root/PCL.Avalonia/Assets/pcl2-avalonia.png"
src_hi="$repo_root/PCL.Avalonia/Assets/pcl2-avalonia@2x.png"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  for scale in 1 2; do
    px=$((size * scale))
    if [ "$px" -le 256 ]; then src="$src_base"; else src="$src_hi"; fi
    sips -z "$px" "$px" --out "$iconset/icon_${size}x${size}@${scale}x.png" "$src"
  done
done
iconutil -c icns "$iconset" -o "$approot/Contents/Resources/PCL2-R.icns"

# Ad-hoc sign the whole bundle (launcher plus both payloads)
codesign --sign - --force --deep --timestamp=none "$approot"

# Stage the DMG volume with the /Applications drag-to-install alias
stage="$work/dmg-stage"
mkdir -p "$stage"
cp -R "$approot" "$stage/"
ln -s /Applications "$stage/Applications"
hdiutil create -volname "PCL2-R" -srcfolder "$stage" -ov -format UDZO "$dmg_name"

# Structural gates on the assembled bundle and the DMG contents
launcher_info="$(lipo -info "$approot/Contents/MacOS/$app_exe")"
echo "launcher slices: $launcher_info"
case "$launcher_info" in
  *x86_64*arm64* | *arm64*x86_64*) ;;
  *)
    echo "::error::launcher is missing an architecture: $launcher_info" >&2
    exit 1
    ;;
esac
for dir_slice in "osx-x64:x86_64" "osx-arm64:arm64"; do
  payload_dir="${dir_slice%%:*}"
  expected_slice="${dir_slice##*:}"
  child_info="$(lipo -info "$approot/Contents/MacOS/$payload_dir/$app_exe")"
  echo "$payload_dir apphost slices: $child_info"
  case "$child_info" in
    *"$expected_slice"*) ;;
    *)
      echo "::error::$payload_dir payload has the wrong apphost: $child_info" >&2
      exit 1
      ;;
  esac
done
codesign --verify "$approot"

python3 "$repo_root/build-scripts/checksums.py" "$dmg_name"

mount_dir="$work/dmg-mount"
mkdir -p "$mount_dir"
hdiutil attach "$dmg_name" -mountpoint "$mount_dir" -nobrowse > /dev/null
detach_dmg() {
  hdiutil detach "$mount_dir" > /dev/null 2>&1 || true
}
trap 'detach_dmg; rm -rf "$work"' EXIT
ls -l "$mount_dir"
if [ ! -L "$mount_dir/Applications" ]; then
  echo "::error::DMG is missing the /Applications alias" >&2
  exit 1
fi
if [ ! -f "$mount_dir/$app/Contents/Resources/PCL2-R.icns" ]; then
  echo "::error::App bundle is missing PCL2-R.icns" >&2
  exit 1
fi
if [ ! -f "$mount_dir/$app/Contents/MacOS/osx-x64/$app_exe" ] || [ ! -f "$mount_dir/$app/Contents/MacOS/osx-arm64/$app_exe" ]; then
  echo "::error::App bundle is missing a per-architecture payload" >&2
  exit 1
fi
hdiutil detach "$mount_dir" > /dev/null
detach_dmg() { :; }
trap 'rm -rf "$work"' EXIT

ls -l "$dmg_name" "$dmg_name".*
