#!/usr/bin/env bash
# Makes the .deb and .rpm of the OpenVP server from a self-contained publish folder.
#   packaging/linux/build.sh VERSION RID PUBLISH_DIR OUT_DIR
#   packaging/linux/build.sh 0.2.0 linux-x64 /tmp/publish-linux-x64 /tmp/out
# RID is linux-x64 or linux-arm64. Needs nfpm (https://nfpm.goreleaser.com); NFPM=/path/to/nfpm if it isn't on PATH.
# Run it from the repository root.
set -euo pipefail

version=${1:?version, for example 0.2.0}
rid=${2:?runtime id: linux-x64 or linux-arm64}
publish=${3:?folder made by dotnet publish}
out=${4:?folder for the packages}

case "$rid" in
    linux-x64) arch=amd64 ;;
    linux-arm64) arch=arm64 ;;
    *) echo "unsupported runtime id: $rid" >&2; exit 2 ;;
esac
[ -x "$publish/openvp" ] || { echo "$publish/openvp not found or not executable: publish with -r $rid --self-contained first" >&2; exit 1; }
nfpm=${NFPM:-nfpm}
command -v "$nfpm" >/dev/null || { echo "nfpm not found (set NFPM=/path/to/nfpm)" >&2; exit 1; }

mkdir -p "$out"
export VERSION="${version#v}" ARCH="$arch" PUBLISH_DIR="$(cd "$publish" && pwd)"
# Fill in the three variables ourselves, so the result does not depend on where nfpm expands them.
config=$(mktemp "${TMPDIR:-/tmp}/openvp-nfpm.XXXXXX.yaml")
trap 'rm -f "$config"' EXIT
sed -e "s|\${VERSION}|$VERSION|g" -e "s|\${ARCH}|$ARCH|g" -e "s|\${PUBLISH_DIR}|$PUBLISH_DIR|g" packaging/linux/nfpm.yaml > "$config"
for fmt in deb rpm; do
    "$nfpm" package --config "$config" --packager "$fmt" --target "$out/"
done
ls -l "$out"
