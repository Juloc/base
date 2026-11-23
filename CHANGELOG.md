# Changelog

The base follows SemVer; the version stands in `VERSION` (OPS-05). An app carries the version of its copy in `base/VERSION`;
`scripts/check-app.sh` shows how far a copy is behind, `scripts/sync-base.sh` brings it up to date. Before 1.0.0 a minor version may change
what apps rely on; such changes are marked **Breaking**.

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
