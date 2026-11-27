#!/usr/bin/env bash
# Makes a new app from the base: a copy of the base (base/) and of the start project, with its own name, namespace and colour.
#
#   scripts/new-app.sh <name> <target folder> [--accent <hue 0-359> | --color <#rrggbb>] [--title "Name people see"]
#
#   name     short technical name: lower case, digits, dash (it names the Compose project, the cookie, the repository)
#   target   a folder that does not exist yet or is empty
#   --accent the colour of the app as an angle on the colour wheel (COLOR-01), default 299
#   --color  the colour of the app as a colour (#rrggbb, since 0.4.0): written as --accent-color and AccentColor; AccentHue becomes its hue
#
# Afterwards: cd <target> && docker compose up -d --build, open http://localhost:8080, read the setup code with docker compose logs app.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

name="${1:-}"; target="${2:-}"; shift $(( $# >= 2 ? 2 : $# ))
accent=299; color=""; title=""
while [ $# -gt 0 ]; do
  case "$1" in
    --accent) accent="${2:-}"; shift 2 ;;
    --color) color="$(tr 'A-F' 'a-f' <<< "${2:-}")"; shift 2 ;;
    --title) title="${2:-}"; shift 2 ;;
    *) fail "unknown argument: $1" ;;
  esac
done

[[ "$name" =~ ^[a-z][a-z0-9-]{1,30}$ ]] || fail "usage: new-app.sh <name> <target folder> [--accent <hue>] [--title <text>]   (name: lower case, digits, dash)"
[ -n "$target" ] || fail "the target folder is missing"
[[ "$accent" =~ ^[0-9]{1,3}$ ]] && [ "$accent" -lt 360 ] || fail "--accent is an angle from 0 to 359"
if [ -n "$color" ]; then
  [[ "$color" =~ ^#[0-9a-f]{6}$ ]] || fail "--color is a colour like #00e5ff"
  # the hue of the colour (HSL, close enough for the places that still read AccentHue)
  accent="$(awk -v r=$((16#${color:1:2})) -v g=$((16#${color:3:2})) -v b=$((16#${color:5:2})) 'BEGIN {
    mx = r; if (g > mx) mx = g; if (b > mx) mx = b; mn = r; if (g < mn) mn = g; if (b < mn) mn = b; d = mx - mn
    if (d == 0) h = 0; else if (mx == r) h = 60 * ((g - b) / d); else if (mx == g) h = 60 * ((b - r) / d + 2); else h = 60 * ((r - g) / d + 4)
    if (h < 0) h += 360; printf "%d", h % 360 }')"
fi
if [ -e "$target" ] && [ -n "$(ls -A "$target" 2>/dev/null)" ]; then fail "the target folder '$target' is not empty"; fi

# notes -> Notes, my-app -> MyApp, "My App"
ns=""; words=""
IFS='-' read -ra parts <<< "$name"
for p in "${parts[@]}"; do
  [ -n "$p" ] || fail "the name must not contain two dashes in a row or end with one"
  cap="$(tr '[:lower:]' '[:upper:]' <<< "${p:0:1}")${p:1}"
  ns+="$cap"; words+="${words:+ }$cap"
done
[ -n "$title" ] || title="$words"
[[ "$title" != *[\"\\\&\|\<\>]* ]] || fail "--title must not contain quotes, backslash, &, | or angle brackets"
[ "$ns" != "Base" ] || fail "the name 'base' is taken"
version="$(tr -d '[:space:]' < "$BASE_ROOT/VERSION")"

mkdir -p "$target"
target="$(cd "$target" && pwd)"
start="$BASE_ROOT/samples/StartApp"
app="$target/src/$ns"

# 1. the base
copy_base "$target"

# 2. the start project under its new name
mkdir -p "$app"
(cd "$start" && find . -type f -not -path './bin/*' -not -path './obj/*' -not -path './data/*' -not -name Dockerfile -not -name docker-compose.yml -print0 | xargs -0 -I{} cp --parents {} "$app/")
mv "$app/StartApp.csproj" "$app/$ns.csproj"
rename_in() { sed -i -e "s/StartApp/$ns/g" -e "s/startapp/$name/g" -e "s|Start App|$title|g" "$@"; }
grep -rlZ -e 'StartApp' -e 'startapp' -e 'Start App' "$app" | xargs -0 -r sed -i -e "s/StartApp/$ns/g" -e "s/startapp/$name/g" -e "s|Start App|$title|g"
sed -i 's|\.\.\\\.\.\\src\\Base\.Web\\|..\\..\\base\\src\\Base.Web\\|' "$app/$ns.csproj"
sed -i "s/--accent-hue: [0-9]*;/--accent-hue: $accent;/" "$app/wwwroot/app.css"
sed -i "s/AccentHue = [0-9]*,/AccentHue = $accent,/" "$app/Program.cs"
if [ -n "$color" ]; then
  sed -i "s/--accent-hue: [0-9]*;/--accent-color: $color;/" "$app/wwwroot/app.css"
  sed -i "s|^\(  *\)AccentHue = $accent,.*|\1AccentColor = \"$color\",                        // the same colour as --accent-color in wwwroot/app.css\n\1AccentHue = $accent,                              // its hue, for what still reads a hue|" "$app/Program.cs"
fi

# 3. the files at the root
sed -e 's|^#   docker build -f .*|#   docker build .|' -e "s|samples/StartApp|src/$ns|g" -e 's|^ARG BASE_DIR=\.$|ARG BASE_DIR=base|' -e "s/StartApp/$ns/g" "$start/Dockerfile" > "$target/Dockerfile"
sed -e '/^    build:$/,/^      dockerfile: .*$/c\    build: .' "$start/docker-compose.yml" > "$target/docker-compose.yml"
rename_in "$target/docker-compose.yml"
cp "$BASE_ROOT/global.json" "$BASE_ROOT/.gitignore" "$BASE_ROOT/.dockerignore" "$target/"
grep -v -e 'xunit' -e 'Mvc.Testing' "$BASE_ROOT/Directory.Packages.props" > "$target/Directory.Packages.props"
render() { sed -e "s|{{NAME}}|$name|g" -e "s|{{NS}}|$ns|g" -e "s|{{TITLE}}|$title|g" -e "s|{{BASE_VERSION}}|$version|g" -e "s|{{ACCENT}}|$accent|g" "$1" > "$2"; }
render "$BASE_ROOT/templates/CLAUDE.app.md" "$target/CLAUDE.md"
render "$BASE_ROOT/templates/app/AGENTS.md" "$target/AGENTS.md"
render "$BASE_ROOT/templates/app/README.md" "$target/README.md"
render "$BASE_ROOT/templates/app/CHANGELOG.md" "$target/CHANGELOG.md"
render "$BASE_ROOT/templates/app/Directory.Build.props" "$target/Directory.Build.props"
render "$BASE_ROOT/templates/app/app.slnx" "$target/$ns.slnx"
mkdir -p "$target/.github/workflows"
render "$BASE_ROOT/templates/app/build.yml" "$target/.github/workflows/build.yml"

echo "Made $title ($name) in $target from base $version."
echo "  cd $target && docker compose up -d --build"
echo "  docker compose logs app        (the setup code for the first account)"
echo "  open http://localhost:8080"
echo "Then write what the app is for in Description (src/$ns/Program.cs): /api/about, the sign-in card and the manifest show it."
