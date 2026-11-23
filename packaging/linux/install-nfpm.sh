#!/usr/bin/env bash
# Downloads nfpm (MIT) for this machine into the folder given, checking the SHA-256 against the value pinned here.
#   packaging/linux/install-nfpm.sh /some/folder     then use /some/folder/nfpm
set -euo pipefail
version=2.41.1
dest=${1:?folder to put nfpm in}
case "$(uname -m)" in
    x86_64) file=nfpm_${version}_Linux_x86_64.tar.gz; sha=b3cf95aa6dabed836d09ad7f0c190a13c74c5b1304db60846f0f702ee407f430 ;;
    aarch64|arm64) file=nfpm_${version}_Linux_arm64.tar.gz; sha=17350a838c8e2c422c6e573ed379b18424565d2de8a2b1cb1b20211976124eb5 ;;
    *) echo "no nfpm build for $(uname -m)" >&2; exit 2 ;;
esac
mkdir -p "$dest"
curl -fsSL -o "$dest/nfpm.tar.gz" "https://github.com/goreleaser/nfpm/releases/download/v${version}/${file}"
echo "$sha  $dest/nfpm.tar.gz" | sha256sum -c -
tar -xzf "$dest/nfpm.tar.gz" -C "$dest" nfpm
rm "$dest/nfpm.tar.gz"
"$dest/nfpm" --version
