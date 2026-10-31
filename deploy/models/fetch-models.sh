#!/bin/sh
# Downloads the models of one engine into the models volume and verifies their SHA-256.
# A verified model gets a "<target>.sha256" marker, so later starts skip both download and hashing.
# Targets ending in "/" are zip archives, unpacked into that directory (Vosk models).
# Runs in the models-init container (BusyBox) and on any Linux/WSL host:
#   deploy/models/fetch-models.sh <engine> [models-dir]
set -eu

engine="${1:?usage: fetch-models.sh <engine> [models-dir]}"
root="${2:-/models}"
manifest="$(dirname "$0")/models.txt"

entries="$(awk -v engine="$engine" '$1 == engine { print $2, $3, $4 }' "$manifest")"
if [ -z "$entries" ]; then
    echo "No models listed for engine '$engine' in $manifest" >&2
    exit 1
fi

# download <url> <sha256> <file>: fetches with resumable retries (large downloads over HTTP/2 CDNs
# are prone to reset streams) and keeps the file only if its checksum matches.
download() {
    rm -f "$3"
    curl --fail --location --http1.1 --retry 5 --retry-all-errors --continue-at - \
        --silent --show-error --output "$3" "$1"
    if [ "$(sha256sum "$3" | cut -d ' ' -f 1)" != "$2" ]; then
        rm -f "$3"
        echo "checksum mismatch for $1" >&2
        exit 1
    fi
}

while read -r sha target url; do
    path="$root/${target%/}"
    marker="$path.sha256"
    if [ -e "$path" ] && [ -f "$marker" ] && [ "$(cat "$marker")" = "$sha" ]; then
        echo "ok        $target"
        continue
    fi

    echo "download  $target"
    mkdir -p "$(dirname "$path")"
    case "$target" in
        */)
            work="$path.download"
            rm -rf "$work" && mkdir -p "$work/unpacked"
            download "$url" "$sha" "$work/archive.zip"
            unzip -q "$work/archive.zip" -d "$work/unpacked"
            # Archives usually wrap the model in one top-level folder; use its content if so.
            set -- "$work/unpacked"/*
            content="$work/unpacked"
            if [ "$#" -eq 1 ] && [ -d "$1" ]; then content="$1"; fi
            # Replace a previous model only once the new one is completely unpacked.
            rm -rf "$path.old"
            if [ -e "$path" ]; then mv "$path" "$path.old"; fi
            mv "$content" "$path"
            rm -rf "$path.old" "$work"
            ;;
        *)
            download "$url" "$sha" "$path.part"
            mv "$path.part" "$path"
            ;;
    esac
    echo "$sha" > "$marker"
    chmod -R a+rX "$path" "$marker"
done <<EOF
$entries
EOF
