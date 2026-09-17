FROM node:22-bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends \
    git gh ca-certificates python3 procps \
    && rm -rf /var/lib/apt/lists/* \
    && npm install --global @openai/codex@0.153.4
COPY zeroshot /usr/local/bin/zeroshot
ENV HOME=/home/node
ENV CODEX_HOME=/home/node/.codex
WORKDIR /home/node
ENTRYPOINT ["zeroshot", "target", "serve"]
