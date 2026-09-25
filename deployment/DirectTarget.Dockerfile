# The supported profile pins the selected runtime and delivery dependencies. Build with the
# deployment directory as context:
#   docker build -f deployment/DirectTarget.Dockerfile -t broodling-target:REVISION deployment

# The official SDK wheel, only for its pinned native binary (the same pin as bridge/requirements.txt).
FROM scratch AS wheel
ADD --checksum=sha256:f3629459837a27b7496f98fe0034e7b47a00079d93d3374922c2960952b8ace9 \
    https://github.com/the-open-engine/zeroshot/releases/download/zeroshot-python-v10.3.0_1/the_open_engine_zeroshot-10.3.0.post1-py3-none-manylinux_2_17_x86_64.whl /zeroshot.whl

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
RUN --mount=type=bind,from=wheel,source=/zeroshot.whl,target=/tmp/zeroshot.whl \
    python3 -c 'import shutil, zipfile; shutil.copyfileobj(zipfile.ZipFile("/tmp/zeroshot.whl").open("the_open_engine_zeroshot-10.3.0.post1.data/purelib/zeroshot/_bin/zeroshot"), open("/usr/local/bin/zeroshot", "wb"))' \
    && chmod 755 /usr/local/bin/zeroshot \
    && echo 'afeb4372eaa63c3d88b308bd32afa5b888297fc0a82aa879542daf1437a6ee06  /usr/local/bin/zeroshot' | sha256sum --check \
    && zeroshot --version && zeroshot target serve --help > /dev/null
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
