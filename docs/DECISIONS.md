# Decisions

Small decisions made while building, one sentence each with its reason. Newest last. The big ones are in `docs/BASE.md` of the JulOS repository.

## 0.1.0 (2026-11-23)

1. **English everywhere in the repository** (docs, comments, log lines, error messages of the server); German and English only in what a person sees: one language for the people and agents who read code, as in the JulOS docs.
2. **An app carries the base in a folder `base/`** (C# modules, `ui/`, rulebook, `VERSION`, `MANIFEST`) and references the projects there: one folder that is plainly "not ours to edit", and a checksum per file makes drift visible.
3. **The base modules keep their names (`Base.Core` ...) in every app; only the start project is renamed:** a copy that is byte for byte the base can be compared and renewed by a script.
4. **The look is embedded in `Base.Web` and served under `/base/`:** an app has exactly the look of the base version it was built with, and nothing has to be copied into its `wwwroot`.
5. **Migrations come in sets (`accounts`, `web`, `app`), applied set after set:** each module owns its tables, and an app's numbers never collide with those of the base.
6. **An applied migration is protected by a checksum:** the rule "never edit an applied script" fails loudly at start instead of silently producing two different schemas.
7. **The server answers failures with a code, the browser translates it:** the server does not need to know the language of the reader, and a test can check that every code has a text.
8. **The key for encrypted values is required** (a file in the data folder, made at the first start), unlike JulOS where it was optional: an app without it would store the authenticator secret in plain text.
9. **The Compose file uses one volume with three sub-folders** (database, the server's key, the database password) and a one-shot init step from the PostgreSQL image: one thing to back up, no variables, and a random database password instead of one written into the file. It needs Docker Engine 26 or newer (volume sub-paths).
10. **The database password is random per installation although the database is not published:** it costs one init step and removes the only secret-like value a Compose file without variables would otherwise contain.
11. **The session cookie is named after the app (`<id>_sid`):** cookies do not know ports, so two apps on one host name would otherwise sign each other out.
12. **Lockout counters and a requested-but-unconfirmed authenticator live in memory:** a restart only clears waits of at most 15 minutes, and no table is written on every failed attempt. Sessions themselves are in PostgreSQL.
13. **The first account gets the role `admin`; roles are a `text[]` on the person:** enough to guard the settings of the installation now, and open for roles per app later.
14. **Trusted proxies default to "every private network" until set in the app,** as in JulOS: an app behind a reverse proxy on the same host works without setup; the settings page says how to restrict it.
15. **`/api/health` answers 503 when the database does not answer** (JulOS answered 200 with `ok: false`): proxies and monitors read the status code.
16. **JSON of the base's endpoints is source-generated, an app's own types use reflection:** the base stays fast and explicit, and an app author does not have to register every type.
17. **Colours are `oklch()` with fixed lightness per role and the hue from one variable (`--accent-hue`), light and dark through `light-dark()`:** contrast is decided by lightness, so it can be tested once for all 360 hues; it needs browsers from 2024 on.
18. **The contrast test takes the worst of three renderings** (wide gamut as it is, clipped to sRGB, chroma lowered): browsers differ in how they show a colour outside sRGB.
19. **Controls are 44 px high by default and 36 px only on a wide screen that is not driven by a finger** (`not (pointer: coarse)`): the phone is the default, and a tablet keeps finger sizes.
20. **A dialog is a non-modal `<dialog>` (`show()`), movable on a wide screen, a sheet on a phone,** also for confirmations: the owner wants dialogs that never block the page.
21. **`base.js` contains a tiny hash router and the shell:** every app needs both, and without them each app would grow its own.
22. **The service worker keeps what was fetched as a fallback and has no list of files:** nothing to maintain, updates arrive at once, answers of the API are never kept.
23. **App icons are SVG only for now** (no PNG sizes): enough for installing on Android and desktop; iOS wants a PNG, which comes with the first real app.
24. **Step-up (a fresh second factor for dangerous actions) was not taken over from JulOS yet:** its policy table is specific to JulOS; it returns when the second app needs it.
25. **The rulebook has 50 rules and one level word per rule (MUST, SHOULD, MAY for Muss, Soll, Kann):** only rules the base keeps today, most with a test.
26. **`scripts/sync-base.sh` exists although only `new-app.sh` and `check-app.sh` were asked for:** it is the same copy function, and a drift check without a way to renew the copy would be half a tool.
27. **The made app has no test project of its own yet:** the tests of the base cover the base; what an app must test of its own is decided with the first real app.
