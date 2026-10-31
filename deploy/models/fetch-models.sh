#!/bin/sh
# Downloads the models of one engine into the models volume and verifies their SHA-256.
# A verified model gets a "<file>.sha256" marker, so later starts skip both download and hashing.
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

while read -r sha target url; do
    file="$root/$target"
    marker="$file.sha256"
    if [ -f "$file" ] && [ -f "$marker" ] && [ "$(cat "$marker")" = "$sha" ]; then
        echo "ok        $target"
        continue
    fi

    echo "download  $target"
    mkdir -p "$(dirname "$file")"
    rm -f "$file.part"
    # HTTP/1.1, and retries that resume where the previous attempt stopped: large downloads
    # over HTTP/2 CDNs are prone to reset streams.
    curl --fail --location --http1.1 --retry 5 --retry-all-errors --continue-at - \
        --silent --show-error --output "$file.part" "$url"
    if [ "$(sha256sum "$file.part" | cut -d ' ' -f 1)" != "$sha" ]; then
        rm -f "$file.part"
        echo "checksum mismatch for $url" >&2
        exit 1
    fi
    mv "$file.part" "$file"
    echo "$sha" > "$marker"
    chmod a+r "$file" "$marker"
done <<EOF
$entries
EOF
