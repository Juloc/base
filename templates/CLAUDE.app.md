# {{TITLE}}: working rules

Built on the shared base of the app family. base version: **{{BASE_VERSION}}** (the copy in `base/`).

1. **The rulebook is [base/RULES.md](base/RULES.md).** Every rule there applies here; cite the numbers (`AUTH-03`) in comments and reviews. A SHOULD rule that this app does not follow is written down with its reason in `docs/DECISIONS.md`.
2. **`base/` is a copy and is never edited here** (CODE-07). A change to it goes into the base repository and comes back with its `scripts/sync-base.sh`; `scripts/check-app.sh` shows drift. After a sync, the version above changes with it.
3. **Before building a part of the interface, look at the catalogue** (`/base/catalog.html`, KIT-02) and use what is there: `shell()`, `field()`, `openDialog()`, `toast()`, `emptyState()`, icons through `icon()`. Something missing that a second app would need too belongs into the base, not here.
4. **The colour of this app is one variable** (COLOR-01): `--accent-hue: {{ACCENT}}` in `src/{{NS}}/wwwroot/app.css`. No colour values anywhere else.
5. **Every text goes through `t()`** in German and English (I18N-01); the server answers failures with a code that has a text (I18N-02).
6. **The server:** endpoints under `/api` are behind the guard (SEC-03, SEC-04); take the person from `ctx.Person()`, check input with a `Validator` (FORM-02), name the owner in every SQL statement (DATA-03), return `Result` or `Error` (CODE-04). Schema changes are a new numbered script in `Migrations/` (DATA-02). A new setting gets a `SettingRule` (OPS-03). An endpoint that needs a role is mapped with `.RequireRole("admin")` (SEC-03). What the app is (name, description, icon, roles, `AccentHue` equal to `--accent-hue`) stands once in the `BaseAppOptions` of `Program.cs`; `/api/about` is made from it (CONTRACT-02).
7. **No new packages, no build step in the browser, no variables in the Compose file** (CODE-02, CODE-06, OPS-02).
8. **Nothing private in the repository** (SEC-06). **Never type a real password**; test accounts are throwaway values on localhost.
9. **Verify by running** (TEST-02): `docker compose up -d --build`, then look at the change at 375 px and wide, light and dark. Say what was not checked.
10. **Docs change with behaviour.** Commits are small with a clear subject.

## Map

| Where | What |
|---|---|
| `src/{{NS}}/Program.cs` | Start: `AddBase`, `StartBaseAsync`, `UseBase`, `MapBase`, then the app's own services and endpoints. |
| `src/{{NS}}/Items/` | The example feature (store and endpoints). Replace it by what the app is for; keep the pattern. |
| `src/{{NS}}/Migrations/` | The app's numbered SQL scripts (`NNN_name.sql`). |
| `src/{{NS}}/wwwroot/` | `index.html`, `app.css` (the accent), `app.js` (pages), `i18n/` (texts), the PWA files. |
| `base/` | The copy of the base: C# modules (`src/Base.*`), the look (`ui/`), `RULES.md`, `VERSION`, `MANIFEST`. |
| `Dockerfile`, `docker-compose.yml` | One container plus PostgreSQL, one volume (OPS-01). |
