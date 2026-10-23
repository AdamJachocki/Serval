#!/bin/sh
set -eu

# Fixed standalone smoke launcher; Visual Studio debugs dotnet directly.
[ "$#" -eq 0 ] || { echo 'Web launcher does not accept arguments.' >&2; exit 1; }
[ "$(id -u)" -eq 0 ] || { echo 'Web launcher requires root to drop identity.' >&2; exit 1; }
exec /usr/bin/setpriv --reuid=serval-web --regid=serval-web --clear-groups \
    --bounding-set=-all --inh-caps=-all --ambient-caps=-all --no-new-privs \
    /usr/bin/dotnet /opt/serval/web/Serval.Web.dll \
    --staticwebassets /opt/serval/web/disabled.staticwebassets.runtime.json \
    --webroot /opt/serval/web/wwwroot
