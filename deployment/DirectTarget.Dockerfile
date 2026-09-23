# The supported profile pins the selected runtime and delivery dependencies.
FROM node:22-bookworm-slim@sha256:83f487e0a63425e5b4d146fb5e5be574bcbe1b7b843d3ebafdd95eaf7767a7e5
RUN apt-get update && apt-get install -y --no-install-recommends \
    git ca-certificates python3 procps \
    && rm -rf /var/lib/apt/lists/* \
    && npm install --global @openai/codex@0.153.4
# Native Zeroshot invokes /usr/bin/gh and needs api --paginate --slurp.
ADD --checksum=sha256:f876a3b87bf67c94f773d17becca4dc7340b056dab901473a9260ee2a73e237b \
    https://github.com/cli/cli/releases/download/v2.101.0/gh_2.101.0_linux_amd64.deb /tmp/gh.deb
RUN dpkg --install /tmp/gh.deb && rm /tmp/gh.deb \
    && /usr/bin/gh --version \
    && /usr/bin/gh api graphql --paginate --slurp --help > /dev/null
COPY zeroshot /usr/local/bin/zeroshot
RUN echo 'afeb4372eaa63c3d88b308bd32afa5b888297fc0a82aa879542daf1437a6ee06  /usr/local/bin/zeroshot' | sha256sum --check \
    && zeroshot --version && zeroshot target serve --help > /dev/null
# The native hosted allocator needs root to assign its isolated process UIDs.
# Docker's default capabilities are sufficient; no privileged container is used.
ENV HOME=/home/node
ENV CODEX_HOME=/home/node/.codex
WORKDIR /home/node
COPY --chmod=755 direct-target-entrypoint.sh /usr/local/bin/broodling-target
ENTRYPOINT ["/usr/local/bin/broodling-target"]
