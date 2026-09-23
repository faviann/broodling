#!/bin/sh
# Target-package lifecycle only. Native owns creation and all run data.
set -eu
refuse() { echo "Native target state refused: $*" >&2; exit 1; }
mode=serve
if [ "${1-}" = initialize ]; then mode=initialize; shift; fi
# Keep the supported internal paths fixed; accept no native option bypass.
[ "$#" -eq 6 ] && [ "$1" = --listen ] && [ "$3" = --public-origin ] && [ "$5" = --storage ] \
    || refuse 'expected [initialize] --listen ADDRESS --public-origin ORIGIN --storage /state'
origin=$4
jq -en --arg origin "$origin" '$origin | test("^http://127[.]0[.]0[.]1:[1-9][0-9]{0,4}$") and (split(":")[-1] | tonumber <= 65535)' >/dev/null \
    || refuse 'public origin must be canonical loopback HTTP with an explicit port'
# Native normalizes the default HTTP port in its registry.
binding_origin=${origin%:80}
[ "$6" = /state ] && [ "${HOME-}" = /home/node ] && [ "${CODEX_HOME-}" = /home/node/.codex ] \
    || refuse 'configured state/home paths differ from /state and /home/node'
[ -z "${ZEROSHOT_CONFIG_DIR-}" ] && [ -z "${XDG_CONFIG_HOME-}" ] \
    || refuse 'native configuration must remain under /home/node'
[ "$(id -u)" = 0 ] || refuse 'container root is required for native process identities'
for path in /state /home/node; do
    [ -d "$path" ] && [ ! -L "$path" ] || refuse "missing or redirected directory: $path"
done
registry=/home/node/.config/zeroshot/targets.json
check_state() {
    for path in /state/runs /home/node/.config /home/node/.config/zeroshot; do
        [ -d "$path" ] && [ ! -L "$path" ] || refuse "missing or redirected directory: $path"
    done
    for path in /state/runs.sqlite3 "$registry"; do
        [ -s "$path" ] && [ -f "$path" ] && [ ! -L "$path" ] || refuse "missing or redirected initialized file: $path"
    done
    # Schema metadata only: never inspect private native run/event rows.
    schema=$(sqlite3 -readonly /state/runs.sqlite3 "SELECT sql FROM sqlite_schema WHERE type='table' ORDER BY name;" | tr -d '[:space:]')
    expected=$(tr -d '[:space:]' <<'SCHEMA'
CREATE TABLE v2_run_events (
    run_id TEXT NOT NULL, sequence INTEGER NOT NULL, event_json TEXT NOT NULL,
    PRIMARY KEY (run_id, sequence),
    FOREIGN KEY (run_id) REFERENCES v2_runs(run_id) ON DELETE CASCADE
) STRICT
CREATE TABLE v2_runs (
    run_id TEXT PRIMARY KEY NOT NULL, submission_key TEXT UNIQUE NOT NULL,
    submission_digest TEXT NOT NULL, cursor INTEGER NOT NULL, stored_json TEXT NOT NULL
) STRICT
SCHEMA
)
    [ "$schema" = "$expected" ] || refuse 'unrecognized native ledger schema'
    jq -e --arg origin "$binding_origin" '.version == 5 and
        .targets.broodling.name == "broodling" and .targets.broodling.origin == $origin and
        .targets.broodling.access.mode == "direct" and
        (.targets.broodling.id | type == "string" and test("^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$"))' \
        "$registry" >/dev/null 2>&1 || refuse 'unrecognized native home/origin binding'
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
    # Bind only container loopback, at the future public port so native target add
    # can discover and retain the real origin without publishing it to callers.
    zeroshot target serve --listen "127.0.0.1:${origin##*:}" --public-origin "$origin" --storage /state >"$scratch/server.log" 2>&1 &
    native_pid=$!
    ready=false
    for attempt in $(seq 1 100); do
        kill -0 "$native_pid" 2>/dev/null || refuse 'native initialization exited'
        if timeout 2 zeroshot target add broodling --url "$origin" --direct 2>/dev/null; then
            ready=true
            break
        fi
        sleep 0.1
    done
    [ "$ready" = true ] || refuse 'native initialization timed out'
    timeout 10 zeroshot list --target broodling >"$scratch/list.json" \
        || refuse 'native ledger initialization failed'
    jq -e '.runs == []' "$scratch/list.json" >/dev/null || refuse 'initialization found existing runs'
    kill "$native_pid"
    wait "$native_pid" 2>/dev/null || :
    native_pid=
    check_state
    echo 'Native target state initialized; no provider work submitted.'
else
    check_state
    exec zeroshot target serve "$@"
fi
