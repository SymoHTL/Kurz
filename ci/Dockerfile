# The image the `gates` job runs in, and a local run of the gates (CLAUDE.md, "Build & Test"): the LLVM
# release the compiler pins, taken from the packages LLVM publishes for that major because the
# distribution has none of it (design record, section 2), the .NET SDK that compiler/global.json pins,
# and the tools the gates call. .github/workflows/image.yml builds it and publishes it; gates.yml names it
# by digest; tools/lint_ci.py pins the facts below. Every download is checked against its hash or its
# key before it is used.
FROM ubuntu:24.04@sha256:68ee22004a9109b44973313b02ff7b1de250f1de0f5ecb628d73f579aaff33ac

ARG LLVM_MAJOR=23
ARG LLVM_KEY=6084F3CF814B57C1CF12EFD515CF4D18AF4F7421
ARG DOTNET_SDK=10.0.402
ARG DOTNET_SHA512=1904a98dda2db34ba78ba3c4fb1b224455329af91c341d83547843c1839e112b04ae699ee92fa2449a8f62249196a938723078c0c60b9f65c88539d5687729c2
ARG GH_VERSION=2.103.0
ARG GH_SHA256=fdd768eb70f6409f80fcfa9f3a1c0adf0fc98d827ab3ce0bed2ac22762c5d786

LABEL org.opencontainers.image.source=https://github.com/SymoHTL/Kurz

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_ROOT=/usr/local/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    PATH=/usr/local/dotnet:$PATH

RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl gnupg git python3 python3-venv \
      libicu74 liblttng-ust1t64 libssl3t64 libstdc++6 tzdata zlib1g \
 && curl -fsSL -o /tmp/llvm.key https://apt.llvm.org/llvm-snapshot.gpg.key \
 && gpg --show-keys --with-colons /tmp/llvm.key | grep -qx "fpr:::::::::$LLVM_KEY:" \
 && gpg --dearmor -o /usr/share/keyrings/llvm.gpg /tmp/llvm.key \
 && echo "deb [signed-by=/usr/share/keyrings/llvm.gpg] https://apt.llvm.org/noble/ llvm-toolchain-noble-$LLVM_MAJOR main" \
      > /etc/apt/sources.list.d/llvm.list \
 && apt-get update \
 && apt-get install -y --no-install-recommends clang-$LLVM_MAJOR \
 && rm -rf /var/lib/apt/lists/* /tmp/llvm.key

RUN curl -fsSL -o /tmp/sdk.tar.gz "https://builds.dotnet.microsoft.com/dotnet/Sdk/$DOTNET_SDK/dotnet-sdk-$DOTNET_SDK-linux-x64.tar.gz" \
 && echo "$DOTNET_SHA512  /tmp/sdk.tar.gz" | sha512sum -c - \
 && mkdir -p "$DOTNET_ROOT" \
 && tar -xzf /tmp/sdk.tar.gz -C "$DOTNET_ROOT" \
 && rm /tmp/sdk.tar.gz

RUN curl -fsSL -o /tmp/gh.tar.gz "https://github.com/cli/cli/releases/download/v$GH_VERSION/gh_${GH_VERSION}_linux_amd64.tar.gz" \
 && echo "$GH_SHA256  /tmp/gh.tar.gz" | sha256sum -c - \
 && tar -xzf /tmp/gh.tar.gz -C /tmp \
 && install -m 0755 "/tmp/gh_${GH_VERSION}_linux_amd64/bin/gh" /usr/local/bin/gh \
 && rm -rf /tmp/gh.tar.gz "/tmp/gh_${GH_VERSION}_linux_amd64"

RUN clang-$LLVM_MAJOR --version && dotnet --version && gh --version && git --version && python3 --version
