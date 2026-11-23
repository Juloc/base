# base: working rules

The shared base of the app family: every new app is made from this repository. Short list; if a rule does not help, delete it.

1. **The rulebook is [docs/RULES.md](docs/RULES.md).** Rules have numbers (`AUTH-03`); cite them in comments and reviews. A change that touches a rule changes the rule, its test and the code in one commit.
2. **Into the base goes only what two apps need** (CODE-07). Build a thing in a real app first, then move it here. Later steps (passkeys, a hub sign-in, the AI client) are not built ahead.
3. **Own code, few packages** (CODE-02, CODE-03): Npgsql and Argon2, nothing else without changing the rule. Never write cryptography, password hashing, WebAuthn or TLS.
4. **No build step in the browser** (CODE-06), **plain SQL with numbered migrations** (DATA-01, DATA-02), **settings in the app** (OPS-03), **a Compose file without variables** (OPS-02).
5. **Nothing private in the repository or its history** (SEC-06): no domain names, real addresses, host names, keys, tokens, passwords or personal paths; examples use `192.0.2.x` and `example.com`. Look at `git diff` before every push.
6. **Never type a real password.** Test accounts use throwaway values on localhost only.
7. **Test sparingly, verify by running** (TEST-01, TEST-02): `scripts/test.sh`, then start the start app and look at it (375 px and wide, light and dark). Say what was not checked.
8. **Everything an app relies on is a promise.** Changing a file under `src/` or `ui/` changes every app at its next sync: bump `VERSION`, write the `CHANGELOG.md` entry, mark what breaks.
9. **Docs change with behaviour**, in the same commit. Small decisions go into [docs/DECISIONS.md](docs/DECISIONS.md), one sentence with its reason.
10. **Commits** are small with a clear subject. Push only to `main` of this repository, never force.

## Map

| Where | What |
|---|---|
| `src/Base.Core` | `Result`, `Error`, `Validator`, `Ids`, `PageRequest`/`Page`/`Cursor`, `ManualTime`. No dependency. |
| `src/Base.Data` | `Db` (SQL helpers, transactions), `Migrator` and `MigrationSet`, `Database` (data source, wait and migrate). |
| `src/Base.Accounts` | `AccountService` (setup, sign-in, account), `UserStore`, `SessionStore`, `AuditStore`, `PasswordHasher`, `Totp`, `RecoveryCodes`, `SecretBox`, `LoginLimiter`, `SetupCode`; `Migrations/`. |
| `src/Base.Web` | `BaseApp` (the four calls of an app: `AddBase`, `StartBaseAsync`, `UseBase`, `MapBase`), `SecurityHeaders`, `Auth/` (guard, cookie, endpoints), `Settings/` (rules, store, endpoints), `ProxyTrust`, `UiFiles` (serves `ui/` under `/base/`). |
| `ui/` | The look, no build: `tokens.css`, `base.css`, `base.js`, `icons.js`, `i18n.js` + `i18n/`, `auth.js` (setup, sign-in), `account.js` (settings cards), `qr.js`, `catalog.html` (the living catalogue). |
| `samples/StartApp` | The start project: a small real app (a list with detail, form, settings), its Dockerfile and Compose file. `new-app.sh` copies it. |
| `scripts/` | `new-app.sh` (make an app), `sync-base.sh` (renew an app's copy), `check-app.sh` (drift and file rules), `test.sh` (tests with a throwaway PostgreSQL). |
| `templates/` | `CLAUDE.app.md` (the rule file of every app) and `app/` (the other files `new-app.sh` writes). |
| `tests/Base.Tests` | Security and logic tests; `RuleFileTests`, `ContrastTests`, `RulebookTests` check rules from the files. |
| `docs/` | `RULES.md`, `DECISIONS.md`; `plans/` holds drafts of later steps (not agreed, not built). |

How an app uses the base: [README.md](README.md). The agreed design: `docs/BASE.md` in the JulOS repository.
