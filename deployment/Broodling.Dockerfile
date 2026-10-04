# The Broodling service image: the ASP.NET host as a fixed non-root user, serving HTTP (the read-only
# reader, or with its processing configuration also submission and automatic processing), with the
# store-only commands (initialization, inspection, installation pause) and, for the processing
# server's Attempts, retire-attempt and replace-attempt, and resume to dispatch such a Replacement
# Attempt. Build from the repository root with a read:packages credential for the Zeroshot.Client feed
# (nuget.config), passed as a build secret so no layer or build argument retains it:
#   export NuGetPackageSourceCredentials_github="Username=USER;Password=TOKEN"
#   docker build -f deployment/Broodling.Dockerfile \
#     --secret id=nuget-github,env=NuGetPackageSourceCredentials_github -t broodling:REVISION .
# It carries no Python, Python SDK, Codex launcher or native executable; its Zeroshot client is the
# Zeroshot.Client library. For Attempts that submit created, the
# invocation commands (submit, resume, wait, stop), retire-attempt and replace-attempt run from the
# release artifact on a host with gh and the caller checkout's common Git directory.

# The DirectTarget binding's native release archive (src/Broodling/DirectTargetBinding.cs and
# execution-assets/approval.json), the one the DirectTarget image installs. This pin is independent
# of the LocalTarget bridge's SDK wheel in bridge/requirements.txt.
FROM scratch AS native
ADD --checksum=sha256:fbc13b2385a088ff0f8fa03fdf72d4aa7ae6202d4289204e57ba1617628d6f16 \
    https://github.com/the-open-engine/zeroshot/releases/download/v10.10.0/zeroshot-v10.10.0-x86_64-unknown-linux-musl.tar.gz /zeroshot.tar.gz

# The same Ubuntu 24.04 as the runtime stage, so libbroodling_git.so links against its libc.
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build
RUN apt-get update && apt-get install -y --no-install-recommends gcc libc6-dev python3 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /source
COPY nuget.config ./
COPY src/Broodling/ src/Broodling/
COPY src/Broodling.Host/ src/Broodling.Host/
# A CI build: the application library restores in locked mode from its packages.lock.json.
RUN --mount=type=secret,id=nuget-github,required=true \
    NuGetPackageSourceCredentials_github="$(cat /run/secrets/nuget-github)" \
    dotnet publish src/Broodling.Host --configuration Release --output /app -p:ContinuousIntegrationBuild=true
# The published output must carry the approved execution asset, or HTTP preparation refuses. The
# pinned native regenerates it with Zeroshot's own tooling and admits it (generate.sh); the published
# bytes must equal what it produced, and the published approval must be the reviewed one.
RUN --mount=type=bind,from=native,source=/zeroshot.tar.gz,target=/tmp/zeroshot.tar.gz \
    tar -xzf /tmp/zeroshot.tar.gz -C /tmp zeroshot \
    && src/Broodling/execution-assets/generate.sh /tmp/zeroshot /tmp/execution-asset.json \
    && cmp /tmp/execution-asset.json /app/execution-assets/software-change-pr-codex-gateway.json \
    && cmp src/Broodling/execution-assets/approval.json /app/execution-assets/approval.json \
    && rm /tmp/zeroshot /tmp/execution-asset.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble@sha256:2d584d8147faddb0d678c5748d47953e5b8e18621ed4fb7049a91381d9d7746f
LABEL org.opencontainers.image.source=https://github.com/faviann/broodling
# Git for source/accepted-object custody; curl only for the credential-free health check; tini as
# PID 1, because administrative Git refuses a PID-1 host process (dotnet-worktree-materialization.md).
# The processing server acquires issues and repositories through gh, the DirectTarget image's pin.
# Completion fetches each accepted commit with plain Git, which asks gh for the current GH_TOKEN, so a
# private repository's result can be retained; acquisition ignores system Git configuration.
ADD --checksum=sha256:f876a3b87bf67c94f773d17becca4dc7340b056dab901473a9260ee2a73e237b \
    https://github.com/cli/cli/releases/download/v2.101.0/gh_2.101.0_linux_amd64.deb /tmp/gh.deb
RUN apt-get update && apt-get install -y --no-install-recommends git curl tini \
    && rm -rf /var/lib/apt/lists/* \
    && dpkg --install /tmp/gh.deb && rm /tmp/gh.deb \
    && /usr/bin/gh --version \
    && git config --system credential.https://github.com.helper '!/usr/bin/gh auth git-credential'
COPY --from=build /app /app
# The image's non-root `app` user. The operator's durable state directory, mounted at
# /var/lib/broodling, must be owned by it; the image never changes mounted ownership.
USER 1654:1654
ENV HOME=/home/app \
    Broodling__Store=/var/lib/broodling/state.sqlite3 \
    ASPNETCORE_HTTP_PORTS=8080
WORKDIR /home/app
# The DirectTarget image's reference helper reads from http://broodling:8080.
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD ["curl", "--fail", "--silent", "--show-error", "--max-time", "4", "--output", "/dev/null", "http://127.0.0.1:8080/health"]
ENTRYPOINT ["/usr/bin/tini", "--", "dotnet", "/app/Broodling.Host.dll"]
ARG REVISION=unknown
LABEL org.opencontainers.image.revision=$REVISION
