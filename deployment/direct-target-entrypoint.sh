#!/bin/sh
# Target-package lifecycle only. Native owns creation, origin validity and all run data.
set -eu
refuse() { echo "Native target state refused: $*" >&2; exit 1; }
# zeroshot-tls runs Caddy as this package-defined non-root user, the only reader of the root key.
tls_user=10443:10443
if [ "${1-}" = initialize-tls ]; then
    # One-off root helper: creates zeroshot-tls's root once, before Caddy first starts. Rotation
    # empties both locations deliberately first; an existing or partial root is never overwritten.
    tls_refuse() { echo "TLS root refused: $*" >&2; exit 1; }
    [ "$#" -eq 1 ] || tls_refuse 'expected initialize-tls with no arguments'
    [ "$(id -u)" = 0 ] || tls_refuse 'container root is required to set root ownership'
    for path in /tls-root-key /tls-root; do
        [ -d "$path" ] || tls_refuse "missing directory: $path"
        [ -z "$(ls -A "$path")" ] || tls_refuse "$path is not empty; an existing root is never replaced"
    done
    [ "$(stat -c %d:%i /tls-root-key)" != "$(stat -c %d:%i /tls-root)" ] \
        || tls_refuse 'the root key and certificate need separate locations'
    umask 077
    openssl ecparam -name prime256v1 -genkey -noout -out /tls-root-key/root.key
    openssl req -x509 -new -key /tls-root-key/root.key -sha256 -days 3650 \
        -subj "/CN=Broodling DirectTarget Root $(date -u +%Y%m%dT%H%M%SZ)" \
        -addext 'basicConstraints=critical,CA:TRUE' -addext 'keyUsage=critical,keyCertSign,cRLSign' \
        -out /tls-root/root.crt
    chown "$tls_user" /tls-root-key /tls-root-key/root.key
    chmod 0700 /tls-root-key
    chmod 0400 /tls-root-key/root.key
    chown 0:0 /tls-root /tls-root/root.crt
    chmod 0755 /tls-root
    chmod 0644 /tls-root/root.crt
    echo 'TLS root created; the key is readable only by zeroshot-tls.'
    exit 0
fi
mode=serve
if [ "${1-}" = initialize ]; then mode=initialize; shift; fi
# Keep the supported internal paths and the inner port fixed; accept no native option bypass.
# zeroshot-tls forwards the origin to this port over the project network; it is never published.
[ "$#" -eq 6 ] && [ "$1" = --listen ] && [ "$2" = 0.0.0.0:18770 ] && [ "$3" = --public-origin ] && [ "$5" = --storage ] \
    || refuse 'expected [initialize] --listen 0.0.0.0:18770 --public-origin ORIGIN --storage /state'
origin=$4
[ "$6" = /state ] && [ "${HOME-}" = /home/node ] && [ "${CODEX_HOME-}" = /home/node/.codex ] \
    || refuse 'configured state/home paths differ from /state and /home/node'
[ -z "${ZEROSHOT_CONFIG_DIR-}" ] && [ -z "${XDG_CONFIG_HOME-}" ] \
    || refuse 'native configuration must remain under /home/node'
[ "$(id -u)" = 0 ] || refuse 'container root is required for native process identities'
unredirected() { [ "$(realpath "$1")" = "$1" ]; }
for path in /state /home/node; do
    [ -d "$path" ] && unredirected "$path" || refuse "missing or redirected directory: $path"
done
registry=/home/node/.config/zeroshot/targets.json
check_state() {
    [ -d /state/runs ] && unredirected /state/runs || refuse 'missing or redirected directory: /state/runs'
    for path in /state/runs.sqlite3 "$registry"; do
        [ -f "$path" ] && [ -s "$path" ] && unredirected "$path" || refuse "missing or redirected initialized file: $path"
    done
    # Native creates its tables in any SQLite file it opens, so an unrelated database
    # must refuse here. Table names only: native owns their shape and rows.
    python3 -c 'import sqlite3, sys
ledger = sqlite3.connect("file:/state/runs.sqlite3?mode=ro", uri=True)
tables = {name for (name,) in ledger.execute("SELECT name FROM sqlite_master WHERE type = ?", ("table",))}
sys.exit(not {"v2_runs", "v2_run_events"} <= tables)' 2>/dev/null \
        || refuse 'unrecognized native ledger: /state/runs.sqlite3'
    # Native records its canonical origin; only an identical configured origin is the same binding.
    node -e 'const [file, origin] = process.argv.slice(1);
        const target = JSON.parse(require("fs").readFileSync(file)).targets?.broodling;
        process.exit(target?.origin === origin ? 0 : 1);' "$registry" "$origin" 2>/dev/null \
        || refuse 'native home is not bound to this public origin'
}
if [ "$mode" = initialize ]; then
    # Only a deliberate fresh installation. No retry may overwrite partial/used state.
    [ -z "$(ls -A /state)" ] && [ -z "$(ls -A /home/node)" ] \
        || refuse 'initialization requires empty state and home; retain or restore existing state'
    # Native's client trusts only this root, so a missing file would surface as a bare transport failure.
    [ -f /tls-root/root.crt ] || refuse 'initialization requires the public root certificate at /tls-root/root.crt'
    scratch=$(mktemp -d)
    native_pid=
    cleanup() {
        if [ -n "$native_pid" ]; then kill "$native_pid" 2>/dev/null || :; wait "$native_pid" 2>/dev/null || :; fi
        rm -rf "$scratch"
    }
    trap cleanup EXIT
    trap 'exit 1' HUP INT TERM
    # Serve on the project network so native target add discovers and records the configured
    # origin through zeroshot-tls, which forwards to this container's `zeroshot` alias.
    zeroshot target serve --listen 0.0.0.0:18770 --public-origin "$origin" --storage /state >"$scratch/server.log" 2>&1 &
    native_pid=$!
    ready=false
    for attempt in $(seq 1 100); do
        if ! kill -0 "$native_pid" 2>/dev/null; then
            cat "$scratch/server.log" >&2
            refuse 'native initialization exited'
        fi
        if SSL_CERT_FILE=/tls-root/root.crt timeout 2 zeroshot target add broodling --url "$origin" --direct 2>/dev/null; then
            ready=true
            break
        fi
        sleep 0.1
    done
    [ "$ready" = true ] || refuse 'native initialization could not reach the origin: is zeroshot-tls running with this root, and does this one-off container carry the zeroshot alias (docker compose run --use-aliases)? Clear the partial state deliberately before retrying'
    # A public read opens the native ledger without submitting work.
    SSL_CERT_FILE=/tls-root/root.crt timeout 10 zeroshot list --target broodling >/dev/null \
        || refuse 'native ledger initialization failed'
    kill "$native_pid"
    wait "$native_pid" 2>/dev/null || :
    native_pid=
    check_state
    echo 'Native target state initialized; no provider work submitted.'
else
    check_state
    exec zeroshot target serve "$@"
fi
