#!/bin/bash
set -Eeuo pipefail
export LC_ALL=C

if (( $# != 2 )); then
    printf 'The cleanup verifier requires a harness and oracle.\n' >&2
    exit 1
fi

harness="$1"
oracle="$2"
if [[ ! -f "$harness" || ! -x "$oracle" ]]; then
    printf 'The cleanup verifier input is unavailable.\n' >&2
    exit 1
fi

prefix="serval-enumeration-test-$(cat /proc/sys/kernel/random/uuid)"
[[ "$prefix" =~ ^serval-enumeration-test-[a-f0-9-]+$ ]]
instance_id="${prefix#serval-enumeration-test-}"
privileged_name="serval-agent@$instance_id.service"
privileged_alias_name="serval-exclusion-alias@$instance_id.service"
lookalike_name="serval-agent-helper@$instance_id.service"
export SERVAL_ENUMERATION_FIXTURE_PREFIX="$prefix"
export SERVAL_ENVIRONMENT_ORACLE="$oracle"

if bash "$harness" /usr/bin/false; then
    printf 'The deliberate harness failure unexpectedly succeeded.\n' >&2
    exit 1
fi

for listing in \
    "$(systemctl list-unit-files --all --no-legend --plain --type=service)" \
    "$(systemctl list-units --all --no-legend --plain --type=service)"; do
    while IFS= read -r line; do
        name="${line%% *}"
        if [[ "$name" == "$prefix"* || "$name" == "$privileged_name" ||
              "$name" == "$privileged_alias_name" || "$name" == "$lookalike_name" ]]; then
            printf 'A failed harness left a manager fixture behind.\n' >&2
            exit 1
        fi
    done <<< "$listing"
done

if find /run/systemd/system /run/systemd/generator \
    -maxdepth 2 -name "$prefix*" -print -quit | grep -q .; then
    printf 'A failed harness left a systemd fixture behind.\n' >&2
    exit 1
fi

for path in "/run/systemd/system/$privileged_name" \
    "/run/systemd/system/$privileged_alias_name" \
    "/run/systemd/system/$lookalike_name"; do
    if [[ -e "$path" || -L "$path" ]]; then
        printf 'A failed harness left a protected-family fixture behind.\n' >&2
        exit 1
    fi
done

if [[ -e "/run/serval-environment-tests/$prefix" ||
      -L "/run/serval-environment-tests/$prefix" ]]; then
    printf 'A failed harness left a private fixture behind.\n' >&2
    exit 1
fi
