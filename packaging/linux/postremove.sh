#!/bin/sh
# Runs after the files are removed (deb: postrm, rpm: %postun). The data folder and the openvp user are kept, even on
# a purge, because they hold people's programs and users; to delete them by hand:
#   sudo rm -rf /var/lib/openvp /etc/openvp && sudo userdel openvp
set -e
if [ -d "${OPENVP_SYSTEMD_DIR:-/run/systemd/system}" ] && command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload >/dev/null 2>&1 || true
fi
case "$1" in
    purge|remove|0)
        if [ -d /var/lib/openvp ]; then
            echo "OpenVP's data in /var/lib/openvp was kept. Delete it with: sudo rm -rf /var/lib/openvp /etc/openvp && sudo userdel openvp"
        fi
        ;;
esac
exit 0
