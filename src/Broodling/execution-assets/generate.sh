#!/usr/bin/env bash
# Regenerate an execution asset with the native executable its manifest pins, verify its exact bytes,
# a second clean generation and native graph/runtime admission. Runs no target or provider.
# Usage: generate.sh [--manifest MANIFEST] ZEROSHOT_EXECUTABLE [OUTPUT]
#        generate.sh [--manifest MANIFEST] --fetch CACHE_DIR [OUTPUT]
# MANIFEST defaults to the approved binding, approval.json. --fetch takes the executable from the
# manifest's pinned release archive, downloading it into CACHE_DIR (after checking the release tag's
# commit and SHA256SUMS) unless already there.
set -euo pipefail

assets="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
usage='Usage: generate.sh [--manifest MANIFEST] (ZEROSHOT_EXECUTABLE | --fetch CACHE_DIR) [OUTPUT]'
manifest="$assets/approval.json"
if [[ "${1:-}" == --manifest ]]; then manifest="$(realpath -- "${2:?$usage}")"; shift 2; fi
native='' cache=''
if [[ "${1:-}" == --fetch ]]; then cache="${2:?$usage}"; shift 2; else native="$(realpath -- "${1:?$usage}")"; shift; fi
output="${1:-}"

# Manifest values: strings print raw, other JSON compactly, and a missing member prints nothing.
field() {
    python3 -c 'import json, sys
value = json.load(open(sys.argv[1]))
for key in sys.argv[2].split("."):
    value = value.get(key) if isinstance(value, dict) else None
if value is not None: print(value if isinstance(value, str) else json.dumps(value))' "$manifest" "$1"
}
case "$(field kind)" in
    broodling.execution-asset-approval/v1 | broodling.execution-asset-candidate/v1) ;;
    *) printf 'Not an execution asset manifest: %s\n' "$manifest" >&2; exit 1 ;;
esac
asset_file="$(field asset.file)" asset_sha256="$(field asset.sha256)" asset_bytes="$(field asset.bytes)"
native_version="$(field native.version)" native_sha256="$(field native.linuxX64ExecutableSha256)"

# A manifest that records the recipe's own content binds it: the recipe that produced its evidence.
while read -r file sha256; do
    [[ -z "$file" || "$(sha256sum -- "$assets/$file" | cut -d ' ' -f 1)" == "$sha256" ]] \
        || { printf 'The recipe file %s differs from the one this manifest records.\n' "$file" >&2; exit 1; }
done < <(field generation.recipeFiles | python3 -c 'import json, sys
text = sys.stdin.read()
for name, digest in (json.loads(text) if text.strip() else {}).items(): print(name, digest)')

work="$(mktemp -d -t broodling-execution-asset.XXXXXXXX)"
trap 'rm -rf -- "$work"' EXIT

if [[ -n "$cache" ]]; then
    repository="$(field native.release.repository)" tag="$(field native.release.tag)"
    archive_name="$(field native.release.archive)" archive_sha256="$(field native.release.archiveSha256)"
    [[ -n "$archive_name" ]] || { printf 'The manifest names no native release archive.\n' >&2; exit 1; }
    archive="$cache/$archive_name"
    if [[ ! -f "$archive" ]]; then
        tagged="$(curl -fsSL "https://api.github.com/repos/$repository/git/ref/tags/$tag" \
            | python3 -c 'import json, sys; ref = json.load(sys.stdin)["object"]; print(ref["type"], ref["sha"])')"
        [[ "$tagged" == "commit $(field native.sourceRevision)" ]] \
            || { printf 'Release tag %s names %s, not the pinned source revision.\n' "$tag" "$tagged" >&2; exit 1; }
        curl -fsSL -o "$work/SHA256SUMS" "https://github.com/$repository/releases/download/$tag/SHA256SUMS"
        grep -qxF "$archive_sha256  $archive_name" "$work/SHA256SUMS" \
            || { printf 'The release SHA256SUMS does not list the pinned archive checksum.\n' >&2; exit 1; }
        mkdir -p -- "$cache"
        download="$(mktemp -p "$cache" ".$archive_name.XXXXXXXX")"
        curl -fsSL -o "$download" "https://github.com/$repository/releases/download/$tag/$archive_name" \
            && [[ "$(sha256sum -- "$download" | cut -d ' ' -f 1)" == "$archive_sha256" ]] \
            || { rm -f -- "$download"; printf 'The downloaded release archive is not the pinned archive.\n' >&2; exit 1; }
        mv -- "$download" "$archive"
    fi
    [[ "$(sha256sum -- "$archive" | cut -d ' ' -f 1)" == "$archive_sha256" ]] \
        || { printf 'The native release archive %s is not the pinned archive.\n' "$archive" >&2; exit 1; }
    mkdir "$work/native"
    tar -xzf "$archive" -C "$work/native" zeroshot
    native="$work/native/zeroshot"
