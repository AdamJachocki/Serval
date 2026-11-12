#!/usr/bin/env bash
# Static checks and unit tests for the package's shell code. Used by CI and runnable locally
# (needs shellcheck, dash, bash and jq; the identity fixture also needs the tools it uses).
#
#   tests/packaging/run-shell-checks.sh
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.."

shellcheck -x -s sh deploy/serval-common.sh tests/packaging/test-serval-common.sh \
    packaging/serval-setup packaging/debian/postinst packaging/debian/prerm packaging/debian/postrm \
    packaging/debian/config packaging/debian/preinst
shellcheck -x -s bash deploy/install-identity.sh deploy/validate-web-identity.sh \
    tests/packaging/package-tests.sh tests/packaging/run-package-tests.sh \
    tests/packaging/run-shell-checks.sh tests/packaging/test-apt-repository.sh \
    tests/packaging/run-apt-repository-test.sh tests/packaging/test-install-script.sh \
    tests/packaging/run-install-script-test.sh packaging/build-deb.sh packaging/build-apt-repo.sh \
    packaging/stamp-install-script.sh packaging/assemble-site.sh tests/packaging/e2e-test.sh

shellcheck -x -s sh packaging/install.sh

echo '== shared library under dash'
dash tests/packaging/test-serval-common.sh
echo '== shared library under bash'
bash tests/packaging/test-serval-common.sh
echo '== identity deployment checks'
bash tests/Serval.Agent.Tests/Fixtures/test-web-identity.sh
echo 'Shell checks passed.'
