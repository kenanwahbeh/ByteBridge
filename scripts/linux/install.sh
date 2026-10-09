#!/bin/sh
# Installs ByteBridge as a systemd service from an unpacked Linux bundle:
# the output of
#   dotnet publish service/ByteBridge.Service.csproj -r linux-x64 --self-contained
# which carries ByteBridge.Service, bytebridge.unit and this script together.
# The unit is named .unit here, not .service: on a case-insensitive
# filesystem bytebridge.service would overwrite the ByteBridge.Service executable.
#   sudo ./install.sh [path-to-publish-folder]
set -eu

SRC="${1:-$(dirname "$0")}"
[ "$(id -u)" -eq 0 ] || { echo "run as root (sudo)" >&2; exit 1; }
# Both are checked before anything is stopped or copied.
for f in ByteBridge.Service bytebridge.unit; do
  [ -f "$SRC/$f" ] || { echo "$SRC/$f not found; run this from the published Linux bundle" >&2; exit 1; }
done

id bytebridge >/dev/null 2>&1 ||
  useradd --system --home-dir /var/lib/bytebridge --shell /usr/sbin/nologin bytebridge

systemctl stop bytebridge 2>/dev/null || true

install -d -m 755 /opt/bytebridge
cp -r "$SRC"/. /opt/bytebridge/
rm -f /opt/bytebridge/install.sh /opt/bytebridge/bytebridge.unit
chmod 755 /opt/bytebridge/ByteBridge.Service

# The admin commands must run as the service account, or the settings
# file would end up owned by root and unreadable by the service.
# enroll, claim and unenroll are the exception: they install or remove
# the cloudflared systemd unit, which only root may do. They run as root,
# and the data they touched is handed back to the service account after.
cat > /usr/local/bin/bytebridge <<'WRAP'
#!/bin/sh
DATA=/var/lib/bytebridge
BIN=/opt/bytebridge/ByteBridge.Service

case "${1:-}" in
  enroll|claim|unenroll)
    [ "$(id -u)" -eq 0 ] || exec sudo "$0" "$@"
    env BYTEBRIDGE_DATA="$DATA" "$BIN" "$@"
    status=$?
    chown -R bytebridge:bytebridge "$DATA"
    [ ! -d "$DATA-keys" ] || chown -R bytebridge:bytebridge "$DATA-keys"
    exit "$status"
    ;;
esac

exec sudo -u bytebridge env BYTEBRIDGE_DATA="$DATA" "$BIN" "$@"
WRAP
chmod 755 /usr/local/bin/bytebridge

install -m 644 "$SRC/bytebridge.unit" /etc/systemd/system/bytebridge.service
systemctl daemon-reload
systemctl enable --now bytebridge
echo "ByteBridge installed. Try: bytebridge status"
