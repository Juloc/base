# Design 0.4: every component in the family's own style

The owner went through the first app on the base (caddy) page by page and decided its look (caddy's `docs/REDESIGN.md`). What is not caddy's alone comes here, so every app gets it at its next sync. **Every base version contains all components, complete** (the owner's rule): an app takes a version as a whole and adds only its definition (name, colour, pages).

The look stays close to JulOS: friendly, tonal surfaces, rounded, calm. What changes: colour per app as a real colour, a soft style for selections and buttons, own controls instead of browser ones, big dialogs instead of side panels, tables as the main way to show lists, a floating menu pill on the phone.

Released as **0.4.0**, marked **Breaking** where an app's markup or look changes. Built in the steps below; each step ends with its catalogue entries (`ui/catalog.html`, KIT-02), screenshots at 375 px and wide, light and dark, the base's tests passing, and one commit.

## Step 1: colour, type, buttons (tokens.css, base.css)

- **Colour per app (COLOR-01 changes):** an app names its accent as a colour (`--accent-color`, e.g. caddy `#00e5ff`), not only a hue. The tokens derive from it: the fill (`--accent`, the colour itself), text on it (`--on-accent`, dark when the colour is light), a **dark ink** of the same hue for lines and icons (`--accent-ink`, about lightness 0.52), soft surfaces (`--accent-soft`, `--on-accent-soft`, `--secondary-soft`). `ContrastTests` check every derived pair for the colours of the existing apps and a few edge colours (very light, very dark). `--accent-hue` keeps working for apps that set only a hue.
- **States for every app:** faults in **raspberry** (about oklch 0.56 0.21 5; soft surface and text derived), warnings in **soft amber** (pale surface about oklch 0.96 0.05 85, strong amber about oklch 0.70 0.17 75 for text and icons). `ok` stays.
- **The soft style ("Sanft"):** the current page in the menu, the chosen tab, primary buttons and row buttons all use the same soft accent surface with its dark text. The bright accent only for switches that are on, rings and charts.
- **Buttons:** bold text everywhere (`.btn`, segmented, chips); radius about 14 px; **no shape change and no flash on press** (today `.btn:active` rounds the corners differently); one **row-button** style, 34 px high, radius 11: tonal (default), strong (accent fill), quiet (no surface).
- **Hair lines** between rows: about 7 % of the text colour, not the strong line.
- **No selectable chrome:** menu, tabs, buttons, chips, row heads, table heads cannot be selected as text (`user-select: none`), above all on the phone; content stays selectable.

## Step 2: own controls (base.css, base.js)

- **Input:** 48 px, radius 14, a faint edge, a soft shadow, a soft accent ring on focus; explanations go into the placeholder, labels are short and above; no underlined Material field.
- **Dropdown of our own** (`dropdown()`): a button with the value and a chevron; the open list is styled (rounded, shadow, the chosen entry tinted with a check); optional **search field** at the top that filters as you type; keyboard (arrows, Enter, Escape, type-ahead) and screen readers (`listbox`, `option`, `aria-activedescendant`). Replaces native selects in `field({ type: 'select' })`.
- **Toggle:** light track, white knob with a small shadow, accent when on; 46 × 28.
- **Segmented choice** (white chosen part with a light shadow on a light track) and **choice tiles** (two to four tiles with a title and a line, the chosen one tinted with an ink border).
- **Check box** in the accent ink.

## Step 3: dialog, bars, menu (base.css, base.js)

- **Dialog (`openDialog`, DIALOG-01 changes):** big and centred on wide screens (up to about 940 px, two columns when given two), a sheet from below on the phone with its buttons fixed at the bottom; **solid, never see-through**; **closes when the page changes** (today an open dialog stays on screen after navigation, a bug). Delete and deactivate sit bottom left, cancel and the main action bottom right.
- **Bottom action bar:** a white card with radius like the tabs, centred above the menu on the phone and at the bottom of the content on wide screens; one or two buttons (e.g. "+ Route", "Anwenden (n)"). Replaces the glass `float-bar` look.
- **Selection bar:** dark (text colour as surface), "n gewählt" and actions; replaces the action bar while rows are selected.
- **Menu on the phone:** a **floating white pill you swipe sideways**, holding all pages (about 68 px per item, the current one scrolls to the middle, soft fade at the edges); replaces the full-width strip. On wide screens the side menu stays, with the soft current entry.
- **Dot on a menu entry** for events on another page (accent dot; a count or "!" in raspberry for faults).

## Step 4: tables (base.css, base.js)

- **Table** in a white card (radius 22, soft shadow), head in small bold grey, rows 50 px with hair lines, hover tint.
- **One action column:** right-aligned; at most one tonal text button for the row's main action, otherwise quiet icon buttons in a fixed order; an "active" switch, if any, is the last column.
- **Rows that fold open** (a chevron, several open at once) to a detail part in the tinted surface.
- **Tree:** groups with a fold chevron, a head with name, count, `+`, `⋮` and a group switch (a switched-off group greys every row); levels indented with fine tree lines.
- **Selection:** check boxes per row and the selection bar.
- **On the phone** a table becomes a list of two-line rows (name with symbols above, the second line below, the switch or one action on the right); a tree keeps its indentation; selection through a "Auswählen" button or a long press.

## Step 5: pieces of pages (base.css, base.js)

- **Tabs:** a white bar with radius 14 and a light shadow, the chosen tab soft; on the phone one row you swipe sideways (the same for filter chips and periods).
- **Number box:** a tonal icon square, a big number, a small label, an optional change; on the phone a swipeable strip.
- **Stacked area chart** (`chart()`): smooth layers, a thin surface-coloured line between them, three faint guide lines, the axis below, a legend that hides a kind on click, a dark box with every value on hover or tap; colours given by the app (caddy: medium cyan, light cyan, soft amber, raspberry). SVG, no library.
- **Live feed:** rows added at the top without rebuilding anything; a pause button; a colour dot per row.
- **Timeline:** day headings, cards on a line with a round state node (green, amber, raspberry).
- **Ring with a number** (days left), **folding card head** (a card whose head opens its body).
- **Events in place:** a new row lights up briefly; no toasts for events (toasts stay for "saved" and errors of an action).

## Step 6: sign-in, gate, PWA (built in 0.4.0-alpha.4)

- **Sign-in, first setup, invitation:** one white card in the middle (radius 28), app mark, title, one line; "Mit dem Hub anmelden" as a neutral button above "oder mit eigenem Konto"; fields with placeholders; the main button in the soft style; language and theme as quiet text buttons at the bottom. An app sets only name, line and colour. A small API for apps with pages of their own around a sign-in (caddy's gate): the same card with a target box ("danach geht es direkt dorthin"), and a state card (spinner, "Hier nicht erlaubt", "Link abgelaufen" with one clear button).
- **PWA base:** installable (manifest from `BaseAppOptions`: name, short name, colour, icon), an icon and a splash per app, the theme colour from the app's accent, an offline shell (the shell loads, data says "offline" in words), updates without stale files (the shell list of SEC-08, a cache name per version).

## Step 7: memory defaults (after caddy measured them)

- The .NET settings for little memory (workstation GC, conserve memory, a heap limit) in the start app's Dockerfile and in `templates/`, once caddy shows they hold (target: an idle app below 80 MB).

## Not in 0.4

- Anything only caddy has (its pages, its chart colours, its menu entries).
- Passkeys, the AI client.
