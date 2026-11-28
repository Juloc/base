#!/usr/bin/env bash
# Shows how far an app is from the base: its copy of the base against the version it was taken from and against this repository,
# and a few rules that can be checked from the files alone.
#
#   scripts/check-app.sh <app folder> [--head]
#
# The copy is compared with the newest release (tag v*), with --head with this working tree as it is.
# Exit code 0: clean (the base may still be newer; that is said, not failed). 1: files of the base were edited in the app, or a rule is broken.
set -uo pipefail
source "$(dirname "$0")/lib.sh"

app="${1:-}"; source_mode=release
[ "${2:-}" != --head ] || source_mode=head
[ -z "${2:-}" ] || [ "$2" = --head ] || fail "unknown argument: $2"
[ -n "$app" ] && [ -d "$app" ] || fail "usage: check-app.sh <app folder> [--head]"
app="$(cd "$app" && pwd)"
[ -f "$app/base/MANIFEST" ] || fail "$app has no base/MANIFEST: it was not made with new-app.sh"

problems=0
note() { printf '  %s\n' "$*"; }
bad() { printf '  %-8s %s\n' "$1" "$2"; problems=$((problems + 1)); }

app_version="$(tr -d '[:space:]' < "$app/base/VERSION")"
use_source "$source_mode"
repo_version="$(tr -d '[:space:]' < "$BASE_SRC/VERSION")"
echo "App:  $app"
echo "Base: copy is $app_version, the base is $repo_version ($BASE_FROM)"

# ---- 1. the copy against its own manifest: was anything in base/ edited in the app? ----
echo
echo "Copy of the base against the version it was taken from"
edited=0
while read -r hash path; do
  if [ ! -f "$app/base/$path" ]; then bad MISSING "base/$path"; edited=$((edited + 1))
  elif [ "$(sha256sum < "$app/base/$path" | cut -d' ' -f1)" != "$hash" ]; then bad CHANGED "base/$path"; edited=$((edited + 1)); fi
