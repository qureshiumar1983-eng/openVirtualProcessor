#!/bin/sh
# Runs before the files are removed (deb: prerm, rpm: %preun). Does nothing on an upgrade, so the running
# service keeps going until the new version restarts it. deb passes "remove", "upgrade" and so on; rpm passes
# the number of copies that will remain (0 for a real removal).
set -e
remove=0
case "$1" in
    remove|deconfigure) remove=1 ;;
    0) remove=1 ;;
esac
if [ "$remove" -eq 1 ] && [ -d "${OPENVP_SYSTEMD_DIR:-/run/systemd/system}" ] && command -v systemctl >/dev/null 2>&1; then
    systemctl stop openvp.service >/dev/null 2>&1 || true
    systemctl disable openvp.service >/dev/null 2>&1 || true
fi
exit 0
