# Local build and test image for the Debian package: .NET SDK plus packaging and lint tools.
#   docker build -f packaging/tools.Dockerfile -t serval-pkg-tools .
FROM mcr.microsoft.com/dotnet/sdk:10.0
RUN apt-get update \
    && apt-get install --yes --no-install-recommends dpkg-dev lintian shellcheck jq xz-utils file \
    && rm -rf /var/lib/apt/lists/*