done < "$app/base/MANIFEST"
while IFS= read -r -d '' f; do
  rel="${f#"$app/base/"}"
  case "$rel" in MANIFEST|src/*/bin/*|src/*/obj/*) continue ;; esac
  grep -qF "  $rel" "$app/base/MANIFEST" || { bad ADDED "base/$rel"; edited=$((edited + 1)); }
done < <(find "$app/base" -type f -print0)
[ "$edited" -gt 0 ] || note "unchanged ($(wc -l < "$app/base/MANIFEST") files)"

# ---- 2. the copy against this repository: what would sync-base.sh bring? ----
echo
echo "Copy of the base against $BASE_FROM"
tmp="$BASE_TMP/check"; mkdir -p "$tmp"
copy_base "$tmp"
behind="$(diff <(sort -k2 "$app/base/MANIFEST") <(sort -k2 "$tmp/base/MANIFEST") | grep '^[<>]' | awk '{print $3}' | sort -u)"
if [ -z "$behind" ]; then note "up to date"
else
  note "$(wc -l <<< "$behind") file(s) differ from base $repo_version (scripts/sync-base.sh brings them):"
  sed 's/^/    base\//' <<< "$behind" | head -40
  [ "$(wc -l <<< "$behind")" -le 40 ] || note "  ..."
fi

# ---- 3. rules that the files alone can show (docs/RULES.md) ----
echo
echo "Rules"
before=$problems
compose="$app/docker-compose.yml"
if [ -f "$compose" ]; then
  ! grep -nE '\$\{|\$[A-Za-z_]|env_file' "$compose" >/dev/null || bad OPS-02 "docker-compose.yml uses variables or an env file"
  ! grep -nE '^\s+(security_opt|cap_drop|cap_add|read_only|tmpfs|healthcheck|user|entrypoint|command):' "$compose" >/dev/null || bad OPS-02 "docker-compose.yml is not minimal: user, rights, health check and start command belong in the image"
  [ "$(grep -cE '^  [a-z0-9_-]+:$' <(sed -n '/^volumes:/,$p' "$compose"))" -le 2 ] || bad OPS-01 "docker-compose.yml has more than two volumes (data and db)"
else bad OPS-01 "docker-compose.yml is missing"; fi
[ -z "$(find "$app" -name package.json -not -path '*/node_modules/*' -not -path '*/bin/*' -not -path '*/obj/*' | head -1)" ] || bad CODE-06 "there is a package.json: the browser side has no build step"
while IFS= read -r -d '' f; do
  rel="${f#"$app/"}"
  ! grep -nE '<[^>]+ style=' "$f" >/dev/null || bad SEC-02 "$rel has an inline style attribute"
  ! grep -niE '<[^>]+ on[a-z]+=' "$f" >/dev/null || bad SEC-02 "$rel has an inline event handler"
  ! grep -nE '<script(>| [^>]*[^c]>)[^<]' "$f" >/dev/null || bad SEC-02 "$rel has an inline script"
  ! grep -nE '(src|href)="(https?:)?//' "$f" >/dev/null || bad SEC-02 "$rel loads something from another server"
done < <(find "$app/src" -path '*/wwwroot/*' -name '*.html' -not -path '*/bin/*' -not -path '*/obj/*' -print0 2>/dev/null)
while IFS= read -r -d '' f; do
  rel="${f#"$app/"}"
  ! grep -nE '\.innerHTML|insertAdjacentHTML|document\.write|\beval\(' "$f" >/dev/null || bad SEC-05 "$rel builds markup from text (use h() of base.js)"
done < <(find "$app/src" -path '*/wwwroot/*' -name '*.js' -not -path '*/bin/*' -not -path '*/obj/*' -print0 2>/dev/null)
while IFS= read -r -d '' de; do
  en="$(dirname "$de")/en.js"
  keys() { grep -oE "^\s*'[^']+':" "$1" | tr -d " ':" | sort; }
  if [ ! -f "$en" ]; then bad I18N-01 "${de#"$app/"} has no en.js next to it"
  elif ! diff <(keys "$de") <(keys "$en") >/dev/null; then bad I18N-01 "${de#"$app/"} and en.js do not have the same keys"; fi
done < <(find "$app/src" -path '*/wwwroot/i18n/de.js' -not -path '*/bin/*' -not -path '*/obj/*' -print0 2>/dev/null)
while IFS= read -r -d '' prog; do
  css="$(dirname "$prog")/wwwroot/app.css"
  [ -f "$css" ] || continue
  if grep -qE -e '--accent-color:' "$css"; then      # a colour, not a hue: AccentHue is its approximate hue and is not compared (COLOR-01); AccentColor (icon, theme colour) must be the same colour
    col_code="$(grep -oE 'AccentColor = "#[0-9A-Fa-f]{6}"' "$prog" | grep -oE '#[0-9A-Fa-f]{6}' | head -1 | tr 'A-F' 'a-f' || true)"
    col_css="$(grep -oE -e '--accent-color: *#[0-9A-Fa-f]{6}' "$css" | grep -oE '#[0-9A-Fa-f]{6}' | head -1 | tr 'A-F' 'a-f' || true)"
    [ -z "$col_css" ] || [ "$col_code" = "$col_css" ] || bad CONTRACT-02 "${prog#"$app/"}: AccentColor (${col_code:-none}) is not the --accent-color of app.css ($col_css); the icon and the theme colour come from it"
    continue
  fi
  hue_code="$(grep -oE 'AccentHue = [0-9]+' "$prog" | grep -oE '[0-9]+' | head -1 || true)"
  hue_css="$(grep -oE -e '--accent-hue: [0-9]+' "$css" | grep -oE '[0-9]+' | head -1 || true)"
  [ -n "$hue_code" ] && [ "$hue_code" = "$hue_css" ] || bad CONTRACT-02 "${prog#"$app/"}: AccentHue ($hue_code) is not the --accent-hue of app.css ($hue_css)"
done < <(find "$app/src" -maxdepth 2 -name Program.cs -not -path '*/bin/*' -not -path '*/obj/*' -print0 2>/dev/null)
if [ -f "$app/CLAUDE.md" ]; then
  grep -q "base version: \*\*$app_version\*\*" "$app/CLAUDE.md" || bad CODE-07 "CLAUDE.md does not name the base version $app_version of the copy"
else bad CODE-07 "CLAUDE.md is missing"; fi
[ "$problems" -gt "$before" ] || note "nothing found by the file checks (the tests of the base cover the rest)"

echo
if [ "$problems" -eq 0 ]; then echo "Clean."; exit 0; fi
echo "$problems finding(s)."
exit 1
