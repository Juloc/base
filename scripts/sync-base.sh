#!/usr/bin/env bash
# Brings the copy of the base inside an app (its folder base/) up to the newest release of this repository (the tag v* with the
# highest version), never to what is still being built on main.
#
#   scripts/sync-base.sh <app folder> [--force] [--head]
#
# --head takes this working tree as it is instead, unreleased changes included (to try them in an app; sync again after the release).
# Refuses when files in base/ were edited in the app (they would be lost); look at them with check-app.sh, move the change
# into the base repository, then run this again. --force overwrites them. Afterwards: read CHANGELOG.md of the base, build, test,
# and update the base version named in the app's CLAUDE.md.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

app="${1:-}"; [ $# -eq 0 ] || shift
force=""; source_mode=release
for a in "$@"; do
  case "$a" in --force) force=--force ;; --head) source_mode=head ;; *) fail "unknown argument: $a" ;; esac
done
[ -n "$app" ] && [ -f "$app/base/MANIFEST" ] || fail "usage: sync-base.sh <app folder> [--force] [--head]   (the folder must contain base/MANIFEST)"
app="$(cd "$app" && pwd)"

if [ "$force" != "--force" ] && ! (cd "$app/base" && sha256sum --quiet -c MANIFEST >/dev/null 2>&1); then
  fail "files in $app/base were changed in the app. See scripts/check-app.sh; use --force to overwrite them."
fi
use_source "$source_mode"
old="$(tr -d '[:space:]' < "$app/base/VERSION")"
copy_base "$app"
new="$(tr -d '[:space:]' < "$app/base/VERSION")"
[ ! -f "$app/CLAUDE.md" ] || sed -i "s/\(base version: \*\*\)[0-9][0-9A-Za-z.+-]*\(\*\*\)/\1$new\2/" "$app/CLAUDE.md"
echo "base $old -> $new ($BASE_FROM) in $app. Read the CHANGELOG of the base, then build and test the app."
