# Changelog

The base follows SemVer; the version stands in `VERSION` (OPS-05). An app carries the version of its copy in `base/VERSION`;
`scripts/check-app.sh` shows how far a copy is behind, `scripts/sync-base.sh` brings it up to date. Before 1.0.0 a minor version may change
what apps rely on; such changes are marked **Breaking**.

## 0.3.1

**Second round of the look: more JulOS, and a little Jularr.** Same markup and API; one **visible change for every app**: a `.list` outside a card is now tiles with a space between (inside a card, or as `.list.lines`, the rows are lines as before), and an app that put a list in `.card.flush` should drop the card.

- **Pages:** `shell()` builds the head of the page in the page: big title (`view.title`), a second line (new `view.sub`), the main action at the right (`view.actions`), the way back on wide screens; on the phone a small bar with the app name that shows the title once the page has scrolled. A `nav` place with `foot: true` goes into a second group at the bottom of the side list. The selected place is a soft filled pill over the whole width.
- **Glass and depth:** stronger gradient (three tinted spots), clearly translucent cards with a fine light edge (`--glass-edge` is now the text colour at 13 percent), tiles, rows, empty states and the menu are glass. Dark gets the depth of Jularr: a blue-black from the hue of the app, surfaces a trace lighter than the page, fine edges instead of shadows (shadows are off in dark except for sheets and menus).
- **New parts:** capsule (`.capsule`, `capsule({ label, items })`, and `segmented()` now looks like it: round outline, the chosen part a filled pill), chip (`.chip`, `.chips`), tiles (`.tiles`, `.tile`, `.tile-lead`), round outlined icon button (`.btn.outline.icon-only`), search pill with symbol (`field({ type: 'search' })` wraps the input in `.search`), floating bar (`floatBar({ text, icon, actions })`). The empty state is a glass tile with a large rounded symbol.
- **Density:** one step more compact: `--text-s` 13 px, `--text-m` 15 px, rows 52 px, `.stack` gap 12 px; the 44 px of a finger stay. Titles, card titles and section titles are bolder.
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

First trial against a real JulOS 0.26.0 (everything local, [docs/plans/HUB-TRIAL.md](docs/plans/HUB-TRIAL.md)): the sign-in flow fits; two things did not and are fixed here.

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
