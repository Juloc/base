# Trial: the start app signs in through a local JulOS

Date of the run: 2026-11-24. Base 0.2.0 (the fixes below make 0.2.1), JulOS 0.26.0 (the image of its `main`, one commit before the release commit; the
sign-in centre `docs/SIGN-IN.md` is the same). Everything ran on one machine, only on `127.0.0.1`; nothing of the live JulOS was touched. This is the first time the two
sides met: the base had only been tested against its own small test hub, JulOS only against Microsoft's client in its own tests.

Result in one line: **the flow works** (sign-in with and without a hub session, roles, role change, logout both ways, restart of either side). Two real mismatches were
found (sign-out from the app, and the "dead address" when the hub is away); the base was fixed for both. One bug in JulOS remains (the sign-out confirmation page fails in
Chromium) and three smaller points are listed for JulOS.

## 1. Setup, to repeat (no private data; ports and names are examples)

**The network problem, and the solution.** The app calls the hub itself (description, keys, token) and the hub calls the app (back-channel logout). The browser, the app
and the hub must all use the SAME address for each of them: the hub's address is the issuer in every token (and the app compares it), and the app's address is the return
address that the hub compares exactly. `localhost` does not work for that in containers (inside a container `localhost` is the container). Solution that needs nothing on the
host: use names under `.localhost` (they are accepted as "this machine" by both sides: http is allowed, and every browser and glibc resolve `*.localhost` to 127.0.0.1) and
give each service **the same name as a network alias** on one shared Docker network, **listening on the same port inside as it is published outside**. So `http://julos.localhost:18390`
is the same address from the browser (published port on 127.0.0.1) and from the app container (alias on the shared network, port 18390 inside).

1. `docker network create hubtrial-net`
2. JulOS: a Compose file of its own project (`name: hubtrial-julos`), PostgreSQL plus the JulOS image (built from the JulOS repository's `Dockerfile`; the dev Compose file in
   `deploy/dev/` shows the rest), with throwaway database password and `JULOS_SECRET_KEY` in an env file outside the repository, and:
   ```yaml
   julos:
     environment:
       ASPNETCORE_HTTP_PORTS: "18390"                 # the same number inside as outside
       JULOS_PUBLIC_URL: http://julos.localhost:18390 # the issuer; http is allowed for localhost names only
     ports: ["127.0.0.1:18390:18390"]
     networks: { hubtrial-net: { aliases: [julos.localhost] } }
   ```
   Start it, read the setup code from the log (`... logs julos | grep Setup-Code`), do the first setup (browser, or `POST /api/setup/begin` and `/finish`; the TOTP code is computed from the
   secret with a few lines of Python). More people: `docker compose exec -it julos dotnet JulOS.dll user add NAME` (needs a terminal).
3. The start app, with an override file next to the sample Compose file (`docker compose -p hubtrial-app -f samples/StartApp/docker-compose.yml -f override.yaml up -d --build`):
   ```yaml
   services:
     app:
       environment: { ASPNETCORE_HTTP_PORTS: "18391" }
       ports: !override ["127.0.0.1:18391:18391"]
       healthcheck: { disable: true }                   # the sample's check asks port 8080
       networks: { default: {}, hubtrial-net: { aliases: [startapp.localhost] } }
   networks: { hubtrial-net: { external: true } }
   ```
   Open `http://startapp.localhost:18391`, do the first setup (code in the log of `app`).
4. In JulOS (settings, security, sign-in centre; every write wants a fresh authenticator code; or `POST /api/security/oidc/apps`) add the app:
   return address `http://startapp.localhost:18391/api/auth/hub/callback`, address after sign-out `http://startapp.localhost:18391/`, back-channel address
   `http://startapp.localhost:18391/api/auth/hub/backchannel-logout`, **tick "App on the local network"** (the name resolves to the Docker network, which JulOS refuses otherwise),
   roles `admin, member, viewer` (from `/api/about` of the app). Copy the client id and the secret (shown once). Give the second person a role (`PUT /api/security/oidc/apps/{id}/roles/{user}`).
