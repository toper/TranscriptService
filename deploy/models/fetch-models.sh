#!/bin/sh
# Downloads the models of one engine into the models volume and verifies their SHA-256.
# Files that are already present and intact are kept, so restarts cost one checksum, not a download.
# Usage: fetch-models.sh <engine> [models-dir]
set -eu

engine="${1:?usage: fetch-models.sh <engine> [models-dir]}"
root="${2:-/models}"
manifest="$(dirname "$0")/models.txt"

entries="$(awk -v engine="$engine" '$1 == engine { print $2, $3, $4 }' "$manifest")"
if [ -z "$entries" ]; then
    echo "No models listed for engine '$engine' in $manifest" >&2
    exit 1
fi

verify() { echo "$1  $2" | sha256sum -c -s -; }

echo "$entries" | while read -r sha target url; do
    file="$root/$target"
    case "$target" in
        */) download="$root/.download/$(basename "$target").zip"; marker="$file.sha256" ;;
        *)  download="$file.part"; marker="" ;;
    esac

    if [ -n "$marker" ] && [ -f "$marker" ] && [ "$(cat "$marker")" = "$sha" ]; then
        echo "ok        $target"; continue
    fi
    if [ -z "$marker" ] && [ -f "$file" ] && verify "$sha" "$file"; then
        echo "ok        $target"; continue
    fi

    echo "download  $target"
    mkdir -p "$(dirname "$download")" "$(dirname "$file")"
    # HTTP/1.1 and resumable retries: large downloads over HTTP/2 CDNs are prone to reset streams.
    curl --fail --location --http1.1 --retry 5 --retry-all-errors --continue-at -         --silent --show-error --output "$download" "$url"
    if ! verify "$sha" "$download"; then
        echo "checksum mismatch for $url" >&2
        rm -f "$download"
        exit 1
    fi

    if [ -n "$marker" ]; then
        # Archives (Vosk models) are unpacked into the target directory.
        rm -rf "$file" && mkdir -p "$file"
        unzip -q "$download" -d "$file.unpack"
        mv "$file.unpack"/*/* "$file" && rm -rf "$file.unpack" "$download"
        echo "$sha" > "$marker"
    else
        mv "$download" "$file"
    fi
    chmod -R a+rX "$root"
done
