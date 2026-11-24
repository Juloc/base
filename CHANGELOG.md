# Changelog

The base follows SemVer; the version stands in `VERSION` (OPS-05). An app carries the version of its copy in `base/VERSION`;
`scripts/check-app.sh` shows how far a copy is behind, `scripts/sync-base.sh` brings it up to date. Before 1.0.0 a minor version may change
what apps rely on; such changes are marked **Breaking**.

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
