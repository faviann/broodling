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
# Broodling's record of the public origin this native state serves, written once by initialization.
binding=/home/node/.config/broodling/origin
check_state() {
    [ -d /state/runs ] && unredirected /state/runs || refuse 'missing or redirected directory: /state/runs'
    for path in /state/runs.sqlite3 "$binding"; do
        [ -f "$path" ] && [ -s "$path" ] && unredirected "$path" || refuse "missing or redirected initialized file: $path"
    done
    # Native creates its tables in any SQLite file it opens, so an unrelated database
    # must refuse here. Table names only: native owns their shape and rows.
    python3 -c 'import sqlite3, sys
ledger = sqlite3.connect("file:/state/runs.sqlite3?mode=ro", uri=True)
tables = {name for (name,) in ledger.execute("SELECT name FROM sqlite_master WHERE type = ?", ("table",))}
sys.exit(not {"v2_runs", "v2_run_events"} <= tables)' 2>/dev/null \
        || refuse 'unrecognized native ledger: /state/runs.sqlite3'
    # Initialization recorded native's canonical origin; only an identical configured origin is the same binding.
    [ "$(cat "$binding")" = "$origin" ] || refuse 'native state is not bound to this public origin'
}
# Native serves only in its private mode: every control and OECP request needs the bearer token that
# the operator's bootstrap installs in each new target process. The bootstrap key is a root-only secret
# file; native requires its own private copy, which it reads and unlinks before serving anything.
key_secret=/run/secrets/zeroshot-bootstrap-key
key_copy=/run/broodling-target/bootstrap-key
private_key() {
    [ -f "$key_secret" ] && [ ! -L "$key_secret" ] \
        || refuse "missing bootstrap key: mount the target's root-only secret file at $key_secret"
    # Execution agents run as other identities, so the key must stay readable by root alone.
    [ "$(stat -c %u "$key_secret")" = 0 ] && [ $((0$(stat -c %a "$key_secret") & 077)) -eq 0 ] \
        || refuse "the bootstrap key at $key_secret must be owned by root with no group or other access (mode 0400)"
    [ "$(wc -c <"$key_secret")" -eq 64 ] && grep -qxE '[0-9a-f]{64}' "$key_secret" \
        || refuse 'the bootstrap key must be exactly 64 lowercase hexadecimal characters with no newline'
    rm -rf "${key_copy%/*}"
    (umask 077 && mkdir "${key_copy%/*}" && cat "$key_secret" >"$key_copy") || refuse "cannot prepare native's private copy of the bootstrap key"
}
if [ "$mode" = initialize ]; then
    # Only a deliberate fresh installation. No retry may overwrite partial/used state.
    [ -z "$(ls -A /state)" ] && [ -z "$(ls -A /home/node)" ] \
        || refuse 'initialization requires empty state and home; retain or restore existing state'
    scratch=$(mktemp -d)
    native_pid=
    # A stop sent before native installs its signal handlers can be lost, so a bounded wait ends in a kill.
    stop_native() {
        kill "$native_pid" 2>/dev/null || :
        for attempt in $(seq 1 50); do
            kill -0 "$native_pid" 2>/dev/null || break
            sleep 0.1
        done
        kill -KILL "$native_pid" 2>/dev/null || :
        wait "$native_pid" 2>/dev/null || :
        native_pid=
    }
    cleanup() {
        if [ -n "$native_pid" ]; then stop_native; fi
        rm -rf "$scratch"
    }
    trap cleanup EXIT
    trap 'exit 1' HUP INT TERM
    # Native validates the origin and creates its ledger when it starts serving. It serves privately on this
    # one-off container's loopback, with a throwaway bootstrap key that is never used, so nothing can control
    # it; it is stopped once it reports the canonical origin it serves.
    serve_once() {
        (umask 077 && openssl rand -hex 32 | tr -d '\n' >"$scratch/bootstrap-key")
        : >"$scratch/server.log"
        zeroshot target serve --listen 127.0.0.1:18770 --public-origin "$origin" --storage "$1" \
            --bootstrap-key-file "$scratch/bootstrap-key" >"$scratch/server.log" 2>&1 &
        native_pid=$!
        canonical=
        for attempt in $(seq 1 100); do
            canonical=$(sed -n 's/^Zeroshot direct target listening on 127\.0\.0\.1:18770 as //p' "$scratch/server.log" || :)
            [ -n "$canonical" ] && break
            if ! kill -0 "$native_pid" 2>/dev/null; then
                cat "$scratch/server.log" >&2
                refuse 'native initialization exited; clear any partial state deliberately before retrying'
            fi
            sleep 0.1
        done
        [ -n "$canonical" ] || refuse 'native initialization did not start serving; clear any partial state deliberately before retrying'
        stop_native
    }
    # Pinned native, not the entrypoint, decides the canonical spelling it serves; learn it on scratch storage,
    # so a refused origin leaves the configured state empty.
    mkdir "$scratch/state"
    serve_once "$scratch/state"
    [ "$canonical" = "$origin" ] || refuse "native serves this origin as $canonical; configure exactly that origin"
    serve_once /state
    mkdir -p "${binding%/*}"
    printf '%s\n' "$origin" >"$binding"
    check_state
    echo 'Native target state initialized; no provider work submitted. Start the target and bootstrap its control token.'
else
    check_state
    private_key
    exec zeroshot target serve "$@" --bootstrap-key-file "$key_copy"
fi
