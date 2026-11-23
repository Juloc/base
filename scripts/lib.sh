#!/usr/bin/env bash
# Shared by new-app.sh, sync-base.sh and check-app.sh: what "the base inside an app" is.
#
# An app carries a COPY of the base in its folder base/ (CODE-07):
#   base/src/Base.*   the C# modules, referenced by the app's project
#   base/ui/          the look, embedded by Base.Web and served under /base/
#   base/RULES.md     the rulebook of this version
#   base/VERSION      the version of the base the copy was taken from
#   base/MANIFEST     SHA-256 of every file of the copy, so drift can be shown
# Nothing in base/ is edited in the app. A change goes into the base repository and comes back with sync-base.sh.

BASE_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BASE_MODULES="Base.Core Base.Data Base.Accounts Base.Web"

fail() { echo "error: $*" >&2; exit 1; }

# copy_base <app folder>: writes <app>/base fresh from this repository
copy_base() {
  local app="$1" dest="$1/base" m
  rm -rf "$dest"
  mkdir -p "$dest/src"
  for m in $BASE_MODULES; do
    mkdir -p "$dest/src/$m"
    (cd "$BASE_ROOT/src/$m" && find . -type f -not -path './bin/*' -not -path './obj/*' -print0 | xargs -0 -I{} cp --parents {} "$dest/src/$m/")
  done
  cp -r "$BASE_ROOT/ui" "$dest/ui"
  cp "$BASE_ROOT/VERSION" "$dest/VERSION"
  cp "$BASE_ROOT/docs/RULES.md" "$dest/RULES.md"
  cp "$BASE_ROOT/templates/app/base-README.md" "$dest/README.md"
  write_manifest "$dest"
}

# write_manifest <base folder>: one line per file, "hash  path", sorted by path
write_manifest() {
  (cd "$1" && find . -type f -not -name MANIFEST -print0 | LC_ALL=C sort -z | xargs -0 sha256sum | sed 's#  \./#  #') > "$1/MANIFEST"
}
