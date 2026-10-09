#!/bin/sh
# Builds a signed apt repository from a folder of .deb files:
#
#   scripts/linux/build-apt-repo.sh <debs-dir> <out-dir> <key-fingerprint>
#
# The layout is the standard one apt expects, with a single suite and
# component, so a machine adds it with
#
#   deb [signed-by=/etc/apt/keyrings/bytebridge.asc] <url> stable main
#
# Release is signed twice: InRelease (inline, what current apt reads)
# and Release.gpg (detached, for older apt). The public half of the key
# is written beside them as bytebridge.asc.
set -eu

DEBS="${1:?usage: build-apt-repo.sh <debs-dir> <out-dir> <key-fingerprint>}"
OUT="${2:?usage: build-apt-repo.sh <debs-dir> <out-dir> <key-fingerprint>}"
KEY="${3:?usage: build-apt-repo.sh <debs-dir> <out-dir> <key-fingerprint>}"

ls "$DEBS"/*.deb >/dev/null 2>&1 || { echo "no .deb files in $DEBS" >&2; exit 1; }

POOL="pool/main/b/bytebridge"
DIST="dists/stable"
mkdir -p "$OUT/$POOL" "$OUT/$DIST/main/binary-amd64"
cp "$DEBS"/*.deb "$OUT/$POOL/"

cd "$OUT"

# Paths in Packages are relative to the repository root, so this runs
# from there. --multiversion keeps every version, so a machine can pin
# or roll back to an older one.
dpkg-scanpackages --arch amd64 --multiversion pool > "$DIST/main/binary-amd64/Packages"
gzip -9 -k -n "$DIST/main/binary-amd64/Packages"

cd "$DIST"
apt-ftparchive \
  -o APT::FTPArchive::Release::Origin=ByteBridge \
  -o APT::FTPArchive::Release::Label=ByteBridge \
  -o APT::FTPArchive::Release::Suite=stable \
  -o APT::FTPArchive::Release::Codename=stable \
  -o APT::FTPArchive::Release::Architectures=amd64 \
  -o APT::FTPArchive::Release::Components=main \
  -o "APT::FTPArchive::Release::Description=ByteBridge gateway" \
  release . > ../Release.tmp
mv ../Release.tmp Release

gpg --batch --yes --local-user "$KEY" --clearsign --output InRelease Release
gpg --batch --yes --local-user "$KEY" --armor --detach-sign --output Release.gpg Release

cd ../..
gpg --armor --export "$KEY" > bytebridge.asc

# Pages would otherwise run Jekyll over the tree.
touch .nojekyll
