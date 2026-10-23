#!/bin/sh
set -eu
[ "$#" -eq 1 ] && [ "$1" = --interpreter=vscode ] || {
    echo 'Unsupported development debugger invocation.' >&2; exit 1;
}
[ "$(id -u)" -eq 0 ] || { echo 'Debugger bootstrap requires root.' >&2; exit 1; }
case "$(uname -m)" in
    x86_64) debugger=/opt/serval/vsdbg/vsdbg ;;
    aarch64) debugger=/opt/serval/vsdbg/linux-arm64/vsdbg ;;
    *) echo 'Unsupported debugger architecture.' >&2; exit 1 ;;
esac
[ -x "$debugger" ] || { echo 'Visual Studio debugger mount is missing.' >&2; exit 1; }
# Container Tools sends targetOutputLogPath=/dev/console to vsdbg. Docker's
# private TTY is root:tty 0620; allow only the Web group to write its output.
# Never follow a substituted console path or grant console read access.
[ ! -L /dev/console ] && [ -c /dev/console ] && \
    [ "$(stat -c %u /dev/console)" -eq 0 ] || {
    echo 'Unsafe development console device.' >&2; exit 1;
}
chgrp --no-dereference serval-web /dev/console
chmod 0620 /dev/console
# Both debugger and debuggee inherit the same unprivileged identity.
exec /usr/bin/setpriv --reuid=serval-web --regid=serval-web --clear-groups \
    --bounding-set=-all --inh-caps=-all --ambient-caps=-all --no-new-privs \
    "$debugger" --interpreter=vscode
