#!/usr/bin/env bash
set -euo pipefail

web_entry=$(getent passwd serval-web) || {
    echo 'Dedicated Serval Web identity is unavailable.' >&2
    exit 2
}
IFS=: read -r web_name _ web_entry_uid web_entry_gid _ web_home web_shell <<< "$web_entry"
shadow_entry=$(getent shadow serval-web) || {
    echo 'Dedicated Serval Web identity must have a locked local password.' >&2
    exit 2
}
IFS=: read -r shadow_name shadow_password _ <<< "$shadow_entry"
if [[ $web_name != serval-web || $web_home != /nonexistent ||
      $web_shell != /usr/sbin/nologin || $shadow_name != serval-web ||
      ( $shadow_password != '!'* && $shadow_password != '*'* ) ]]; then
    echo 'Dedicated Serval Web identity must be locked and noninteractive.' >&2
    exit 2
fi

web_uid=$(id -u serval-web)
web_gid=$(id -g serval-web)
if [[ $web_uid == 0 || $web_gid == 0 ||
      $web_entry_uid != "$web_uid" || $web_entry_gid != "$web_gid" ]] ||
    [[ $(id -gn serval-web) != serval-web ]]; then
    echo 'Dedicated Serval Web identity is invalid.' >&2
    exit 2
fi
if [[ $(id -G serval-web) != "$web_gid" ]]; then
    echo 'Dedicated Serval Web identity must have no supplementary groups.' >&2
    exit 2
fi
