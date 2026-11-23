# Third-party code

What runs in an app besides our own code and Microsoft's .NET and ASP.NET Core (MIT):

| What | Used for | Licence |
|---|---|---|
| Npgsql | the PostgreSQL driver | PostgreSQL License |
| Konscious.Security.Cryptography.Argon2 | the password hash (Argon2id) | MIT |

In the browser there is no third-party code at all: no framework, no library, no font, nothing from another server.

The icons (`ui/icons.js`) are own drawings, taken from JulOS and extended in the same style. The older everyday ones (folder, cloud, globe, shield ...)
follow the style of the Feather set by Cole Bemis (MIT License); where they match it, its MIT licence applies.

The QR code generator (`ui/qr.js`) and the sign-in code under `src/Base.Accounts` were taken from JulOS (same owner, MIT).

Tests only: xunit.v3 (Apache-2.0), Microsoft.AspNetCore.Mvc.Testing (MIT).
