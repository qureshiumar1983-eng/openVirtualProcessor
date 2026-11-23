#!/usr/bin/env bash
# Checks the maintainer scripts without touching this machine: every command that would change it (useradd,
# systemctl, mkdir, chown, ...) is replaced by a stub that writes down what it was asked to do.
#   packaging/linux/test-scripts.sh
set -u
here=$(cd "$(dirname "$0")" && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin" "$work/systemd"
fail=0

for cmd in groupadd useradd systemctl chown chmod mkdir; do
    printf '#!/bin/sh\necho "%s $*" >> "$CALLS"\n' "$cmd" > "$work/bin/$cmd"
    chmod +x "$work/bin/$cmd"
done
# getent: says the user and group are missing until $EXISTS is set
printf '#!/bin/sh\n[ -n "${EXISTS:-}" ]\n' > "$work/bin/getent"; chmod +x "$work/bin/getent"

run() { # script args...  (state in $EXISTS, $SYSTEMD)
    local script=$1; shift
    : > "$work/calls"
    CALLS="$work/calls" PATH="$work/bin:$PATH" OPENVP_SYSTEMD_DIR="${SYSTEMD:-$work/none}" sh "$here/$script" "$@" > "$work/out" 2>&1
    echo $? > "$work/rc"
}
check() { # name, pattern that must be in the calls, [absent]
    local name=$1 pattern=$2 want=${3:-present}
    if grep -q -- "$pattern" "$work/calls"; then got=present; else got=absent; fi
    if [ "$got" = "$want" ] && [ "$(cat "$work/rc")" = 0 ]; then echo "  ok   $name"; else echo "  FAIL $name (wanted $pattern $want; calls:)"; sed 's/^/         /' "$work/calls"; fail=1; fi
}
out_has() { grep -q -- "$2" "$work/out" && echo "  ok   $1" || { echo "  FAIL $1"; fail=1; }; }
out_lacks() { grep -q -- "$2" "$work/out" && { echo "  FAIL $1"; fail=1; } || echo "  ok   $1"; }

unset EXISTS SYSTEMD
run preinstall.sh install;                         check "a new install makes the group" "groupadd --system openvp"
check "a new install makes the user" "useradd --system --gid openvp --home-dir /var/lib/openvp"
EXISTS=1 run preinstall.sh upgrade;                check "an existing user is not made again" "useradd" absent
unset EXISTS

SYSTEMD="$work/systemd" run postinstall.sh configure; check "deb first install: enabled" "systemctl enable openvp.service"
check "deb first install: not started" "systemctl .*start" absent
check "deb first install: data folder is owned by openvp" "chown openvp:openvp /var/lib/openvp"
out_has "deb first install: says how to start" "sudo systemctl start openvp"
SYSTEMD="$work/systemd" run postinstall.sh configure 0.1.0; check "deb upgrade: restarts only if running" "systemctl try-restart openvp.service"
check "deb upgrade: not enabled again" "systemctl enable" absent
out_lacks "deb upgrade: no first-install message" "OpenVP is installed"
SYSTEMD="$work/systemd" run postinstall.sh 1;      check "rpm first install: enabled" "systemctl enable openvp.service"
SYSTEMD="$work/systemd" run postinstall.sh 2;      check "rpm upgrade: restarts only if running" "systemctl try-restart openvp.service"
check "rpm upgrade: not enabled again" "systemctl enable" absent
run postinstall.sh configure;                      check "no systemd (a container): systemctl is never called" "systemctl" absent
out_has "no systemd: still prints the start instructions" "OpenVP is installed"

SYSTEMD="$work/systemd" run preremove.sh upgrade;  check "deb upgrade: the service is not stopped" "systemctl" absent
SYSTEMD="$work/systemd" run preremove.sh remove;   check "deb remove: stopped" "systemctl stop openvp.service"
check "deb remove: disabled" "systemctl disable openvp.service"
SYSTEMD="$work/systemd" run preremove.sh 1;        check "rpm upgrade: the service is not stopped" "systemctl" absent
SYSTEMD="$work/systemd" run preremove.sh 0;        check "rpm remove: stopped" "systemctl stop openvp.service"
mkdir -p "$work/fake-var"
SYSTEMD="$work/systemd" run postremove.sh purge;   check "purge: units reloaded" "systemctl daemon-reload"
check "purge: nothing is deleted (no rm)" "rm " absent
for s in preinstall postinstall preremove postremove; do
    sh -n "$here/$s.sh" && echo "  ok   $s.sh parses" || { echo "  FAIL $s.sh syntax"; fail=1; }
done
[ "$fail" = 0 ] && echo "all passed" || echo "FAILED"
exit $fail
