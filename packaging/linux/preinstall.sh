#!/bin/sh
# Runs before the files are unpacked (deb: preinst, rpm: %pre). Makes the openvp user, once. Safe to run again.
set -e
if ! getent group openvp >/dev/null 2>&1; then
    groupadd --system openvp
fi
if ! getent passwd openvp >/dev/null 2>&1; then
    nologin=/usr/sbin/nologin
    [ -x "$nologin" ] || nologin=/sbin/nologin
    [ -x "$nologin" ] || nologin=/bin/false
    useradd --system --gid openvp --home-dir /var/lib/openvp --no-create-home --shell "$nologin" --comment "OpenVP service" openvp
fi
exit 0
