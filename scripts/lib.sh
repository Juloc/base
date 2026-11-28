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

# Where the copied files come from (BASE_SRC). By default the newest release, the tag v* with the highest version, unpacked from git:
# an app never gets what is still being built on main. use_source head takes this working tree as it is (to try out what is not released).
BASE_TMP="$(mktemp -d)"; trap 'rm -rf "$BASE_TMP"' EXIT
BASE_SRC="$BASE_ROOT"; BASE_FROM="this working tree"
use_source() {
  [ "${1:-release}" != head ] || return 0
  local tag; tag="$(git -C "$BASE_ROOT" tag -l 'v[0-9]*' --sort=-v:refname | head -1)"
  [ -n "$tag" ] || fail "no release tag (v*) in $BASE_ROOT; --head takes the working tree instead"
  mkdir -p "$BASE_TMP/release"
  git -C "$BASE_ROOT" archive "$tag" | tar -x -C "$BASE_TMP/release"
  BASE_SRC="$BASE_TMP/release"; BASE_FROM="release $tag"
}

# copy_base <app folder>: writes <app>/base fresh from BASE_SRC
copy_base() {
  local app="$1" dest="$1/base" m
  rm -rf "$dest"
  mkdir -p "$dest/src"
  for m in $BASE_MODULES; do
    mkdir -p "$dest/src/$m"
    (cd "$BASE_SRC/src/$m" && find . -type f -not -path './bin/*' -not -path './obj/*' -print0 | xargs -0 -I{} cp --parents {} "$dest/src/$m/")
  done
  cp -r "$BASE_SRC/ui" "$dest/ui"
  cp "$BASE_SRC/VERSION" "$dest/VERSION"
  cp "$BASE_SRC/docs/RULES.md" "$dest/RULES.md"
  cp "$BASE_SRC/templates/app/base-README.md" "$dest/README.md"
  write_manifest "$dest"
}

# write_manifest <base folder>: one line per file, "hash  path", sorted by path
write_manifest() {
  (cd "$1" && find . -type f -not -name MANIFEST -print0 | LC_ALL=C sort -z | xargs -0 sha256sum | sed 's#  \./#  #') > "$1/MANIFEST"
}
