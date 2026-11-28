# The contract between an app and the hub (draft for decision)

Status: **draft**, 2026-11-23. Builds on `BASE.md` in JulOS. "Hub" = JulOS. Contract version: **1**. Effort: S = hours, M = a day or two, L = several days.

Two rules decide everything: **an app works alone, as well as with the hub**, and **the hub has no code that knows a particular app**: the app describes itself, the hub works from that description. The contract is small, has three stages, and each stage is useful without the next.

Stages: **1 Describe** (every app, anonymous `health` and `about`), **2 Link** (one sign-in, an AI key, both sides know each other; from the hub or by hand), **3 Offer** (tiles, few settings, actions, push, launchers, roles, backup and update information).

The app serves `/api/health` and `/api/about` (anonymous) and `/api/hub/*` (what the hub calls, needs the hub key). The hub serves `/api/apps/*` (what an app calls, needs the app key). Fields are camelCase English, JSON UTF-8.

## 1. Stage 1: every app

**`GET /api/health`**: no sign-in, cheap, never touches other apps. `down` is HTTP 503, the others 200.

```json
{ "status": "ok", "version": "1.4.2", "base": "0.3.0", "contract": 1, "time": "2026-11-23T08:15:00Z" }
```
`degraded` = works, something is wrong; `down` = cannot do its job. No reasons here (the keyed state of stage 3 has them).

**`GET /api/about`**: no sign-in, no secrets, fixed fields only, `Cache-Control: max-age=300`.

```json
{
  "contract": 1,
  "id": "notes",
  "name": "Notes",
  "description": { "en": "Plain notes with search.", "de": "Schlichte Notizen mit Suche." },
  "version": "1.4.2",
  "icon": "note",
  "accent": "#4F7CAC",
  "start": "/",
  "can": ["hub", "state", "settings", "actions", "notify", "roles", "backup"],
  "embed": { "hub": true },
  "provides": [ { "name": "notes.search", "version": 1 } ],
  "needs": [ { "name": "calendar.events", "min": 1, "optional": true } ]
}
```
`id` is a slug, the repository's plain name. `icon` is a name from the base icon set. `start` is a **relative** path, joined by the hub with the address the owner entered. `can` lists what the app implements: a word not listed means "do not call". `provides`/`needs`: section 4. `embed.hub` (built in base 0.4.4): the app may be shown inside a window of the hub; it is true only while the app allows it (`BaseAppOptions.Embeddable`, so per version) and a hub is connected and switched on, and then the app names exactly that hub's origin in `frame-ancestors`. Missing or false: own window only. The session cookie stays `SameSite=Strict`, so a frame works only when hub and app are the same site (https, same registrable domain); a hub offers its window only then.

**Errors** (every `/api` route): HTTP status plus

```json
{ "error": { "code": "validation_failed", "message": "Title is too long.", "fields": { "title": "too_long" }, "retryAfter": null } }
```
`code` is one of `bad_request`, `validation_failed`, `unauthorized`, `forbidden`, `not_found`, `conflict`, `rate_limited` (with `Retry-After`), `confirm_required`, `unavailable`. `message` is English, for developers, with no secrets, paths or stack traces.

**Language, time, units.** Texts for people are a string or an object by language (`en` required, `de` expected, else `en`). Time is UTC ISO 8601 with `Z`; durations whole seconds, sizes whole bytes, fractions 0 to 1.

**Moving forward.** `contract` is a whole number; adding a field or a value never raises it, a change that breaks a reader does. **Readers ignore unknown fields.** An unknown enum value means the safe reading: `status` = `degraded`, `danger` = `critical`, value `type` = not shown. A higher `contract` than the hub knows: stage 1 only, and the hub says so.

## 2. Stage 2: linked to the hub

Two keys, one OpenID Connect client. The **app key** (app to hub) is created by the hub, grants scopes the owner ticked (`ai`, `notify`, `bindings`) and is also the AI key. The **hub key** (hub to app) is created by the hub and lets it read state and run the actions of stage 3. Both are 256 random bits, sent as `Authorization: Bearer`, **stored only as SHA-256** on the receiving side, compared in constant time. The OIDC client (`clientId` = app id, own secret, exact return address) is for people signing in (`BASE.md`); a token of that sign-in may be sent as `X-Hub-Token` so the hub can count per person; nothing depends on it.

The link is **two halves that work alone**: app to hub (sign-in, AI, notify, bindings) and hub to app (tiles, settings, actions). A manual link can do one half only.

### Adoption from the hub (the normal way)

