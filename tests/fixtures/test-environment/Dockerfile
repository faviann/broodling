# Test-only environment T (#235): the adopted dotnet-r5 image with only its OS identity relabelled. Native, Claude Code,
# the entrypoint and every package stay the base's bytes. The suite builds it, publishes it to a disposable loopback
# registry and runs it by digest; it is never published or approved for real work.
ARG BASE
FROM ${BASE}
# /etc/os-release is the base's link to this file.
COPY --chmod=644 os-release /usr/lib/os-release
