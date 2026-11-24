# Ideas (not agreed, not built)

Things found while apps moved onto the base. Nothing here is built until it gets a line in the changelog.

- **A settable name for the base's own tables beyond the migration table.** The first existing app had an unused table called `instance_settings` and renamed its own. An app that uses such a name could not. Possible: one prefix option for every table of the base.
- **The base sets JSON options for the whole app** (`null` values are left out). An existing app whose clients expect `null` fields had to write its old interface with options of its own. Possible: the base's options only for the base's own endpoints, or a documented way to keep the framework's defaults for a door.
- **A stricter analysis level in the base.** An app that builds with `latest-recommended` and warnings as errors finds 49 places in the base (mostly logging through `LoggerMessage`, the position of `CancellationToken` parameters, two names). The app now holds the copy to the base's own level; raising the base's level is a separate decision.
- **A brake on wrong keys at a door.** A door's check is the app's own; a shared limiter for wrong keys per address would save every app from writing one.
- **Backup and restore** with a recovery key the owner sees once and keeps outside: the server holds only what it needs to encrypt. Until then an app with backups keeps its passphrase outside the database.
- **Two administrators demoting each other in the same moment.** The last-administrator check and the change are one statement, but two such statements running at once can each still see two administrators. The window is tiny; a row lock on the administrators would close it.
- **The bottom bar on the phone lets the list shine through** (seen in the first app in dark mode: rows stay readable behind the labels of the bar). The bar is glass; a stronger fill for `.shell-tabs` would fix it for every app.