fi

[[ "$(sha256sum -- "$native" | cut -d ' ' -f 1)" == "$native_sha256" ]] \
    || { printf 'The pinned Linux x86-64 %s executable is required.\n' "$native_version" >&2; exit 1; }

# Each generation and the admission use their own empty HOME, configuration and state.
zeroshot() {
    local dir="$1"; shift
    (cd -- "$dir" && env -i PATH=/usr/bin:/bin HOME="$dir/home" ZEROSHOT_CONFIG_DIR="$dir/config" \
        ZEROSHOT_STATE_DIR="$dir/state" "$native" "$@")
}
fresh() { mkdir -p "$work/$1/home" "$work/$1/config" "$work/$1/state"; printf '%s\n' "$work/$1"; }
[[ "$(zeroshot "$(fresh version)" --version)" == "$native_version" ]]

# Asset identity is SHA-256 of these exact bytes: {graph, runtime} from `profile show`,
# written as Python json.dumps(indent=2, sort_keys=True) plus a final newline.
format='import json, sys
profile = json.load(sys.stdin)
sys.stdout.write(json.dumps({"graph": profile["graph"], "runtime": profile["runtime"]}, indent=2, sort_keys=True) + "\n")'
field generation.uniformRuntime >"$work/uniform-runtime.json"
for generation in first second; do
    dir="$(fresh "$generation")"
    zeroshot "$dir" profile set generated --template software-change --delivery pull_request \
        --uniform-runtime-config "$work/uniform-runtime.json" >/dev/null
    zeroshot "$dir" profile show generated | python3 -c "$format" >"$work/$generation.json"
done
cmp -- "$work/first.json" "$work/second.json"
mv -- "$work/first.json" "$work/asset.json"
[[ "$(sha256sum -- "$work/asset.json" | cut -d ' ' -f 1)" == "$asset_sha256" \
    && "$(stat -c %s -- "$work/asset.json")" == "$asset_bytes" ]] \
    || { printf 'Generated bytes differ from the manifest'\''s execution asset.\n' >&2; exit 1; }
cmp -- "$work/asset.json" "$assets/$asset_file"

# A manifest that records the asset's structure must match what describe.py reads from these bytes.
structure="$(field structure)"
if [[ -n "$structure" ]]; then
    python3 "$assets/describe.py" "$work/asset.json" | python3 -c 'import json, sys
if json.load(sys.stdin) != json.loads(sys.argv[1]): sys.exit("The asset structure differs from the manifest.")' "$structure"
fi

# Native admission of the opaque values the application submits, with an exact round trip.
python3 -c 'import json, sys
asset = json.load(open(sys.argv[1]))
for name in ("graph", "runtime"):
    json.dump(asset[name], open(sys.argv[2] + "/" + name + ".json", "w"))' "$work/asset.json" "$work"
dir="$(fresh admitted)"
zeroshot "$dir" profile set admitted --graph "$work/graph.json" --runtime-config "$work/runtime.json" >/dev/null
zeroshot "$dir" profile show admitted | python3 -c "$format" | cmp -- "$work/asset.json" -

if [[ -n "$output" ]]; then cp -- "$work/asset.json" "$output"; fi
printf 'Verified execution asset sha256:%s (%s), a second clean generation and native admission.\n' \
    "$asset_sha256" "$native_version"
