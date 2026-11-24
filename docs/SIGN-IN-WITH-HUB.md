# Sign in through the hub

An app on the base can let people sign in with the central sign-in of the family (the hub, JulOS) **in addition to** its own form. The app keeps its own sessions and its own form; if the hub is down, the form still works (CODE-01). The rules are AUTH-13 and AUTH-14 of [RULES.md](RULES.md); the hub's side is `docs/SIGN-IN.md` in the JulOS repository.

## What happens

1. The person taps "Sign in with *name of the hub*" on the sign-in page. The page asks the app (`POST /api/auth/hub/begin`), which makes a random `state`, `nonce` and PKCE verifier, stores them for ten minutes, sets a cookie that ties the flow to this browser, and answers with the address of the hub's authorize endpoint.
2. The hub signs the person in (its own password and second factor) and sends the browser back to `/api/auth/hub/callback?code=...&state=...`.
3. The app takes the flow (once), checks that the cookie and the state belong together, exchanges the code (client secret and PKCE verifier) and checks the ID token: signature from the hub's published keys (ES256 only), issuer, audience (this app's client id), lifetime, `nonce`, and that `sub` and `sid` are there.
4. It finds the person by `sub` (a new person is made the first time, named after the claim) and reads the role from the claim `role`. A role this app does not list means **no access**: nobody is made, no session is given. Otherwise the app makes **its own session**, exactly as after a password, and writes the `sub` and the `sid` on it. No token of the hub is kept.
5. **Sign out everywhere:** when the person's session at the hub ends (for whatever reason) the hub posts a signed logout token to `/api/auth/hub/backchannel-logout`; the app ends the sessions that carry that `sid`. When the person signs out in the app, the app goes on to the hub's `end-session`, so the hub session ends and the other apps hear of it.

A person of the hub is **never joined to a local account by name or mail**: a hub person called like a local one becomes a second person with a numbered name. They have no password here. Their role is read again at every sign-in and cannot be changed in the app's list of people (the list shows where the person comes from). They can be blocked or deleted here; a blocked person gets no session from the hub.

## What to enter in the hub

Per app, in the hub (settings, security, the sign-in centre, only the owner):

| Field | Value |
|---|---|
| Name | what people see |
| Return address | `https://<address of the app>/api/auth/hub/callback` (exactly; the app shows it in its settings card) |
| Address after sign-out | optional; the app sends no `id_token_hint` because it keeps no token, so the hub asks the person first |
| Back-channel logout address | `https://<address of the app>/api/auth/hub/backchannel-logout` (the settings card shows it); the hub posts only to `https`, and refuses addresses of the local network unless the app is ticked as "on the local network" |
| Roles | the keys of the roles of the app: the list is in the settings card and in `/api/about` (`admin`, `member`, ...) |

The hub then shows the **client id** and a **secret once**. A role the app does not know gives no access, so give every person a role from this list.

## What to enter in the app

Settings, **Central sign-in** (only the role `admin`):

- **Address of the hub**: `https://` and no path (`http://localhost:...` only for development on one machine). The secret travels to this address, so nothing else is accepted.
- **Client id** and **secret** from the hub. The secret is stored encrypted with the key of the app (`data/secret.key`), sent only to the hub, and never shown again; leave the field empty to keep it.
- **Check connection** reads the description of the hub (`/.well-known/openid-configuration`, its keys) and, if the hub has an anonymous `/api/about`, its name for the button; otherwise the button carries the name of the host. It also shows an error in words.
- **On/off**: only when address, client id and secret are all there. Off: no button, no flow; a logout from the hub still ends sessions.

Everything takes effect at once, without a restart. Nothing of this is in an environment variable or the Compose file (OPS-02, OPS-03).

## Things to know

- **Time:** the check allows a minute of difference between the clocks; a token is five minutes old at most.
- **A key change at the hub:** the app reads the hub's keys again when it meets an unknown key (at most once a minute).
- **The hub's address from the app's machine:** the app calls the hub itself (description, keys, token). It must be able to reach the address that is entered. Links to link-local addresses are refused.
- **The app's own address:** the return address is built from the address the browser used. Behind a reverse proxy set the trusted proxies (settings, installation), otherwise the app builds `http://` or an inner host name and the hub refuses the return address.
- **Health:** when the app cannot reach the hub during a sign-in, `/api/health` says `degraded` until the next success. Nothing else changes.
- **Not done:** silent sign-in (`prompt=none`), refresh tokens, a second factor in the app for dangerous actions of hub people (the hub's `amr` and `auth_time` are not used yet), joining a hub person to a local account.

## Checked and not checked

The tests (`tests/Base.Tests/HubSignInTests.cs`) run the whole flow against a small hub in the test project that signs with a test key: success, wrong `state`, missing flow cookie, repeated callback, wrong `nonce`, issuer, audience, expired, wrong key, `none`, HS256, unknown role, unreachable hub, logout token (valid, wrong signature, wrong audience, expired, repeated, ID token instead of logout token). **Not checked automatically: a sign-in against the real JulOS.** That needs a running JulOS with an app registered; do it by hand once (see "Open points" in the changelog of the base).
