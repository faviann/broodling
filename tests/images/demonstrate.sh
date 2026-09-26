#!/usr/bin/env bash
# Demonstrates the Broodling and DirectTarget images in a disposable instance of ADR 0001's topology
# (compose.yaml): explicit initialization, service startup as the production users, real mount
# ownership, credential-free health, in-project network reads and discovery, then host-only
# readiness of the containers, mounts and pinned dependencies. No service gets a Docker socket.
# Needs rootful Docker with Compose, curl, jq and a .NET 10 ASP.NET runtime on the host. Uses no
# credentials, provider, GitHub or existing target; everything it creates is removed on exit.
# Usage: demonstrate.sh BROODLING_IMAGE TARGET_IMAGE [FACTS_JSON]
set -euo pipefail

usage='usage: demonstrate.sh BROODLING_IMAGE TARGET_IMAGE [FACTS_JSON]'
broodling_image=${1:?$usage}
target_image=${2:?$usage}
facts=${3:-}
here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
origin=https://zeroshot.dev.faviann.com
host=${origin#https://}
broodling_user=1654:1654
project="broodling-121-$(od -An -N6 -tx1 /dev/urandom | tr -d ' \n')"
base="${BROODLING_TEST_WORKSPACE_ROOT:-$HOME/.cache/broodling-tests}"
mkdir -p -- "$base"
# Readiness compares canonical host paths.
root="$(realpath -- "$base")/image-demo-$project"
export DEMO_ROOT="$root" BROODLING_IMAGE="$broodling_image" TARGET_IMAGE="$target_image"

compose() { docker compose --project-name "$project" --file "$here/compose.yaml" "$@"; }
step() { printf '\n== %s\n' "$*"; }
fail() { printf 'FAILED: %s\n' "$*" >&2; exit 1; }
bind() { printf 'type=bind,src=%s,dst=%s' "$1" "$2"; }

cleanup() {
    compose down --volumes --remove-orphans --timeout 5 >/dev/null 2>&1 || :
    if [[ ${created_root-} == true ]]; then
        # Root- and service-owned files: remove them as root before deleting the owned directory.
        docker run --rm --network none --mount "$(bind "$root" /owned)" --entrypoint find "$target_image" \
            /owned -mindepth 1 -delete || :
        rmdir -- "$root" || :
    fi
    # Remove the pinned Caddy image only if this run pulled it.
    if [[ ${pulled_tls-} == true ]]; then docker image rm "$tls_image" >/dev/null 2>&1 || :; fi
}
trap cleanup EXIT
tls_image="$(sed -n 's/^ *image: \(caddy:.*\)$/\1/p' "$here/compose.yaml")"
docker image inspect "$tls_image" >/dev/null 2>&1 || pulled_tls=true

step "Disposable host directories with production ownership ($root)"
mkdir -- "$root"
created_root=true
# As the explicit first initialization may: a one-off root helper sets ownership and modes.
docker run --rm --network none --mount "$(bind "$root" /owned)" --workdir /owned --entrypoint /bin/sh "$target_image" -ec \
    "mkdir -m 0700 target-state target-home broodling && mkdir tls-root-key tls-root && chown $broodling_user broodling"

step 'Create the TLS root once (target image, one-off root helper)'
docker run --rm --network none --mount "$(bind "$root/tls-root-key" /tls-root-key)" \
    --mount "$(bind "$root/tls-root" /tls-root)" "$target_image" initialize-tls

step 'Start zeroshot-tls'
compose up --detach --quiet-pull zeroshot-tls
tls_port="$(compose port zeroshot-tls 443)"
tls_port=${tls_port##*:}
for _ in $(seq 1 100); do
    # Any HTTP status means Caddy completed a handshake that chains to the root.
    code="$(curl --silent --output /dev/null --write-out '%{http_code}' --max-time 2 --cacert "$root/tls-root/root.crt" \
        --resolve "$host:$tls_port:127.0.0.1" "https://$host:$tls_port/" || :)"
    [[ $code != 000 ]] && break
    sleep 0.2
done
[[ $code != 000 ]] || fail 'zeroshot-tls never served with the root'

step 'Initialize native state through the origin, then start zeroshot'
compose run --rm --no-deps --use-aliases -T zeroshot initialize \
    --listen 0.0.0.0:18770 --public-origin "$origin" --storage /state
compose up --detach zeroshot

step 'Initialize the Broodling store as the image user, then start broodling'
store="$(compose run --rm --no-deps -T broodling initialize-store /var/lib/broodling/state.sqlite3)"
printf '%s\n' "$store"
compose up --detach --wait --wait-timeout 120 broodling
health="$(docker inspect --format '{{.State.Health.Status}}' "$(compose ps --quiet broodling)")"
[[ $health == healthy ]] || fail "broodling health is $health"
echo "broodling: $health (image health check GET /health, no credentials)"

step 'Network application checks on the project network'
reply="$(compose exec -T zeroshot node -e \
    'fetch("http://broodling:8080/health").then(r => r.text().then(t => { console.log(r.status, t); process.exit(r.ok ? 0 : 1); }))')"
echo "zeroshot -> http://broodling:8080/health: $reply"
[[ $reply == '200 {"status":"ok"}' ]] || fail 'Broodling health over the network'
# The installed reference helper reaches the reader by service name; the empty store has no bundle,
# so the reader's own refusal is the expected answer.
if reference="$(compose exec -T zeroshot broodling-reference demo-bundle demo-reference 2>&1)"; then
    fail 'the reader returned a reference from an empty store'
fi
echo "zeroshot broodling-reference: $reference"
[[ $reference == 'broodling-reference: read refused (404 unknown_record)' ]] || fail 'reference helper to reader'
for _ in $(seq 1 100); do
    # Broodling's own path to the target: the origin alias, zeroshot-tls, trusting only the public root.
    discovery="$(compose exec -T broodling curl --silent --fail --proto =https --max-time 5 --cacert /tls-root/root.crt \
        "$origin/.well-known/zeroshot-native-v2" || :)"
    [[ -n $discovery ]] && break
    sleep 0.2
done
kind="$(jq -r .kind <<<"${discovery:-null}")"
echo "broodling -> $origin discovery: $kind"
[[ $kind == zeroshot.native-v2-target/v2 ]] || fail 'target discovery from broodling through the origin'

step 'Host-only checks (Docker host, never a service)'
container() { compose ps --all --format '{{.Name}}' "$1"; }
for service in broodling zeroshot zeroshot-tls; do
    if docker inspect --format '{{range .Mounts}}{{println .Source}}{{end}}' "$(container "$service")" | grep -q 'docker\.sock'; then
        fail "$service mounts a Docker socket"
    fi
done
echo 'no service mounts a Docker socket'
broodling_container="$(container broodling)"
[[ "$(docker inspect --format '{{.Config.User}}' "$broodling_container")" == "$broodling_user" ]] || fail 'broodling user'
# Administrative Git refuses a PID-1 host process, so the application must run under an init.
[[ "$(docker exec "$broodling_container" cat /proc/1/comm)" == tini ]] || fail 'the application runs as PID 1'
mounts="$(docker inspect --format '{{json .Mounts}}' "$broodling_container" \
    | jq -c 'map({Type, Source, Destination, RW}) | sort_by(.Destination)')"
expected="$(jq -cn --arg root "$root" '[{Type: "bind", Source: "\($root)/tls-root", Destination: "/tls-root", RW: false},
    {Type: "bind", Source: "\($root)/broodling", Destination: "/var/lib/broodling", RW: true}]')"
[[ $mounts == "$expected" ]] || fail "broodling mounts $mounts"
# The state directory is private to the service user, so inspect it as root.
ownership="$(docker run --rm --network none --mount "$(bind "$root/broodling" /state),readonly" --entrypoint stat "$target_image" \
    -c '%n %u:%g %a' /state /state/state.sqlite3)"
printf '%s\n' "$ownership"
[[ $ownership == "/state $broodling_user 700"$'\n'"/state/state.sqlite3 $broodling_user "* ]] || fail 'broodling state ownership'
echo "broodling runs as $broodling_user; its only writable mount is its state, where the store it created is its own"

# The image's own check-target command, run on the host as the operator with Docker access.
extract="$(docker create "$broodling_image")"
docker cp --quiet "$extract:/app" "$root/host"
docker rm "$extract" >/dev/null
jq -n --arg origin "$origin" --arg root "$root" --arg network "${project}_default" \
    --arg target "$(container zeroshot)" --arg tls "$(container zeroshot-tls)" --arg broodling "$broodling_container" \
    --arg image "$(docker inspect --format '{{.Image}}' "$(container zeroshot)")" \
    '{containerName: $target, imageId: $image, directOrigin: $origin, stateMount: "\($root)/target-state",
      homeMount: "\($root)/target-home", network: $network, tlsContainerName: $tls,
      rootKeyMount: "\($root)/tls-root-key", rootCertificateMount: "\($root)/tls-root", broodlingContainerName: $broodling}' \
    >"$root/target-inventory.json"
