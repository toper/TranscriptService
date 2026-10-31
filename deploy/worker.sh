#!/bin/sh
# Entry point of every engine worker image: runs the published worker assembly, the one that has a
# runtimeconfig (libraries such as Worker.Core do not), passing arguments through (--health-probe).
set -eu
for config in /app/*.runtimeconfig.json; do
    exec dotnet "${config%.runtimeconfig.json}.dll" "$@"
done
