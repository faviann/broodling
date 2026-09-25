# Controlled provider and forge over the unchanged DirectTarget image; the native binary is untouched.
ARG BASE
FROM ${BASE}
COPY --chmod=755 codex /usr/local/bin/codex
COPY --chmod=755 gh /usr/bin/gh
COPY --chmod=755 git /usr/bin/git
COPY --chmod=644 forge.gitconfig /etc/broodling-forge.gitconfig