5. In the start app (settings, "Central sign-in", as admin): address `http://julos.localhost:18390`, client id, secret, switch on, "Check connection" (answer: the hub's name, here the host name).
6. Teardown: `docker compose -p hubtrial-app down -v`, `docker compose -p hubtrial-julos down -v`, `docker network rm hubtrial-net`, delete the env file.

## 2. The cases

Browser = the built-in browser pane (one profile); "curl" = scripted flow with two cookie jars (what a second profile is).

| | Case | Result |
|---|---|---|
| a | Sign in with an existing hub session | **Works.** No form, straight into the app, right person (created on first sight, named after `preferred_username`), right role (`admin` for the owner). Browser. |
| b | Without a hub session | **Works.** The hub shows its own sign-in ("Anmelden bei JulOS, für Start App"), after it the browser lands in the app. The `SameSite=Strict` bounce of the hub (`jb=1`) is invisible. Browser. |
| c | Person without a role for the app | **Works.** The hub shows "Kein Zugang ... hat in „Start App“ keine Rolle. Bitte den Eigentümer von JulOS um Zugang." (an error page, no redirect). The app never sees anything. curl. |
| d | Role changed in the hub | **Works.** The hub queues a logout token for the role change: "Back-channel logout: 1 session(s) ended" within about 5 s; the next sign-in has the new role (member to viewer). The owner's role cannot be changed (`owner`: "Der Eigentümer hat in jeder App admin"). curl. |
| e | Sign out in the app | **Was broken, fixed in the base** (see 4). Before: the app went to `end-session?client_id=...`, the hub asked "Bei JulOS abmelden?" and the confirmation failed (JulOS bug, 3.1); even without that bug nothing led back to the app. After: `id_token_hint` and `post_logout_redirect_uri` are sent, the hub signs out without asking and the browser returns to the app's sign-in page; the hub session is really gone (the next hub button shows the hub's form). Browser. |
| f | Sign out at the hub (API logout, same as removing the device) | **Works.** The hub posts the logout token, the app logs "Back-channel logout: 1 session(s) ended", the app session is gone on the next request. Reached through the `.localhost` alias with the "local network" tick. Browser. |
| g | Hub stopped | **Works, with one fix.** The own form works, health says `degraded`. With a cold cache the button says "Die Zentrale ist nicht erreichbar. Das eigene Formular geht weiter." (390 px checked). With a WARM cache (hub went away within ten minutes of the last use) the old code still answered the begin call with the hub's address (seen with curl), so a browser would have gone to a dead address: fixed, the begin call now asks the hub afresh. "Check connection" says "cannot be reached (nothing answers at that address)". |
| h | App container restarted | **Works.** Sessions (local and hub) stay (they are in the database; the key file is on the volume). curl and browser. |
| i | JulOS restarted | **Works.** Its sessions stay, the signing keys stay (`/oidc/jwks` the same key), the next sign-in through the hub works at once, health back to `ok`. |
| j | Errors | **Clean on both sides.** Wrong secret: the hub answers 401 at the token endpoint, the app shows "Signing in through the centre failed" and logs `The hub refused the code (401)`; "Check connection" cannot know (it does not need the secret). Return address not registered (called as `127.0.0.1:18391`): the hub shows its neutral error page (400), no redirect. Code expired (66 s): the hub refuses (400), the app shows the failure text. Same callback twice, or the code replayed in another browser: `hub.state` (the flow is gone), no session. Not reached: the hub's own "second use of a code revokes the grants" (needs the PKCE verifier of a live flow; covered by JulOS tests). |
| k | Two people at the same time | **Works** (two cookie jars: owner as `admin`, member as `member`/`viewer`, separate sessions, separate hub sessions; logging out one did not touch the other). A second real browser profile was not available in the pane. |
| l | 390 x 844 | **Looks right**: sign-in page with the hub button, the failure banner (hub error and "not reachable"), the settings card "Zentrale" with the form, the three addresses and the role list, the hub's sign-in page. Light mode only; the hub's "no access" and "sign out" pages were read as text and (sign-out) seen on a wide screen. |

## 3. Differences between the base and JulOS

Names, formats and times that were compared; all fit unless a line says otherwise.

| Thing | Base | JulOS | Fits |
|---|---|---|---|
| Discovery | `/.well-known/openid-configuration`, `issuer` must equal the entered address, endpoints on the same origin | issuer from the public address setting, same origin | yes |
| Scope | `openid profile` | `profile` only adds the name claims | yes |
| Claims | `sub`, `sid`, `nonce`, `aud`, `iss`, `exp`, `role`, name from `preferred_username` (else `name`) | exactly these (`amr`, `auth_time` unused) | yes |
| Signature | ES256 only | ES256, `kid`, `/oidc/jwks` | yes |
| Client authentication | `client_secret_basic` | accepts basic and post | yes |
| Response mode | query, `iss` checked if present | query, `iss` always present (RFC 9207) | yes |
| Logout token | form field `logout_token`, `typ`, `events`, `jti`, no `nonce`, at most 5 min old | the same, `exp` 2 min | yes |
| Logout address | `/api/auth/hub/backchannel-logout`, answers 200 | `https` only, **http allowed only while the issuer is `http://localhost`-like**; private addresses need the "local network" tick per app | yes, but it must be known (section 5) |
| Sign-out | was: `end-session?client_id=` only | redirect back to the app only with a valid `id_token_hint` and an exact registered address | **was a mismatch**, fixed (4) |
| Sign-out confirmation | n/a | form POST that checks `Origin` against the host | **JulOS bug** (3.1) |
| Name for the button | asks `GET /api/about` of the hub, anonymously | `/api/about` needs a session | falls back to the host name (3.2) |
| Return address | built from the address the browser used | compared exactly | yes (behind a proxy: trusted proxies) |
| Time | one minute of clock skew allowed, token 5 min | 5 min | yes |
| Roles | one per person, from `role`; unknown role = no access | one per app and person; owner is `admin` and cannot change | yes |

