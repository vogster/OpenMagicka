#!/usr/bin/env sh
# Download FNA native libraries (FNA3D, FAudio, SDL3, Theorafile) into lib/fnalibs.
# Uses the latest successful CI build of FNA-XNA/fnalibs-dailies, which tracks FNA master.
# Pass a run id to pin a specific build. Requires an authenticated GitHub CLI (`gh auth login`).
#
# Layout: x64/ x86/ D3D12/ (Windows), lib64/ libaarch64/ (Linux), osx/ (macOS universal).
set -eu

root=$(cd "$(dirname "$0")/.." && pwd)
dest="$root/lib/fnalibs"
repo=FNA-XNA/fnalibs-dailies

run=${1:-$(gh run list -R "$repo" -w CI -b main --status success --limit 1 --json databaseId --jq '.[0].databaseId')}
echo "fnalibs: $repo run $run"

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

gh run download "$run" -R "$repo" -n fnalibs -D "$tmp/fnalibs"
gh run download "$run" -R "$repo" -n fnalibs-apple -D "$tmp/apple"
mv "$tmp/apple/osx" "$tmp/fnalibs/osx"
echo "$repo run $run" > "$tmp/fnalibs/SOURCE"

rm -rf "$dest"
mkdir -p "$(dirname "$dest")"
mv "$tmp/fnalibs" "$dest"
echo "fnalibs: installed into $dest"
