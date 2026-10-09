#!/bin/sh
# Builds bytebridge_<version>_amd64.deb from a linux-x64 publish folder:
#
#   dotnet publish service/ByteBridge.Service.csproj -c Release \
#     -r linux-x64 --self-contained true -o publish
#   scripts/linux/build-deb.sh 3.1.0 publish out
#
# The package does what install.sh does, the way dpkg expects it: the
# bundle goes to /opt/bytebridge, the unit to the system unit folder,
# and the `bytebridge` command to /usr/bin. The service account is
# created on install and kept on removal, with the data, so reinstalling
# never loses a connection or the key that opens its passwords.
set -eu

VERSION="${1:?usage: build-deb.sh <version> <publish-dir> <out-dir>}"
PUBLISH="${2:?usage: build-deb.sh <version> <publish-dir> <out-dir>}"
OUT="${3:?usage: build-deb.sh <version> <publish-dir> <out-dir>}"
HERE="$(cd "$(dirname "$0")" && pwd)"

[ -f "$PUBLISH/ByteBridge.Service" ] || { echo "$PUBLISH/ByteBridge.Service not found" >&2; exit 1; }
[ -f "$HERE/bytebridge.unit" ] || { echo "bytebridge.unit not found beside this script" >&2; exit 1; }

# 3.2.0-beta.1 sorts after 3.2.0 under dpkg unless the hyphen becomes a tilde.
DEB_VERSION="$(echo "$VERSION" | sed 's/-/~/')"
PKG="$(mktemp -d)/bytebridge_${DEB_VERSION}_amd64"

install -d -m 755 "$PKG/DEBIAN" "$PKG/opt/bytebridge" "$PKG/usr/bin" "$PKG/lib/systemd/system"

cp -r "$PUBLISH"/. "$PKG/opt/bytebridge/"
rm -f "$PKG/opt/bytebridge/install.sh" "$PKG/opt/bytebridge/bytebridge.unit"
chmod 755 "$PKG/opt/bytebridge/ByteBridge.Service"
install -m 644 "$HERE/bytebridge.unit" "$PKG/lib/systemd/system/bytebridge.service"

# The admin commands must run as the service account, or the settings
# file would end up owned by root and unreadable by the service. root
# can drop to it directly; anyone else goes through sudo.
#
# enroll, claim and unenroll are the exception: they install or remove
# the cloudflared systemd unit, which only root may do. They run as root,
# and the data they touched is handed back to the service account after,
# for the same reason.
cat > "$PKG/usr/bin/bytebridge" <<'WRAP'
#!/bin/sh
DATA=/var/lib/bytebridge
BIN=/opt/bytebridge/ByteBridge.Service

case "${1:-}" in
  enroll|claim|unenroll)
    [ "$(id -u)" -eq 0 ] || exec sudo "$0" "$@"
    # An approval can take minutes, and the service keeps running and
    # reading its files meanwhile. So what root creates must be usable by
    # the service from the start (the folders are owner-only, which is
    # what keeps everyone else out), and the hand-back below runs however
    # this ends, Ctrl+C included. The exit status stays the command's own.
    umask 000
    repair() {
      chown -R bytebridge:bytebridge "$DATA"
      [ ! -d "$DATA-keys" ] || chown -R bytebridge:bytebridge "$DATA-keys"
    }
    trap repair EXIT
    trap 'exit 130' INT TERM HUP
    env BYTEBRIDGE_DATA="$DATA" "$BIN" "$@"
    exit $?
    ;;
esac

if [ "$(id -u)" -eq 0 ]; then
  exec runuser -u bytebridge -- env BYTEBRIDGE_DATA="$DATA" "$BIN" "$@"
fi
exec sudo -u bytebridge env BYTEBRIDGE_DATA="$DATA" "$BIN" "$@"
WRAP
chmod 755 "$PKG/usr/bin/bytebridge"

SIZE="$(du -sk "$PKG" | cut -f1)"
cat > "$PKG/DEBIAN/control" <<CONTROL
Package: bytebridge
Version: $DEB_VERSION
Section: net
Priority: optional
Architecture: amd64
Maintainer: Kenan Wahbeh <kenanwahbeh@users.noreply.github.com>
Installed-Size: $SIZE
Depends: systemd, util-linux, sudo, libc6, libgcc-s1, libstdc++6, zlib1g, libicu78 | libicu76 | libicu74 | libicu72 | libicu70 | libicu67
Homepage: https://github.com/kenanwahbeh/ByteBridge
Description: HTTP gateway for Firebird and PostgreSQL databases
 ByteBridge exposes a database through a read-only-by-default HTTP API,
 reached through a Cloudflare Tunnel. This package runs it as a systemd
 service and installs the "bytebridge" command to configure it.
CONTROL

cat > "$PKG/DEBIAN/postinst" <<'POSTINST'
#!/bin/sh
set -e
if [ "$1" = configure ]; then
  id bytebridge >/dev/null 2>&1 ||
    useradd --system --home-dir /var/lib/bytebridge --shell /usr/sbin/nologin bytebridge

  # A machine that used scripts/linux/install.sh has its own copies of
  # the unit and the command in /etc and /usr/local, which take
  # precedence over the package's and nothing would ever remove. This
  # package replaces them.
  if grep -qs '^Description=ByteBridge gateway' /etc/systemd/system/bytebridge.service; then
    rm -f /etc/systemd/system/bytebridge.service
  fi
  if grep -qs '/opt/bytebridge/ByteBridge.Service' /usr/local/bin/bytebridge; then
    rm -f /usr/local/bin/bytebridge
  fi

  if [ -d /run/systemd/system ]; then
    systemctl daemon-reload
    if [ -z "$2" ]; then
      # First install: enable and start it.
      systemctl enable bytebridge
      systemctl restart bytebridge || true
    elif systemctl is-active --quiet bytebridge; then
      # Upgrade: the new binary replaces the running one. A service the
      # administrator disabled or stopped stays that way.
      systemctl restart bytebridge || true
    fi
    if [ -n "$(systemctl is-enabled bytebridge 2>/dev/null | grep -x enabled)" ] &&
       ! systemctl is-active --quiet bytebridge; then
      echo "ByteBridge is installed, but the service is not running." >&2
      echo "See why with: journalctl -u bytebridge -n 50" >&2
      exit 0
    fi
  fi
  echo "ByteBridge installed. Try: bytebridge status"
fi
exit 0
POSTINST

cat > "$PKG/DEBIAN/prerm" <<'PRERM'
#!/bin/sh
set -e
if [ "$1" = remove ]; then
  systemctl stop bytebridge || true
  systemctl disable bytebridge || true
fi
exit 0
PRERM

cat > "$PKG/DEBIAN/postrm" <<'POSTRM'
#!/bin/sh
set -e
# Data (/var/lib/bytebridge) and the key beside it are left in place on
# purpose, even on purge: they hold the connections and the key that
# opens their passwords, and deleting them is the operator's call.
systemctl daemon-reload || true
exit 0
POSTRM
chmod 755 "$PKG/DEBIAN/postinst" "$PKG/DEBIAN/prerm" "$PKG/DEBIAN/postrm"

mkdir -p "$OUT"
dpkg-deb --root-owner-group --build "$PKG" "$OUT/bytebridge_${DEB_VERSION}_amd64.deb"
