# The supported profile pins the selected runtime and delivery dependencies. Build with the
# deployment directory as context:
#   docker build -f deployment/DirectTarget.Dockerfile -t broodling-target:REVISION deployment

# The DirectTarget binding's native release (src/Broodling/DirectTargetBinding.cs and
# execution-assets/approval.json): the official Linux x86-64 release archive, pinned by checksum. It
# carries the zeroshot executable and the restic executable native run allocation requires beside it.
FROM scratch AS native
ADD --checksum=sha256:fbc13b2385a088ff0f8fa03fdf72d4aa7ae6202d4289204e57ba1617628d6f16 \
    https://github.com/the-open-engine/zeroshot/releases/download/v10.10.0/zeroshot-v10.10.0-x86_64-unknown-linux-musl.tar.gz /zeroshot.tar.gz

FROM node:22-bookworm-slim@sha256:83f487e0a63425e5b4d146fb5e5be574bcbe1b7b843d3ebafdd95eaf7767a7e5
RUN apt-get update && apt-get install -y --no-install-recommends \
    git ca-certificates openssl python3 procps \
    && rm -rf /var/lib/apt/lists/* \
    && npm install --global @openai/codex@0.153.4
# Native Zeroshot invokes /usr/bin/gh and needs api --paginate --slurp.
ADD --checksum=sha256:f876a3b87bf67c94f773d17becca4dc7340b056dab901473a9260ee2a73e237b \
    https://github.com/cli/cli/releases/download/v2.101.0/gh_2.101.0_linux_amd64.deb /tmp/gh.deb
RUN dpkg --install /tmp/gh.deb && rm /tmp/gh.deb \
    && /usr/bin/gh --version \
    && /usr/bin/gh api graphql --paginate --slurp --help > /dev/null
RUN --mount=type=bind,from=native,source=/zeroshot.tar.gz,target=/tmp/zeroshot.tar.gz \
    tar -xzf /tmp/zeroshot.tar.gz -C /usr/local/bin --no-same-owner zeroshot restic \
    && chmod 755 /usr/local/bin/zeroshot /usr/local/bin/restic \
    && printf '%s  %s\n' d0c84ffbafa731ef7fa6b61f87af9c000cc4e5b4d2e0d3b7df461fd239bb923e /usr/local/bin/zeroshot \
        90ab22a5e731063c27590e704e8da2f4d9bae59a67899bd45d0904afc868a8cf /usr/local/bin/restic | sha256sum --check \
    && zeroshot --version && zeroshot target serve --help > /dev/null && restic version
# The native hosted allocator needs root to assign its isolated process UIDs.
# Docker's default capabilities are sufficient; no privileged container is used.
ENV HOME=/home/node
ENV CODEX_HOME=/home/node/.codex
WORKDIR /home/node
COPY --chmod=755 direct-target-entrypoint.sh /usr/local/bin/broodling-target
# Native agents read frozen references on demand from Broodling's read-only HTTP reader, which
# the single Compose project serves as the `broodling` service (ADR 0001).
COPY --chmod=755 broodling-reference /usr/local/bin/broodling-reference
RUN mkdir -p /etc/broodling && echo 'http://broodling:8080' > /etc/broodling/reader-origin
ENTRYPOINT ["/usr/local/bin/broodling-target"]
LABEL org.opencontainers.image.source=https://github.com/faviann/broodling
ARG REVISION=unknown
LABEL org.opencontainers.image.revision=$REVISION
