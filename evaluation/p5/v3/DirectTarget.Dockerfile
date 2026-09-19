# Preserve the base used by the retained target; rebuilding must not upgrade Node.
FROM node:22-bookworm-slim@sha256:83f487e0a63425e5b4d146fb5e5be574bcbe1b7b843d3ebafdd95eaf7767a7e5
RUN apt-get update && apt-get install -y --no-install-recommends \
    git ca-certificates python3 procps \
    && rm -rf /var/lib/apt/lists/* \
    && npm install --global @openai/codex@0.153.4
# The supported target is Linux amd64. Native Zeroshot 10.3.0 calls /usr/bin/gh
# directly and requires api --slurp; Debian bookworm's gh 2.23.0 lacks it.
ADD --checksum=sha256:f876a3b87bf67c94f773d17becca4dc7340b056dab901473a9260ee2a73e237b \
    https://github.com/cli/cli/releases/download/v2.101.0/gh_2.101.0_linux_amd64.deb /tmp/gh.deb
RUN dpkg --install /tmp/gh.deb && rm /tmp/gh.deb \
    && /usr/bin/gh --version \
    && /usr/bin/gh api graphql --paginate --slurp --help > /dev/null
COPY zeroshot /usr/local/bin/zeroshot
ENV HOME=/home/node
ENV CODEX_HOME=/home/node/.codex
WORKDIR /home/node
ENTRYPOINT ["zeroshot", "target", "serve"]
