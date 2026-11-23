#!/bin/sh
# Runs after the files are unpacked (deb: postinst, rpm: %post). deb passes "configure" and, on an upgrade, the
# old version; rpm passes how many copies will be installed (1 for a new install, 2 or more for an upgrade).
set -e

mode=other
case "${1:-}" in
    configure) if [ -n "${2:-}" ]; then mode=upgrade; else mode=install; fi ;;
    1) mode=install ;;
    [2-9]|[1-9][0-9]*) mode=upgrade ;;
esac

# The data folder and its owner. (systemd also makes it, for the service, with StateDirectory=.) Existing data is left alone.
mkdir -p /var/lib/openvp
chown openvp:openvp /var/lib/openvp
chmod 0750 /var/lib/openvp

if [ -d "${OPENVP_SYSTEMD_DIR:-/run/systemd/system}" ] && command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload || true
    if [ "$mode" = install ]; then
        # Start at boot, but not now: set the console up first.
        systemctl enable openvp.service >/dev/null 2>&1 || true
    elif [ "$mode" = upgrade ]; then
        # An upgrade restarts the service only if it was running, so a stopped one stays stopped.
        systemctl try-restart openvp.service >/dev/null 2>&1 || true
    fi
fi

if [ "$mode" = install ]; then
    cat <<MSG

OpenVP is installed. It will start at boot, and has not been started now.

  Start it now:      sudo systemctl start openvp
  Open the console:  http://localhost:8080
  Status and log:    systemctl status openvp    journalctl -u openvp
  Settings:          /etc/openvp/openvp.env  (then: sudo systemctl restart openvp)
  Data:              /var/lib/openvp  (kept when the package is upgraded or removed)

MSG
fi
exit 0