## 4. What was fixed in the base

- **Sign-out from the app now returns to the app (AUTH-13, AUTH-14).** The base keeps the ID token of the hub on the session as the hint for the hub's sign-out (migration `accounts/003_hub_hint`,
  column `sessions.hub_hint`, gone with the session), and the sign-out address now carries `id_token_hint` and `post_logout_redirect_uri` (the app's own address `/`). The hub then signs out
  without asking (its rule: a hint that belongs to this very session) and sends the person back. The "address after sign-out" must be registered at the hub; the settings card shows it.
  Without it (or with an old hub) the old behaviour remains: the hub asks. This changes the sentence "a token of the hub is kept nowhere"; the rule now says so.
- **The sign-in button asks the hub afresh (AUTH-13).** A hub that went away is now said in words instead of a browser error page.
- **Unreachable errors name the reason** ("nothing answers at that address", "no answer in time", ...) and no longer the name of an exception type.
- Tests: `Signing_out_in_the_app_goes_on_to_the_sign_out_of_the_hub_for_its_people_only` (hint and return address), the hub-away test (begin says `hub.unreachable` in words),
  the sign-in test (the ID token is on the session).

## 5. What is to be fixed in JulOS (nothing was changed there)

1. **The sign-out confirmation always fails in Chromium (bug).** `src/JulOS.Frontend/Program.cs:204` sets `Referrer-Policy: no-referrer` on every answer; with that policy Chromium sends
   `Origin: null` on a same-origin form POST (checked with a tiny test page). `OidcEndpoints.cs:206` then refuses the POST (`ApiGuard.SameOrigin`, `Auth/ApiGuard.cs:71`, compares the origin with the host), so the
   page "Bei JulOS abmelden?" ends in "Anmeldung nicht möglich" (400) and the hub session is NOT ended. Input: any `GET /oidc/end-session` without a matching `id_token_hint`, then the button.
   Consequence: every app that sends no hint (or an old one) leaves the person signed in at the hub. Suggestion: `Referrer-Policy: same-origin` on the `/oidc/*` pages (or for the whole server), or accept
   `Origin: null` together with `Sec-Fetch-Site: same-origin`.
2. **`/api/about` is not anonymous** (`About/AboutEndpoints.cs:17`, "braucht eine Sitzung"), but the base reads the hub's name for the button from it (anonymously). So the button says "Mit julos.localhost
   anmelden". Suggestion: publish name only (`{"name":"JulOS"}`) anonymously, or tell the base another place; the base can use any small public document.
3. **No second person without a terminal.** The only way to add a person is `JulOS.dll user add NAME` in a terminal (password typed); the sign-in centre then assigns roles. Fine for one family, but a trial or a script needs a pty;
   an invitation like the base's would help. (Not a bug.)
4. **The page after a sign-out without a hint** (`SignedOut`) has only "Zu JulOS"; with a registered "address after sign-out" and no hint the person never gets back to the app. The base now always sends the hint, other apps may not.
5. **The docs say** the back-channel address is "https only (http only while the public address of JulOS is itself `http://localhost`)": a `*.localhost` public address works as well (it did here), worth one sentence.

## 6. What a person setting this up must know

- Both addresses must be the same for the browser, for the app and for the hub (section 1). On a real machine this is just two `https://` host names; the trouble is only on one machine with containers.
- At the hub, register all three addresses of the settings card **exactly** (return, after sign-out, back-channel) and tick "App on the local network" when the app is behind the same router (almost always).
- The secret is shown once; "Check connection" cannot test it (the hub only asks for it when a person signs in).
- Roles: the keys must be the app's keys; the hub owner is always `admin`; a person without an entry gets the hub's "Kein Zugang" page.
- A hub that stays away does not lock anybody out: the form works, health says `degraded`.

## 7. Not checked

A second real browser profile (two cookie jars stood in); the hub's own signing-key rotation with a running app; the replay revocation of a code at the token endpoint; real `https` and a reverse proxy; Firefox and Safari
(the `Origin: null` finding is Chromium; the others were not tried); dark mode of the hub's pages; the base's `HubSignIn` against a hub under load.
