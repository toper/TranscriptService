#!/bin/sh
# Downloads the models of one engine into the models volume and verifies their SHA-256.
# Models that are already present and intact are kept, so a restart costs a checksum, not a download.
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

sha_of() { sha256sum "$1" | cut -d ' ' -f 1; }

download() { # url file
    rm -f "$2"   # a stale partial file from an interrupted run cannot be resumed reliably
    # HTTP/1.1 and resumable retries: large downloads over HTTP/2 CDNs are prone to reset streams.
    curl --fail --location --http1.1 --retry 5 --retry-all-errors --continue-at - \
        --silent --show-error --output "$2" "$1"
}

echo "$entries" | while read -r sha target url; do
    case "$target" in
        */) # Archive (Vosk): unpacked into a directory, recorded by a checksum marker beside it.
            dir="$root/${target%/}"
            marker="$dir.sha256"
            if [ -d "$dir" ] && [ -f "$marker" ] && [ "$(cat "$marker")" = "$sha" ]; then
                echo "ok        $target"; continue
            fi
            echo "download  $target"
            work="$root/.download/$(basename "$dir")"
            rm -rf "$work" && mkdir -p "$work/unpacked"
            download "$url" "$work/archive.zip"
            [ "$(sha_of "$work/archive.zip")" = "$sha" ] || { echo "checksum mismatch for $url" >&2; exit 1; }
            unzip -q "$work/archive.zip" -d "$work/unpacked"
            # Archives usually wrap everything in one top-level folder; use its content if so.
            set -- "$work/unpacked"/*
            content="$work/unpacked"
            if [ "$#" -eq 1 ] && [ -d "$1" ]; then content="$1"; fi
            # Replace the previous model only once the new one is fully unpacked.
            rm -rf "$dir.old" && { [ ! -d "$dir" ] || mv "$dir" "$dir.old"; }
            mv "$content" "$dir" && rm -rf "$dir.old" "$work"
            echo "$sha" > "$marker"
            ;;
        *)
            file="$root/$target"
            if [ -f "$file" ] && [ "$(sha_of "$file")" = "$sha" ]; then
                echo "ok        $target"; continue
            fi
            echo "download  $target"
            mkdir -p "$(dirname "$file")"
            download "$url" "$file.part"
            [ "$(sha_of "$file.part")" = "$sha" ] || { rm -f "$file.part"; echo "checksum mismatch for $url" >&2; exit 1; }
            mv "$file.part" "$file"
            ;;
    esac
done

chmod -R a+rX "$root"