1. In the app, the owner opens *Connections* and taps **Allow linking**: the app shows a **pairing code** (8 characters, valid 10 minutes, single use, 5 wrong tries lock it). Without it an unlinked app could be taken by the first one who reaches it.
2. In the hub: *Add app*, address (and, if the hub reaches the app by another name, the inner address) plus the code.
3. The hub reads `/api/about` (timeout 5 s, no redirects, 64 KB, http or https only, owner only), shows what it found and asks the owner which scopes to grant.
4. The hub creates the OIDC client, the app key and the hub key, and sends them once:

```json
POST /api/hub/link
{ "pairing": "K7QM-2WXD", "hub": { "url": "https://hub.example.test", "apiUrl": "http://os:8080" },
  "hubKey": "<generated>", "appKey": "<generated>",
  "oidc": { "authority": "https://hub.example.test", "clientId": "notes", "clientSecret": "<generated>" },
  "scopes": ["ai", "notify", "bindings"] }
```
5. The app stores everything **encrypted in its own database**, switches on "Sign in with JulOS" and `frame-ancestors` for the hub's address, **without restart**. The app then calls `GET /api/apps/me`; if that fails it says so on the Connections page and the hub keeps the entry "pending".

### By hand, in the app (always possible)

*Connections* has three fields: hub address, app key, and an optional OIDC client id and secret. The owner creates the app key in the hub (*Apps, key for an app not yet adopted*, shown once) and pastes it. For the other half the app shows its own generated **hub key** once; the owner pastes it at the app's entry in the hub. Applied at once, no restart.

### What may be in environment variables

Only what is needed before the app can ask anyone: `HUB_URL` (seed for the address if the setting is empty) and `HUB_OFF=1` (switches every hub feature off; wins over the setting). **Never a key, a secret or a pairing code**: they would live in Compose files, `docker inspect` and logs. Keys live in the database.

### Unlinking

From the hub: `DELETE /api/hub/link` with the hub key; the hub revokes both keys and the OIDC client. From the app: *Disconnect* calls `DELETE /api/apps/me` with the app key (best effort) and forgets everything locally in any case. If either side is gone, the other side's button still works: the hub shows "app unreachable, remove entry", the app shows "hub unreachable, disconnected". Both are idempotent. People who only signed in through the hub keep their account and set up the app's own sign-in by recovery.

### When the hub is away: nothing fails

App sessions are the app's own and keep working; its own sign-in form stays; AI calls answer `unavailable`; push messages wait in a small outbox (100, 24 hours); bindings (section 4) keep working app to app. Hub polls are slow (health 30 s, state 60 s, with back-off).

## 3. Stage 3: what the hub may show and do

Everything below is answered with the **hub key**, never anonymously. The app describes it once and the hub renders it:

**`GET /api/hub/schema`**

```json
{
  "values": [
    { "id": "unread", "label": { "en": "Unread" }, "type": "number" },
    { "id": "storage", "label": { "en": "Storage" }, "type": "bar" },
    { "id": "lastSync", "label": { "en": "Last sync" }, "type": "time" },
    { "id": "recent", "label": { "en": "Recent" }, "type": "list" }
  ],
  "settings": [ { "id": "digest", "label": { "en": "Daily digest" }, "type": "bool" },
                { "id": "smtpPassword", "label": { "en": "Mail password" }, "type": "secret" } ],
  "actions": [ { "id": "emptyTrash", "label": { "en": "Empty trash" }, "kind": "post", "danger": "dangerous" },
               { "id": "export", "label": { "en": "Export" }, "kind": "link", "href": "/export" } ],
  "launchers": [ { "id": "new", "label": { "en": "New note" }, "icon": "add", "href": "/new" } ],
  "roles": [ { "id": "admin", "name": { "en": "Administrator" } }, { "id": "member", "name": { "en": "Member" } } ]
}
```

**State values: `GET /api/hub/state`** (poll, `ETag`, at most 50 values; texts only, never HTML):

```json
{ "values": [ { "id": "unread", "value": 12, "tone": "ok" },
              { "id": "storage", "value": 0.62, "text": "62 %", "tone": "warn" },
              { "id": "lastSync", "value": "2026-11-23T07:55:00Z" },
              { "id": "recent", "items": [ { "text": "Shopping list", "href": "/n/41" } ] } ],
  "issues": ["mail_not_set"],
  "backup": { "last": "2026-11-23T03:00:00Z", "ok": true },
  "update": { "latest": "1.5.0", "notes": "/changes" } }
```
Four types only: `number`, `bar` (0 to 1), `time`, `list` (at most 10 items). `tone` is `ok`, `warn`, `bad` or absent. `issues` are fixed codes the hub can show or turn into a push rule. Anything richer is the app's own page, opened with `start`.

