#!/bin/sh
# Target-package lifecycle only. Native owns creation, origin validity and all run data.
set -eu
refuse() { echo "Native target state refused: $*" >&2; exit 1; }
mode=serve
if [ "${1-}" = initialize ]; then mode=initialize; shift; fi
# Keep the supported internal paths fixed; accept no native option bypass.
[ "$#" -eq 6 ] && [ "$1" = --listen ] && [ "$3" = --public-origin ] && [ "$5" = --storage ] \
    || refuse 'expected [initialize] --listen ADDRESS --public-origin ORIGIN --storage /state'
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
    scratch=$(mktemp -d)
    native_pid=
    cleanup() {
        if [ -n "$native_pid" ]; then kill "$native_pid" 2>/dev/null || :; wait "$native_pid" 2>/dev/null || :; fi
        rm -rf "$scratch"
    }
    trap cleanup EXIT
    trap 'exit 1' HUP INT TERM
    # Bind only container loopback, at the origin's port, so native target add
    # discovers and records the configured origin without publishing it.
    zeroshot target serve --listen "127.0.0.1:${origin##*:}" --public-origin "$origin" --storage /state >"$scratch/server.log" 2>&1 &
    native_pid=$!
    ready=false
    for attempt in $(seq 1 100); do
        if ! kill -0 "$native_pid" 2>/dev/null; then
            cat "$scratch/server.log" >&2
            refuse 'native initialization exited'
        fi
        if timeout 2 zeroshot target add broodling --url "$origin" --direct 2>/dev/null; then
            ready=true
            break
        fi
        sleep 0.1
    done
    [ "$ready" = true ] || refuse 'native initialization timed out'
    # A public read opens the native ledger without submitting work.
    timeout 10 zeroshot list --target broodling >/dev/null || refuse 'native ledger initialization failed'
    kill "$native_pid"
    wait "$native_pid" 2>/dev/null || :
    native_pid=
    check_state
    echo 'Native target state initialized; no provider work submitted.'
else
    check_state
    exec zeroshot target serve "$@"
fi
