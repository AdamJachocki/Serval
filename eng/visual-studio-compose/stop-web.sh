#!/bin/sh
set -eu
[ "$#" -eq 0 ] || { echo 'Web stop does not accept arguments.' >&2; exit 1; }
if [ "$(id -u)" -eq 0 ]; then
    exec /usr/bin/setpriv --reuid=serval-web --regid=serval-web --clear-groups \
        --bounding-set=-all --inh-caps=-all --ambient-caps=-all --no-new-privs \
        /opt/serval/development/stop-web.sh
fi
[ "$(id -u)" -eq "$(id -u serval-web)" ] || exit 1
# Never signal Agent, systemd, vsdbg, or Visual Studio's dotnet helper.
result=0
pkill -TERM -u "$(id -u)" -f '^/usr/bin/dotnet /opt/serval/web/Serval\.Web\.dll( |$)' || result=$?
[ "$result" -le 1 ]