**Settings** (`GET`/`PUT /api/hub/settings`): only what the app flags for the hub, at most 20, **never** security settings. Types `bool`, `text`, `number` (`min`, `max`), `choice` (`options`), `time`, `secret`. A secret reads as `{ "set": true }`, is written with a value, cleared with `null`, and never travels back; the hub asks the second factor for it. The app validates and answers `validation_failed` per field.

**Actions** (`POST /api/hub/actions/{id}`). `kind: "link"` is a plain relative address opened in the browser as the signed-in person: it changes nothing by itself (the app's own page asks). `kind: "post"` is done by the hub with the key and **never by GET**. `danger` is one of the three levels the hub already uses for host actions: `routine` (small sheet, Execute), `dangerous` (warning plus tick) and `critical` (warning, red line, tick, never below the tick). The hub runs the two-step confirmation and sends the result of it:

```json
POST /api/hub/actions/emptyTrash
{ "by": "<hub user id>", "danger": "dangerous", "confirmed": true, "requestId": "<uuid>" }
```
The app answers `200 {"ok":true,"message":"Trash emptied"}` (over 10 s: `202` and progress as a state value), may refuse with `409`, audits `by` and `requestId`, and treats a repeated `requestId` as the same request.

**Push** (`POST /api/apps/notify`, app key with scope `notify`):

```json
{ "kind": "reminder", "title": "Water the plants", "body": "Due today", "href": "/t/7",
  "audience": { "person": "<hub user id>" }, "dedupe": "plants-2026-11-23" }
```
`audience` is `{ "person": id }` or `{ "role": "admin" }`, **only people who hold a role in this app**; never everyone, never someone else's people. Limits: title 60 and body 200 characters, plain text, `href` relative (the hub joins it with the registered address), 20 messages per app per hour, 5 per person per hour, the same `dedupe` is dropped for 30 minutes, quiet hours of the person apply. The hub **prefixes the app name** so no app can pass as the hub, and keeps the rule of PUSH.md: nothing private in a message.

**Roles.** The schema's `roles` is what the hub offers when assigning people; the chosen ones arrive in the sign-in statement as `roles`. Unknown roles are ignored, none means no access. Every app has `admin` and `member`.

**Backup and update.** The app keeps its own backup (base Data part); the contract adds `backup` in state (last time, ok; the hub can push "no backup for 3 days") and the reserved routine action `backup.run`. **Restore stays in the app** (critical, recovery key); the hub never restores. `update` in state says a newer version is known; the hub shows a quiet note and **never updates an app** (SELF-UPDATE.md).

## 4. Capabilities between apps

A capability is a name, a whole version and a small API under `/api/cap/{name}/v{n}/`, written in one short file in `base/docs/capabilities/` (`calendar.events`, `receipts.documents`, `arr.requests`). A provider lists it in `provides`, a consumer in `needs` (`optional`: the consumer works without it). No shared database or cookie; only this API.

**Binding through the hub:** the owner connects "notes needs calendar.events" with one choice in the hub (or the hub offers it because exactly one provider exists). The hub makes a **pair key** for that one pair: it tells the provider (`PUT /api/hub/grants/{name}`: key hash, consumer id) and the consumer (`PUT /api/hub/bindings/{name}`: address, pair key). **By hand:** the provider's *Connections* page has *Access for other apps*: name a capability and get a key shown once; the consumer's page has address plus key per need. After the binding the two apps talk **directly**; the hub is not in the path, so the binding survives the hub being down. The consumer treats the provider as unreliable (3 s timeout, cache, "not available" in its own UI); the provider checks the key's capability on every call. Removing a binding removes the key on both sides.

## 5. Security

- **Anonymous:** `health` and `about` only, fixed fields (name, version, colours, capability words). No paths, hosts, user names, database names, counts, error text or absolute URLs. The version is visible on purpose and permits nothing; the proxy may hide both routes from the Internet, the hub reads them from inside.
- **Keys:** hash only, constant-time compare, https or a private network only (the hub never sends a key over plain http to a public address). The app's identity comes **from the key**, never from a request field. Rotation: the hub sends a new bundle with the old key; the old one works 10 more minutes.
- **Rate limits:** anonymous 60 per minute per address; keyed 120 per minute per key; wrong keys 10 per minute per address, then `rate_limited`. Notify and AI have their own budgets.
- **No open redirects:** every link of an app is relative and joined with the address the owner registered; the hub follows no redirect of an app. **No actions by GET**, no state change on anonymous routes.
- **Audit on both sides:** the hub: adoption, keys, every action (prepared, denied, started, result, as `host_audit`), notify sent or dropped and why. The app: link, unlink and every hub call that changes anything, with `by` and `requestId`. No secrets or content in a line.
- **A bad or taken-over app** can at most: show wrong numbers and texts in its own tile (plain text, never HTML; 64 KB, 50 values, 20 actions, 20 settings; its name always beside it); push to its own people within limits (prefixed with its name); spend its AI budget; link to its own registered address. It **cannot** read another app's state or keys, touch hub sessions, act as a person, make the hub call an address of its choice, or run anything in the hub. One tap blocks it: keys revoked, OIDC client off, its sessions ended.
- **A taken-over hub** can only do what apps expose: read state, change flagged settings, run listed actions at their confirmation level. The contract has no file access, shell or database; the hub's sign-in is the one door and gets its own review (`BASE.md`).

## 6. What the hub builds, what the base brings

| JulOS part | Effort |
|---|---|
| Registry of apps (address, inner address, status, block) and the poller for health and `about` | M |
| Tile and window per app from `about` (icon, accent, name; window with the app, phone page, "open in tab" fallback) | M |
| Adoption: pairing code, key and OIDC client creation, handover, manual key creation, unlink | M |
| Display of health, version, base version, update note, backup age; push on "app down" | S |
| Value renderer (four types) and tiles on the start screen | M |
| Settings form and actions with the three confirmation levels (reuses `ConfirmPolicy`, history) | M |
| `POST /api/apps/notify` with scopes, audiences, limits (the app keeps its own outbox) | S |
| Launchers on the start screen and the dock menu | S |
| Roles per app in people management and in the sign-in statement | M |
| Bindings: grants, pair keys, the Connections view | M |

The **base brings** (an app writes no contract code): middleware for errors, rate limits and key checks; `health` and `about` from one object; the *Connections* page; the encrypted key store; a hub client with timeouts and the notify outbox; claim-to-role mapping; audit hooks; `frame-ancestors` from the linked hub. The app only describes itself:

```csharp
builder.AddBase(app => app
    .Describe(new AppManifest
    {
        Id = "notes", Name = "Notes", Icon = "note", Accent = "#4F7CAC", Start = "/",
        Description = new("Plain notes with search.", de: "Schlichte Notizen mit Suche."),
        Roles = ["admin", "member"],
        Provides = [new("notes.search", 1)],
        Needs = [new("calendar.events", min: 1, optional: true)],
    })
    .Value("unread", ValueType.Number, ctx => notes.CountUnread())
    .Setting("digest", SettingType.Bool)
    .Action("reindex", Danger.Routine, ctx => search.Rebuild())
    .Launcher("new", "/new", icon: "add"));
```
`can` is computed from what is registered; an app that registers nothing is a valid stage 1 app. Rules get numbers (`CONTRACT-01` ...) and tests in the base (anonymous routes carry no forbidden field, wrong key gives 401 in equal time, unknown fields are ignored).

## 7. Order, open questions, not now

**Order.** (1) Base: stage 1 in the Web part; `notes` meets it. (2) JulOS: registry, poller, tile and window: every app appears, adoption by hand. (3) Stage 2 together with the sign-in provider and the AI gateway: `notes` is adopted. (4) Stage 3 in slices: values, then push, then actions and settings, then launchers and roles, then backup and update. (5) Capabilities when the first real pair needs one (for example `notes` and `calendar`).

**Open questions** (my proposal first):
1. **Pairing code for adoption.** Proposal: required (section 2), because otherwise the first caller owns a fresh app. Alternative: no code while the app is in its setup assistant.
2. **Version visible anonymously?** Proposal: yes; the proxy may hide the routes from the Internet.
3. **App inside a hub window (iframe)?** Proposal: yes on desktop with `frame-ancestors` set at linking, full page on phone, "open in tab" as fallback.
4. **Settings in the hub: read only or editable?** Proposal: editable for what the app flags, secrets only with the second factor, security settings never.
5. **One app key with scopes, or one key per use?** Proposal: one key with scopes the owner ticks; pair keys stay separate.
6. **Update information:** should an app check its own releases, or should the hub look at releases of all repositories once? Proposal: the app reports `update` only if the owner switches it on; the hub reads no repositories for apps.

**Not now, on purpose:** no plugin system and no code from the hub running in an app; no marketplace or catalogue; no remote shell, file access or container control in apps (the hub never updates or restarts an app); no generic UI language (four value types, forms, links; deeper pages are the app's own); no event bus or webhook fan-out; no shared database or shared cookie; no automatic discovery of apps on the network (an address is typed in); no more than one hub; no OpenAPI duty (the capability files are short prose).
