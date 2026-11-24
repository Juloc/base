# base

The shared base of the app family: every new app is built on it, so they all sign in, look, start and are configured the same way.
It is a private repository of source code that is **copied** into an app by a script; there is no package server.

What an app gets:

- **One container plus PostgreSQL**, a Compose file without variables, a setup assistant at the first start, settings in the app.
- **A safe sign-in:** password (Argon2id) with an authenticator app, recovery codes, sessions that survive an update, a list of devices, lockout, a history of the account.
- **People and roles:** a small list of roles the app names, one per person; the admin invites people by a link that works once, changes roles, blocks, deletes.
- **The app describes itself:** `/api/about` and `/api/health` (contract version 1) for a hub; optional **sign-in through the hub** (OpenID Connect) next to the own form: [docs/SIGN-IN-WITH-HUB.md](docs/SIGN-IN-WITH-HUB.md).
- **One look:** colours (light and dark, one accent variable per app), the shell with menu and person menu, every control, one icon file; phone first, no build step.
- **German and English**, numbered SQL migrations, `/api/health` with versions.
- **A rulebook** with numbered rules and the tests that check them: [docs/RULES.md](docs/RULES.md).

## Make an app

```bash
scripts/new-app.sh notes ../notes --accent 152
```

That writes a complete repository: the app's own project (`src/Notes/`, a copy of the start project under its new name), a copy of the base (`base/`),
Dockerfile, Compose file, the agent rules (`CLAUDE.md`) and a README. Then, in the new folder:

```bash
docker compose up -d --build
```

```bash
docker compose logs app
```

Open `http://localhost:8080`, enter the setup code from the log, create the first account. The app runs. Now replace the example feature
(a list of things to remember: list, detail, form, settings) by what the app is for, and keep the pattern. The parts of the interface
with their code are at `/base/catalog.html` of every running app.

`--accent` is the colour of the app as an angle on the colour wheel (0 to 359); `--title "..."` the name people see.

## Keep an app up to date

```bash
scripts/check-app.sh ../notes
```

shows whether the app's copy of the base was edited there, how far it is behind this repository, and a few rules it can check from the files.

```bash
scripts/sync-base.sh ../notes
```

renews the copy. Read [CHANGELOG.md](CHANGELOG.md) first; then build and look at the app.

## Work on the base

```bash
scripts/test.sh
```

runs the tests (it starts a throwaway PostgreSQL in Docker and removes it again; with `BASE_TEST_DB` set it uses that database).

```bash
docker compose -f samples/StartApp/docker-compose.yml up -d --build
```

starts the start app on `http://localhost:8080`. While working on the look, run it without Docker and with `BASE_UI_DIR` pointing at `ui/`, so a changed stylesheet needs no rebuild.

- The parts: [CLAUDE.md](CLAUDE.md) has the map of the code and the working rules.
- Decisions with their reasons: [docs/DECISIONS.md](docs/DECISIONS.md). Third-party code: [THIRD-PARTY.md](THIRD-PARTY.md).
- Drafts of the next steps (passkeys, the hub, the first real app): [docs/plans/](docs/plans/). The agreed design is `docs/BASE.md` in the JulOS repository.

## What is not here yet

Passkeys, the keyed calls between hub and app (stages 2 and 3 of the contract), a test of the sign-in against the real JulOS, mail, push, background jobs,
backup and restore, a test project for the made app. They come when an app needs them (see the changelog).
