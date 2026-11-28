# Changelog

The base follows SemVer; the version stands in `VERSION` (OPS-05). An app carries the version of its copy in `base/VERSION`;
`scripts/check-app.sh` shows how far a copy is behind, `scripts/sync-base.sh` brings it up to date. Before 1.0.0 a minor version may change
what apps rely on; such changes are marked **Breaking**.

## 0.4.5 (2026-11-28)

- **Scripts: an app gets the newest release, not the working tree** (found by finance; decision 90). `sync-base.sh`, `new-app.sh` and `check-app.sh` take the base from the tag `v*` with the highest version, unpacked from git; until now they copied the working tree, unreleased changes included. `--head` takes the working tree on purpose (to try something in an app before the release). `check-app.sh` names what it compared with.
- **Fix: an app's own icon and worker win** (found by caddy; wrong since 0.4.0). A file in the app's `wwwroot` with the name of one the base makes (`/icon.svg`, `/icon-maskable.svg`, the PNG icons, `/manifest.webmanifest`, `/sw.js`) is now served instead of the made one; until now the base's endpoint always answered first. What the app does not bring is still made by the base. An app with only its own `icon.svg` gets it in the tab and the menu; for the installed app (PNGs, apple-touch) it brings the PNGs too, because the base draws those from `Icon` and cannot read an SVG.
- **New: icons on dialog actions and a split button** (decision 96, caddy's wish). An action of `openDialog()` takes `icon` (also `confirmDialog({ icon })`); `enter: true` runs it on Enter in a field. `{ label, kind, run, more: [{ label, icon, danger, run }] }` makes a split button: the main action and an arrow with a menu of alternatives, which run like the dialog's actions. Outside a dialog: `splitButton({ label, icon, kind, run, more })` from `base.js`. On a phone a start-side action with an icon shows only its icon. **Look:** a dialog button's label is now inside a `<span>` (an app that styled the button's text node directly: none known).
- **Look: the tree in `table()` shows children as a block** (decision 91, caddy's wish, the owner's choice). The hairlines (`.t-ln`) are gone; the children of a row lie in a soft, rounded block in the app's accent tint that begins at the children, and a deeper level is a smaller block inside it. The same on a phone, with a smaller step per level (14 px). `table()` marks child rows with `data-level` and `t-top` / `t-bot` / `t-end1` for the corners; markup and API are otherwise unchanged. One level is 28 px instead of 22; the head of a group starts at 20 px like the rows. An app that styled `.t-ln` itself should drop that rule. **Look:** a switched-off row greys its actions too (until now only its text), and without the column of check boxes the first column keeps its 20 px from the edge.
- **New: `table()` columns by the table's own width** (decision 92, finance's wish). The layout follows the width of the table's panel (a container query), not the window: below 640 px of that width the rows have two lines, and a column with the new `from: 'm' | 'l' | 'xl'` shows only from 800, 1000 or 1200 px. Existing tables keep their API. **Look:** a table in a room narrower than 640 px now has two-line rows even on a wide screen (a narrow column, a dialog; and the main column of a window of 900 to about 930 px with the side menu); a wrapper of the panel with `display: inline-block` or `width: fit-content` gives it no width (it is a size container) and should be dropped.
- **New: `avatar({ name, image, icon, cat, size, shape, label })`** (decision 93, finance's wish): the mark of who or what a row is about. A picture if it loads, else a symbol of `icons.js` on the soft colour of its category (`cat: 0` … `9`), else the initial (on the category's colour when given, else the soft accent). Sizes `'s'` (32 px, the existing `.avatar`), `'row'` (40 px, default), `'head'` (64 px); round by default, `shape: 'square'` a rounded square. The classes `marks-round` and `marks-square` on a list give every mark inside it (`.avatar`, `.cat-mark`, the tick) one shape.
- **New: choosing only after "Select"** (decision 94, finance's wish). `table({ selection: { mode: 'button', button?, actions } })` shows plain rows until "Auswählen" (or a long press, or `choose(true)`) turns choosing on, at every width; Esc, "Fertig", the × of the selection bar or unticking the last row turn it off. `button: false` leaves the button out for an app that puts its own; the table returns `choose(on)` and `choosing()`. Without `mode` nothing changes. `table({ shape: 'round' | 'square' })` gives every mark in it one shape; a round tick is the base's check box as a circle.
- **New: `dayList({ label, load, day, row, dayValue, dayLabel, rowKey, onRowClick, selection, shape, empty, limit, top, onTopDay })`** (decision 95, finance's wish): the endless list by day, a part beside `timeline()`. A white card per day; its band (the date, "Heute", "Gestern", "Morgen" or the weekday; at the right the value of `dayValue`, e.g. the day's sum) sticks flush under the shell's top bar, turns to glass and is pushed away by the next day. It loads older days below and newer ones above as one scrolls, through `load({ dir: 'older' | 'newer', cursor, limit })` answering `{ items, next }` like a `Page` of the server, and keeps the place when rows come in above. Rows are `{ lead, title, sub, value, quiet }` (an `avatar()` as lead; `quiet` for expected rows). With `selection`, "Auswählen" turns choosing on as in `table()`; the round tick takes the place of the avatar. Returns `{ el, reload, update, remove, flash, scrollToDay, selected, clearSelection, choose, choosing, destroy }`. Also `dayKey(date)` ('YYYY-MM-DD' in local time) and three texts (`day.today`, `day.yesterday`, `day.tomorrow`).
- **Start app:** the list chooses several only after "Auswählen" (tick off or delete several at once), its "Geändert" column shows only where the table is 800 px wide, and a new page "Verlauf" shows the entries by the day they were made as an endless `dayList()` with an `avatar()` per row. A new app gets them from `new-app.sh`; an existing app is not changed.

## 0.4.4 (2026-11-28)

Three parts the finance app asked for (`table()` and `chart()` use the first two themselves), and the window of the hub.

- **New: an app may open inside a window of the hub** (contract `about.embed.hub`, SEC-01, SEC-02 and CONTRACT-02 text changed, decision 85). `BaseAppOptions.Embeddable = true` lets the connected hub frame the app: while a hub is connected and switched on, `frame-ancestors` names its origin (and `X-Frame-Options` is left out) and `/api/about` says `"embed": { "hub": true }`; otherwise nothing changes (`frame-ancestors 'none'`, `DENY`, `embed.hub` false). The session cookie stays `SameSite=Strict`, so it works when hub and app are the same site (https, one domain). `/api/about` always has the new field `embed`.
- **Look: an own check box** (decision 86). `.check input` and `.t-check` (the table's selection) are no longer the browser's box: a 22 px box drawn by the base on a real `<input type="checkbox">`, a 44 px target (MOBILE-01), filled with `--accent-ink` and a tick when on, a dash when `indeterminate` (the head of a table when some rows are chosen), the focus ring of the other controls, greyed when disabled, system colours in forced colours. The markup stays the same; an app that styled `.check input` itself (size, `accent-color`) should drop that rule. **Breaking, look:** the table's check box grows from 18 to 22 px.
- **New: category colours** (decision 87). Ten tokens `--cat-0` … `--cat-9` (blue, amber, green, pink, teal, orange, purple, lime, coral, caramel; bright on the owner's word; light and dark), `class="cat-3"` names one for what is inside, `.cat-dot` (a dot before a name), `.cat-soft` (the soft surface and its text, on a chip, a badge or the icon square `.cat-mark`). **`chart()`** gives a series without `color` the category colours in order; a series with a colour is unchanged. A list never mixes round and square marks.
- **Rule: parts of the look are built in the base from the start** (CODE-07 and the working rules changed, decision 88, the owner's rule). An app asks the base for a missing component instead of building it; server parts still wait for a second app. Shapes of marks are chosen per app and component, never mixed in one place.
- **Look: the mark in the menu is the app's icon** (`/icon.svg`, as an image): the app's own file when it has one, else the one the base draws from `Icon` and the accent. Tab, home screen and menu now look the same.
- **Look: faults are a clear red** (decision 89, COLOR-01 text changed). The raspberry (hue 5) is now a red (hue 27) in every app; an app may turn it with one new variable, `--danger-hue` (caddy: `--danger-hue: 5;` in `app.css` keeps its raspberry).
- **New: 35 icons** for everyday money: `cart fork coffee flame drop wifi car fuel plug parking bus plane bag shirt pill heart paw child school ticket bank receipt gift piggy coins debt briefcase arrows refund tag wallet card safe chart house-loan`. Same grid and stroke as the rest; only the shapes the PNG renderer of the app icon reads (decision 77).

## 0.4.3 (2026-11-28)

- **Breaking, Compose: the file is minimal** (OPS-01 and OPS-02 changed, decision 84, the owner's rule). Two services (`app`, `db`), two named volumes (`data` for the key file, `db` for the database), per service only `build`/`image`, `restart`, `ports`, `environment` (with `TZ: Europe/Berlin`), `volumes`, `depends_on`. No init container, no password file, no `security_opt`, `cap_drop`, `tmpfs` or `healthcheck` in the file; the database password is a fixed value (`app`), because the database has no port. The health check moved into the `Dockerfile` (`HEALTHCHECK ... health`); user and data folder were there already. `scripts/check-app.sh` warns about hardening and health check blocks in an app's Compose file and allows two volumes.
  - **A new app** gets the new file from `new-app.sh`.
  - **An existing installation** may keep its old Compose file: `BASE_DB_PASSWORD_FILE` still works. To move to the new file, dump the database in the old stack (`docker compose exec db pg_dump -U app app > app.sql`), copy the key file out of the old volume (sub-folder `app/secret.key`), start the new stack, put the key file into the `data` volume (owner: the app's user), restore the dump (`docker compose exec -T db psql -U app app < app.sql`), and restart the app. Without the key file, the stored secrets (authenticator, hub) cannot be read.
  - An app adds the `HEALTHCHECK` line to its own `Dockerfile`.

## 0.4.2 (2026-11-28)

- **Look: pages use the whole width on a wide screen** (decision 83, the owner's word). `.page` and `.page-head` have no limit any more and the main area keeps only a margin (no column of 920 px in the middle); tables and grids grow with the screen. `.page.narrow` (640 px) stays for forms and a detail with text. The token `--content-w` is gone: an app that used it for its own limit gets no limit (it should drop the rule, or keep one of its own on purpose).
- **Fix, look: a colour without chroma gives surfaces without a tint.** The page, surfaces, text and lines took a fixed chroma in the accent's hue, so a grey or black accent (`--accent-color: #272727`, also `light-dark(#272727, #e8e8e8)`) made pink surfaces (the hue of grey is about 24). They now take at most the accent's own chroma (`min(c, ...)`, as the soft tokens did already); a coloured accent looks exactly as before.
- **New: `.page.columns`**, cards side by side on a wide screen (columns of at least 440 px, at most two; a card is never split; one column on a phone). The start app's settings page uses it instead of `.page.narrow.stack.l`; an app copies that one class.

## 0.4.1 (2026-11-27)

Fixes found after 0.4.0. Nothing an app must change.

- **Fix, behaviour: only one dialog at a time** (DIALOG-01 text changed, decision 81). The page behind a dialog stays usable, so the button that opened it could be pressed again and opened one dialog more each time. Now a dialog opened from the page closes the open one, the same button again only brings the open one forward (what was typed stays), and a dialog opened from inside a dialog (the question before deleting) stands above it. Code that opened two dialogs from the page on purpose gets only the second.
- **Rule, DATA-03 and SEC-03: shared rows** (decision 82). Another person reaches a person's row only through an explicit share (which row or list, which person, read or read and change, which an app may narrow; deleting and sharing stay with the owner); a row made on another person's row belongs to its maker and is hidden from the other owner (a reservation), unless the app's rules make it part of the shared row (a booking on a shared account); an app that shares tests both directions. No code in the base yet: finance builds the share first, the base takes it over with the second app (wishlist).
- **Contract fix: `time` in `/api/health` is UTC to the whole second with `Z`** (`2026-11-23T08:15:00Z`), as `docs/CONTRACT.md` says; before it had seven decimals and `+00:00`. CONTRACT-01 text says so. A reader that parsed the old form still parses this.
- **New: `replace(el, ...kids)`** in `base.js`: swaps the children with the rules of `h()` (null, undefined and false left out, arrays flattened). `el.replaceChildren(a, null)` writes "null" as text; use `replace` where a child may be missing.
- **German: "du" everywhere.** Ten texts of the invitation, the people, the hub and their errors said "Sie"; they say "du" now like the rest.
- **Look, phone menu:** a place's name in the pill is no longer cut at 64 px ("Einstellun…"); a place is as wide as its name (68 to 120 px). **Settings card:** the rows of "Konto und Sicherheit" wrap their text instead of cutting it, and on a narrow phone the button moves to a line of its own (`.row.wrap`, new and usable by apps).
- **Fix, look: a toast no longer covers the action bar.** "Eintrag angelegt" stood exactly over "Neuer Eintrag" (same place at the bottom); while an action or selection bar is shown, toasts stand above it.
- **Start app, Compose:** the init step gets a `tmpfs` at `/var/lib/postgresql`, so it no longer leaves an empty anonymous volume behind at every start (the `postgres` image declares one). An existing app copies the three lines into its `docker-compose.yml`.
- **`scripts/new-app.sh --color '#rrggbb'`:** an app can be made with a colour instead of a hue (writes `--accent-color` and `AccentColor`, and its hue as `AccentHue`). It now reminds to write the app's `Description`.
- **Start app: the list shows the parts of 0.4** (`samples/StartApp/wwwroot`), so new apps copy them: the list is a `table()` (title as a link, state, changed; "Abhaken" as the row's main action, "Wieder öffnen" a quiet one; the whole row opens the entry), the filter is `chips()` ("Alle", "Nur offene", the same setting as before), and "Neuer Eintrag" sits in the `actionBar()` at the bottom, as in caddy: one bar per page, made again when the page is drawn, closed when the page changes. Existing apps are not touched; an app that wants the pattern copies it from `app.js`.

## 0.4.0 (2026-11-27)

Design 0.4 complete (`docs/DESIGN-0.4.md`, steps 1 to 7). An app taking 0.4.0 from 0.3.x reads the alpha entries below for what is **Breaking**.

- **Step 7, memory defaults:** the start app's `Dockerfile` (and so every app made by `scripts/new-app.sh`) sets the workstation collector without a background thread, conserve memory, a 192 MB heap ceiling, a small first generation and no dynamic PGO (`DOTNET_gcServer=0 DOTNET_gcConcurrent=0 DOTNET_GCConserveMemory=7 DOTNET_GCHeapHardLimit=0xC000000 DOTNET_GCgen0size=0x400000 DOTNET_TieredPGO=0`). Measured on caddy. An existing app copies the line into its own `Dockerfile`.
- **Fix:** the quiet text button of the sign-in card (`.quiet`) no longer styles a `.btn.quiet` row button: the row buttons of a table or card head had negative margins and overlapped.
- **Fix:** `chart()` scales in four round steps and, for counts, in whole numbers (no axis reads "2, 1, 1" any more).
- **Fix:** a number box's label wraps in a narrow box instead of being cut.
- **New: `IdleTrim`** (registered by `AddBase`, nothing to do for an app): once a minute it looks at how much memory was asked for; after a burst of work (8 MB or more) followed by a quiet minute it runs one compacting collection and returns free pages to the system. An idle app no longer keeps the garbage of its last work.
- **Fix:** after a password check (Argon2, 64 MiB) the memory is given back at once when no other check runs; before, an idle app kept it until its next burst of work (measured on caddy: +63 MB after one sign-in).

## 0.4.0-alpha.4 (towards 0.4.0, in progress: step 6 of `docs/DESIGN-0.4.md`)

New file `ui/gate.js` (import from `/base/gate.js`): the card of the door. New in `base.js`: `pwa()`. New in the C# base: `Pwa` (manifest, icons, service worker).

- **Breaking, look and markup: the sign-in, the first setup and the invitation are one white card** (radius 28, soft shadow, 16 px margin on a phone): app mark, title, one line (the app's description from `/api/about`, or `start({ line })`), the hub button (`.btn.neutral`) above "oder mit eigenem Konto", fields with placeholders and no hint lines, the soft main button, language and light or dark as quiet text buttons at the bottom. The code field opens the number keyboard (`inputmode="numeric"`, `autocomplete="one-time-code"`); a quiet button switches to a recovery code (letters). The setup has a three-step marker. Old classes `.auth-card`/`.auth-head`/`.steps` changed; an app with its own CSS for them should drop it. `start({ icon, line, onSignedIn })` takes the new optional `line`.
- **New: `authCard({ icon, title, line, target?, step?, body, foot })`, `stateCard({ icon, tone: 'run' | 'ok' | 'danger', title, text, actions })`, `mountAuth(card)`, `steps(n, of)`, `authFoot()`** for apps with pages of their own around a sign-in (caddy's gate pages): the same card, the soft target box "danach geht es direkt dorthin", a state card (the mark turns, "not allowed", "link expired"; one sentence, one or two buttons). Catalogue entries "Sign-in card" and "State card".
- **New: `field({ labelHidden: true })`:** the label stays for a screen reader, the eyes get the placeholder (FORM-01 text changed, decision 79).
- **New: the PWA base.** The base serves `/manifest.webmanifest` (name, short name, description, start path, theme and splash colour from the accent), `/icon.svg`, `/icon-maskable.svg`, `/icon-192.png`, `/icon-512.png`, `/icon-maskable-512.png`, `/apple-touch-icon.png` and `/sw.js`, all made from `BaseAppOptions`; new options `AccentColor` (`#rrggbb`, the same as `--accent-color`; else `AccentHue`) and `ShellPaths` (more static files for the offline shell). The PNGs are drawn by the base without a library (decision 77). **A file of the same name in the app's `wwwroot` wins**, so an app with its own `manifest.webmanifest`, icons or `sw.js` is not changed by this version.
- **New: `pwa()`** (call it once in `app.js`, replacing the `serviceWorker.register` line): registers the worker, shows a quiet "Offline" line at the top when the device or a request has no connection, and "Neue Version · Neu laden" when a new worker waits (the generated worker does not take over by itself, decision 78). The cache is named `<id>-<app version>-base<base version>`; old caches are deleted when a worker takes over. Texts `pwa.*`, `auth.*`, `setup.*` (placeholders), `invite.passwordPlaceholder`; removed `auth.codeHint`, `setup.codeHint`, `setup.userHint`, `invite.passwordHint`.
- **To take it over, an app:** deletes its `sw.js`, `manifest.webmanifest`, `icon.svg`, `icon-maskable.svg` (or keeps them on purpose), adds `<link rel="apple-touch-icon" href="/apple-touch-icon.png">` and `<meta name="mobile-web-app-capable" content="yes">` to `index.html`, calls `pwa()`, and writes `AccentColor` in its `BaseAppOptions` when it uses `--accent-color`. `scripts/check-app.sh` compares `AccentColor` with `--accent-color`.
- `SEC-08` and `FORM-01` text changed; tests `PwaTests` (manifest, icons, colours, worker), `RuleFileTests.An_app_has_no_worker_list_of_its_own_and_the_page_names_the_manifest_and_the_icon` replaces `The_service_worker_keeps_only_the_shell`. Decisions 76 to 80.

## 0.4.0-alpha.3 (towards 0.4.0, in progress: steps 4 and 5 of `docs/DESIGN-0.4.md`)

New file `ui/parts.js` (import from `/base/parts.js`) holds the pieces of pages; `swipeFade()` is in `base.js`.

- **Rules COLOR-02 and A11Y-01 removed** (owner, decisions 70 and 71): the contrast test and the `@contrast` lines of `tokens.css` are gone; the look decides.
- **New: `table({ label, columns, rows | groups, actions, active, detail, selection, onRowClick, empty })`**: a white panel, small grey head, 50 px rows, hair lines, one action column (at most one tonal text button, quiet icon buttons, the switch last), rows that fold open (several at once), a tree (`children`, fine lines) and folding `groups` (count, `+`, menu, group switch that greys the rows), selection with check boxes driving `selectionBar()`. Real table wide; two-line rows on a phone (switch or main action at the right, the rest in a menu; selection by a "Select" button or a long press). Returns `{ el, set, flash, selected, clearSelection, destroy }`.
- **New: `tabs()`, `chips()`, `numberBox()`, `numberStrip()`, `ring()`, `foldCard()`, `timeline()`, `liveFeed()`, `chart()` (stacked area, SVG, legend that hides a series, dark box on hover and tap, keyboard), `flash()` (event in place), `swipeFade()`.** Rows of tabs, chips and number boxes swipe sideways on a phone. New classes `.panel` (white card, radius 22) and `.tabbar`, token `--shadow-card`, icons `pause` and `play`, texts `table.*`, `chart.*`, `feed.*`, `timeline.*`, `ring.of`.
- **Breaking, look: `.capsule` is now the white tab bar** (radius 14, light shadow, chosen item soft) and swipes on a phone; **`.chip` is white with a light shadow and soft when chosen** (no outline). Hand-written `.table` markup keeps working. An app with its own CSS for the old capsule or chip outlines should drop it.
- Rule numbers of removed rules are not reused (`RulebookTests` now asks only that numbers grow).

## 0.4.0-alpha.2 (towards 0.4.0, in progress: steps 2 and 3 of `docs/DESIGN-0.4.md`)

- **Breaking, markup: `field({ type: 'select' })` makes a `dropdown()`** (a button, our own list, optional search, full keyboard and ARIA) instead of a native `<select>`. Read it through `formValues` as before; code that did `field.querySelector('select')` must use `input[name=...]` (the hidden input that fires `change`) or the `.value` of the dropdown element. New `dropdown({ name, value, options, search, label, placeholder, onchange })`; the list is a sheet from below on a phone. `.select` (native) stays for hand-written markup and gets the new look.
- **Breaking, look: inputs, switch, segmented choice.** Inputs are 48 px (`--input-h`), radius 14, a white surface with a faint edge (`--edge`) and a soft shadow (`--shadow-field`), a soft ring in the accent ink on focus (`--ring`); the underlined filled field is gone. The toggle is 46 x 28 with a white knob (`--knob`); the segmented choice is white on a light track (`--seg-track`, `--seg-on`); the check box is bigger. The capsule keeps its old look for now.
- **New: `choiceTiles({ name, label, value, options: [[value, title, line]], onchange })`**, two to four tiles with radio semantics, the chosen one tinted with an ink border.
- **Breaking, look and behaviour: `openDialog`.** Big and centred on a wide screen (720 px; `size: 's' | 'm' | 'l'`, `wide: true` is `'l'`, 940 px), a sheet from below on a phone with the buttons fixed at the bottom, solid, a faint scrim behind (the page stays usable). New action option `side: 'start'` puts a button bottom left. `.dialog-cols` / `.dialog-col` give two columns. **It closes when the page changes** (the router calls the new `closeDialogs()` on `hashchange`; apps that navigate by other means call it). `confirmDialog` uses `size: 's'`.
- **New: `actionBar({ text?, icon?, actions })`** (a white card above the menu on a phone, at the bottom of the content on wide screens; `update()` changes it in place) and **`selectionBar({ count, actions, onClear })`** (dark, "n gewählt", takes the place of the action bar). **`floatBar()` is now the same as `actionBar()`** (same call, new look: solid white card instead of glass).
- **Breaking, look: the phone menu is a floating white pill** holding every place of `nav` (not only the first five), swiped sideways, the current place scrolled to the middle, soft fade at the edges, about 68 px per place. `.shell-tabs` is now the pill with a `.tabs-scroll` inside; a custom stylesheet that placed `.shell-tabs` as a bar must be removed. New `navBadge(path, true | n | '!')`: an accent dot or a raspberry count on a place of the side menu and the pill.
- New texts `form.search`, `form.nothingFound`, `form.choose`, `bar.*`, `shell.badge*`; new tokens `--edge`, `--shadow-field`, `--shadow-float`, `--ring`, `--seg-track`, `--seg-on`, `--knob`, `--input-h`.
- Rules changed: DIALOG-01 (big, solid, closes with the page, `side: 'start'`), SHELL-01 (the pill holds every place), COLOR-02 (a faint resting edge for own controls, decision 66). New tests `RuleFileTests.Dialogs_are_solid_and_close_when_the_page_changes`, `Selects_are_our_own_dropdown`, `The_phone_menu_is_a_pill_of_all_places`.

## 0.4.0-alpha.1 (towards 0.4.0, in progress: step 1 of `docs/DESIGN-0.4.md`)

- **Breaking, look: the accent is a colour.** An app may set `--accent-color` (any CSS colour, e.g. `#00e5ff`) instead of `--accent-hue`; all tokens derive from it (COLOR-01). `--accent-hue` still works and gives the look of 0.3 in its colours. New tokens `--accent-ink` (dark ink of the hue for links, icons, lines), `--hair`, `--r-btn`, `--r-row`; `--tertiary-hue` is gone. `--accent` is now the colour itself in light and dark and **no longer text**: an app that used `color: var(--accent)` should use `--accent-ink`. `--on-accent` is black or white by luminance.
- **Breaking, look: raspberry for faults, soft amber for warnings** (`--danger*`, `--warn*` change their values; `--warn` is no longer a text colour).
- **Breaking, look: the soft style.** `.btn.primary`, the current menu entry, the current tab, the chosen segment and capsule, the chosen chip use `--accent-soft` with `--on-accent-soft` instead of the bright fill. Bright accent stays for switches that are on, the brand mark and charts. Focus rings and links use `--accent-ink`.
- **Breaking, look: buttons.** Bold text on `.btn`, `.seg`, `.capsule-item`, `.chip`; radius 14 px (also icon-only); no change of shape on press (`.btn:active`, `.tile:active` lose their radius change).
- **New: row button** `.btn.mini` (named so because `.row` is the list row) (34 px, radius 11, tonal), `.btn.mini.strong`, `.btn.mini.quiet`, `.btn.mini.danger`, `.btn.mini.icon-only`; the target stays 44 px.
- **Hair lines** (`--hair`, 7 % of the text) between rows of cards, lists, tables and menus instead of `--line`.
- **No selectable chrome:** menu, tabs, buttons, chips, badges, card heads, table heads, bars cannot be selected as text.
- COLOR-01 and COLOR-02 changed (the rule text, `RuleFileTests.An_app_sets_its_colour_with_one_variable`, `ContrastTests`, which now evaluates the formulas of `tokens.css` for every hue and 14 colours). `check-app.sh` does not compare `AccentHue` with the CSS when an app sets `--accent-color`. Needs browsers with relative colour syntax (Chrome 119, Safari 16.4, Firefox 128).

## 0.3.5

- **CODE-02 allows `DuckDB.NET.Data`** (with its native library) for logs and statistics an app keeps in bulk; nothing else changes, the base itself does not use it. `RuleFileTests.Only_the_allowed_packages_are_referenced` now checks that an app uses only allowed packages and the base exactly its own three.

## 0.3.4

- **A body limit the endpoint declares** (SEC-04): `.WithMaxBody(bytes)` on an endpoint or a group, for uploads under paths with ids. Applied by the guard behind a session only (declared length and while reading); more than 0 and at most 1 GiB, else the start is refused. `LargeBodyPaths` and endpoints without the declaration behave as before.

## 0.3.3

Three safety fixes found in a review of the first app on the base. **Apps with their own `sw.js` should take the new one** (`samples/StartApp/wwwroot/sw.js`), and add the paths of their own static files to its list.

- **Hub sign-in and the last administrator** (AUTH-12): the sign-in through the hub no longer takes the role from the last active administrator; the sign-in works, the history gets the event `hub.role_kept`. All role changes go through `UserStore.TrySetRoleAsync`.
- **Setup stays closed** (AUTH-09): `AccountService.SetupNeededAsync` counts a person of the hub as an account (`UserStore.AnyConfirmedAsync` is now `AnyCanSignInAsync`, own sign-in confirmed or hub person).
- **Service worker keeps only the shell** (SEC-08): a list of shell files instead of every GET outside `/api/`; cache name `app-v2`.

## 0.3.2

Two additions for an app that moves onto the base with a database and clients of its own. Nothing changes for an app that sets neither.

- **Name of the migration table** (DATA-04): `BaseAppOptions.MigrationTable` (default `schema_migrations`) and `new Migrator(db, sets, table)`. Only a plain identifier is accepted; a foreign table of the default name stays untouched.
- **Own doors** (SEC-07): `BaseAppOptions.Doors`, a list of `ApiDoor(prefix, check, maxBodyBytes)`. A path prefix under `/api` that the app protects itself (a bearer key, no cookie, no Origin, paths with ids). The check is required; under a door the base never reads the session cookie and sets no identity. The base's own paths cannot be declared as a door (refused at start). A door matches without regard to case, as the routes do, so no other spelling of its path falls to the session guard.

## 0.3.1

**The repository is public now, under the MIT licence** ([LICENSE](LICENSE)). The contract with the hub and the first trial against a real hub moved to [docs/CONTRACT.md](docs/CONTRACT.md) and [docs/HUB-TRIAL.md](docs/HUB-TRIAL.md); the other drafts left the repository.

**Second round of the look: more JulOS, and a little Jularr.** Same markup and API; one **visible change for every app**: a `.list` outside a card is now tiles with a space between (inside a card, or as `.list.lines`, the rows are lines as before), and an app that put a list in `.card.flush` should drop the card.

- **Pages:** `shell()` builds the head of the page in the page: big title (`view.title`), a second line (new `view.sub`), the main action at the right (`view.actions`), the way back on wide screens; on the phone a small bar with the app name that shows the title once the page has scrolled. A `nav` place with `foot: true` goes into a second group at the bottom of the side list. The selected place is a soft filled pill over the whole width.
- **Glass and depth:** stronger gradient (three tinted spots), clearly translucent cards with a fine light edge (`--glass-edge` is now the text colour at 13 percent), tiles, rows, empty states and the menu are glass. Dark gets the depth of Jularr: a blue-black from the hue of the app, surfaces a trace lighter than the page, fine edges instead of shadows (shadows are off in dark except for sheets and menus).
- **New parts:** capsule (`.capsule`, `capsule({ label, items })`, and `segmented()` now looks like it: round outline, the chosen part a filled pill), chip (`.chip`, `.chips`), tiles (`.tiles`, `.tile`, `.tile-lead`), round outlined icon button (`.btn.outline.icon-only`), search pill with symbol (`field({ type: 'search' })` wraps the input in `.search`), floating bar (`floatBar({ text, icon, actions })`). The empty state is a glass tile with a large rounded symbol.
- **Density:** one step more compact: `--text-s` 13 px, `--text-m` 15 px, rows 52 px, `.stack` gap 12 px; the 44 px of a finger stay. Titles, card titles and section titles are bolder.
- **Compact by default, both modes equal:** rows are tiles with 6 px between, radius 14 px, two text lines, 44 px minimum (the finger rule). The person menu now has the three steps System (the default, follows the device), Light and Dark, with the current one marked (`openMenu` items may have `checked`).
- Default `--accent-soft`, `--secondary-soft`, `--tertiary-soft`, `--line` and the dark surfaces changed values; `--shadow-*` have no tint in dark.

## 0.3.0

**The look follows JulOS.** An app on the base now looks like a sibling of JulOS (Material 3 Expressive from one seed colour). Every app changes its look at its next sync; the markup and the API of `base.js` do not change.

- **Colour:** the Material 3 roles and tone steps of JulOS, derived from `--accent-hue`: tonal surfaces (`--bg`, `--surface`, new `--surface-1` and `--surface-4`), accent = primary, `--accent-soft` = primary container, new `--secondary-soft` and `--tertiary-soft` (with `on-` colours), softer state colours. The default hue is now **299** (the hue of the JulOS seed; was 265). New and changed `@contrast` pairs; `ContrastTests` understands the tertiary hue (`--tertiary-hue`).
- **Background and glass:** `--page-bg` (the soft gradient of JulOS, CSS only), `--glass`, `--glass-strong`, `--glass-edge`; cards, shell bars, side list and menus are glass. `prefers-reduced-transparency` turns glass solid; `forced-colors` gets outlines.
- **Shape:** radii 4, 8, 12, 20 (cards), 28 (sheets and dialogs), round (buttons, chips, search). **Breaking (visual):** `--r-s`, `--r-m`, `--r-l` changed value (6, 10, 16 are now 8, 12, 20); `--r-xs` and `--r-xl` are new; `--tabs-h` is 64 px.
- **Controls:** buttons are tonal by default, filled for `primary`, text for `ghost`, `danger` is tonal red, all round and "forming" when pressed; filled fields with an underline (labels stay above); search fields are glass pills; the switch and segmented choice as in JulOS; badges and notices use the secondary tone (new `.badge.tertiary`, `.notice.accent`, `.notice.tertiary`).
- **Shell:** bar becomes glass when the page scrolls; bottom bar with the growing pill; side list with round items. Dialog: sheet with a handle on the phone, centred window on wide screens (radius 28).
- **Type and motion:** the font stack of JulOS, the Material 3 size steps (`--text-s` 14 px, `--text-m` 16 px, `--text-l` 20 px), weights 500 for titles and labels; durations 150, 250 and 400 ms and the JulOS easing curves (`--dur-m`, `--dur-l`, `--ease-out`, `--ease-in`).
- Catalogue: the section "The look and where it comes from" with the mapping of roles to tokens.

## 0.2.1

First trial against a real JulOS 0.26.0 (everything local, [docs/HUB-TRIAL.md](docs/HUB-TRIAL.md)): the sign-in flow fits; two things did not and are fixed here.

- **Signing out of the app returns to the app (AUTH-13, AUTH-14).** The ID token of the hub stays on the session as the hint for the hub's sign-out (migration `accounts/003_hub_hint`,
  `sessions.hub_hint`, deleted with the session); the sign-out address now has `id_token_hint` and `post_logout_redirect_uri` (`/` of the app). The hub then signs out without asking and sends the
  person back. **Register "address after sign-out" at the hub** (the settings card shows it); without it the hub asks first, as before. The rule no longer says "a token of the hub is kept nowhere".
- **The button asks the hub afresh** when pressed (it used the ten minute memory and sent people to a dead address when the hub had just gone away); errors say why in words ("nothing answers at that address"), not the name of an exception.
- `HubIdentity` has an optional `Hint`, `SessionStore.CreateAsync` an optional `hubHint`, `HubSignIn.EndSessionUrlAsync` takes the request and the hint. No other breaking change.
- Docs: how to run app and hub on one machine in containers (the same address inside and outside: `*.localhost` names as network aliases), what to register at the hub, what the trial found in JulOS (not changed there).

## 0.2.0

Roles and people, the app describes itself, and signing in through the hub (OpenID Connect). Every app works as before without any of it being switched on.

- **Roles and people (AUTH-11, AUTH-12, SEC-03):** the app names a small fixed list of roles in `BaseAppOptions.Roles` (always with `admin`, default admin and member);
  one role per person. `RequireRole("admin")` on an endpoint or a group is checked by the guard in one place. Admins manage people in the settings
  (`ui/people.js`): add by an invitation link (once, 24 hours, in the fragment of the address), change the role, block (ends the sessions at once), delete.
  The last admin stays. Migration `accounts/002_people` (state, `hub_sub`, `hub_sid`, invitations).
- **The app describes itself (CONTRACT-01, CONTRACT-02):** `GET /api/about` (anonymous, fixed fields, 60 per minute and address) from `BaseAppOptions`
  (`Description`, `Icon`, `AccentHue`, `Start`, `Roles`); `/api/health` gains `status` (`ok`, `degraded`, `down`), `contract` and `time`; `HealthState` for `degraded`.
- **Sign in through the hub (AUTH-13, AUTH-14):** the settings card "Central sign-in" (`ui/hub.js`, admin only: address, client id, secret, on or off, check),
  a second button on the sign-in page, back-channel logout, sign-out through the hub. Authorization code with PKCE, `state`, `nonce`, ES256 only, issuer, audience and
  lifetime checked by Microsoft's token library; the app keeps its own session and no token. Persons by `sub` only, role from the claim at every sign-in.
  Migration `web/002_hub`. How to set it up: [docs/SIGN-IN-WITH-HUB.md](docs/SIGN-IN-WITH-HUB.md).
- **Package:** `Microsoft.IdentityModel.JsonWebTokens` 8.19.2 joins Npgsql and Argon2 (CODE-02).
- **Breaking:** `MeResponse` has `appRoles`, `viaHub` and `hub`; `Health` has three more fields; `LoginResponse` is unchanged; the key `role.admin` of the texts is gone (role names come
  from the app); `AddBaseAccounts` takes the roles; `SessionIdentity` has `HubSid`. An app that builds its own `BaseAppOptions` gains new optional properties and needs none of them,
  but should set `Icon`, `AccentHue` and `Description`. In `new-app.sh` the colour is now also written to `Program.cs` (`AccentHue`): keep it equal to `--accent-hue` in `app.css`.
- **Rulebook** now has 56 rules (AUTH-11 to AUTH-14, CONTRACT-01, CONTRACT-02; SEC-01, SEC-03, SEC-04, OPS-04, CODE-02 changed).

Not in this version: a test against the real JulOS (the tests use their own small hub), passkeys, the keyed hub calls of the contract (stage 2 and 3), mail, push, backup.

## 0.1.0

The foundation: a person or an agent makes a new, running app with PostgreSQL, a setup assistant and a safe sign-in in minutes.

- **Core:** results and errors with stable codes, a validator that reports every wrong field, ids (UUID version 7), keyset paging, a manual clock.
- **Data:** plain SQL helpers, transactions, numbered migrations applied at start with checksums and an advisory lock.
- **Accounts** (taken from JulOS and stripped down): people with roles as a plain list, Argon2id, TOTP with the secret encrypted, recovery codes,
  sessions as a hash in PostgreSQL (30 days idle, 180 at most, rotated, one row per device), lockout, the account history.
- **Web:** security headers and a strict content policy, the guard of `/api`, `/api/health` with versions, settings in the database with rules,
  the setup assistant with the code from the log, sign-in, the account endpoints, German and English.
- **The look (`ui/`):** tokens with one accent variable per app and tested contrast, the shell, the controls, one icon file, `base.js`,
  the sign-in and account pages, the living catalogue.
- **Start app** with Dockerfile and a Compose file without variables (one volume), `scripts/new-app.sh`, `scripts/sync-base.sh`, `scripts/check-app.sh`.
- **Rulebook** v0 (`docs/RULES.md`, 50 rules) with the tests that check it.

Not in this version: passkeys, managing further people, sign-in through a hub (OpenID Connect), mail, push, background jobs, backup and restore.