jq -n --arg origin "$origin" --arg root "$root" \
    '{target: "direct", directOrigin: $origin, directRootCertificate: "\($root)/tls-root/root.crt"}' >"$root/config.json"
readiness="$(dotnet "$root/host/Broodling.Host.dll" check-target "$root/target-inventory.json" "$root/config.json")" \
    || fail "check-target: $readiness"
printf '%s\n' "$readiness"
[[ "$(jq -r .ready <<<"$readiness")" == true ]] || fail 'check-target is not ready'

if [[ -n $facts ]]; then
    jq -n --argjson store "$store" --argjson readiness "$readiness" \
        --arg tls "$(docker inspect --format '{{.Config.Image}}' "$(container zeroshot-tls)")" \
        '{storeFormat: $store.schema.format, storeSchemaVersion: $store.schema.schemaVersion,
          storeDefinitionSha256: $store.schema.definitionSha256,
          upgradesFrom: ($store.upgradesFrom | map({storeFormat: .format, storeSchemaVersion: .schemaVersion,
            storeDefinitionSha256: .definitionSha256})),
          zeroshotTlsImage: $tls, readiness: ($readiness | {versions, apiPaginateSlurp, hostedUidTransition, providerTasks})}' \
        >"$facts"
fi
step 'Demonstration passed'
