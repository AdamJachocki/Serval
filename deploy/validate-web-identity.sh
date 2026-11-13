#!/usr/bin/env bash
set -euo pipefail

# shellcheck source=deploy/serval-common.sh
. "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/serval-common.sh"
serval_validate_web_identity
