#!/usr/bin/env sh
# Draw every effect in <fxb dir> with ShaderSpike, one process per effect so a native crash
# only takes down that effect. Usage: run_all.sh <ShaderSpike executable> <fxb dir>
set -u
exe=$1
dir=$2
for f in "$dir"/*.fxb; do
    name=$(basename "$f" .fxb)
    out=$("$exe" "$dir" "$name" 2>&1)
    code=$?
    line=$(printf '%s\n' "$out" | grep -E '^(DRAW|FAIL)' | head -1)
    if [ -n "$line" ]; then
        echo "$line"
    else
        reason=$(printf '%s\n' "$out" | grep -m1 -E 'Fatal|Exception|error' || true)
        echo "CRASH $name: exit $code ${reason}"
    fi
done
