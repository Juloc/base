/* =====================================================================
 * The door of every app, the same everywhere: the setup assistant at the first start and the sign-in after that.
 *   start({ icon, onSignedIn })   asks the server who is there and shows the assistant, the sign-in, or hands over to the app.
 * The server decides everything; this file only draws (AUTH-08: the sign-in never says which part was wrong).
 * ===================================================================== */
import { t, hasKey, getLang } from './i18n.js';
import { h, icon, svg, api, field, passwordField, submit, notice, prefs, markSignedIn, copyText, errorText, ApiError, toast } from './base.js';
import { authCard, stateCard, mountAuth } from './gate.js';
import { qrSvg } from './qr.js';
import { roleName } from './people.js';

/**
 * Starts the app. onSignedIn(me) is called with { user, roles, app: { id, name, version, base } } once somebody is signed in.
 * icon: the icon of the app (a name from icons.js), shown on the card. line: the one line under the title (default: the description of the app, from /api/about).
 */
export async function start({ icon: appIcon = 'grid', line, onSignedIn }) {
  let me;
  try { me = await api('GET', '/api/auth/me'); }
  catch (e) {
    mountAuth(stateCard({ icon: 'cloud', tone: 'danger', title: t('err.title'), text: errorText(e), actions: [{ label: t('common.retry'), icon: 'refresh', run: () => location.reload() }], foot: true }));
    return;
  }
  document.title = me.app.name;
  const enter = async () => {
    markSignedIn();
    try { if (await prefs.load()) { location.reload(); return; } } catch { /* the settings can wait */ }
    document.body.replaceChildren();
    onSignedIn(await api('GET', '/api/auth/me'));
  };
  if (me.user) { markSignedIn(); try { if (await prefs.load()) { location.reload(); return; } } catch { /* the settings can wait */ } onSignedIn(me); return; }
  const text = line ?? await describe();
  // every page of the door is one card: the mark and the name of the app, one line, then what the page needs
  const page = (opts) => { const card = authCard({ icon: appIcon, line: text, ...opts }); mountAuth(card); return card; };
  // an invitation link carries its token in the fragment (#invite=...), so it never reaches a server log
  const invited = /^#invite=([A-Za-z0-9_-]{20,100})$/.exec(location.hash);
  if (invited && !me.setupNeeded) invite(me, invited[1], page, enter);
  else if (me.setupNeeded) setup(me.app, page, enter);
  else signIn(me, page, enter);
}

/** The line under the title: what the app is, in the person's language (the anonymous /api/about); nothing when that cannot be read. */
async function describe() {
  try {
    const about = await api('GET', '/api/about');
    const d = about.description || {};
    return (getLang() === 'de' ? d.de : d.en) || d.en || about.name || '';
  } catch { return ''; }
}

/** A message at the top of a card, under the head. */
const problem = (card, text) => { card.querySelector('.auth-note')?.remove(); card.querySelector('.auth-head').after(h('div', { class: 'auth-note' }, notice(text, 'danger'))); };

/** The sign-in: the hub on top when there is one, then the own account. The main button is soft, the code field opens the number keyboard. */
function signIn(me, page, enter) {
  // the central sign-in sends the browser back to "/?hub_error=<code>" when it did not work: the code has a text, nothing else is shown
  const params = new URLSearchParams(location.search);
  const hubError = params.get('hub_error');
  if (hubError) history.replaceState(null, '', location.pathname + location.hash);
  const code = field({ label: t('auth.code'), name: 'code', placeholder: t('auth.codePlaceholder'), labelHidden: true, class: 'input code', inputmode: 'numeric', autocomplete: 'one-time-code', autocapitalize: 'none', spellcheck: 'false', required: true });
  // a recovery code has letters: one quiet button turns the number keyboard into the normal one
  const input = code.querySelector('input');
  const letters = h('button', { class: 'quiet', type: 'button', text: t('auth.useRecovery') });
  letters.addEventListener('click', () => {
    const on = input.inputMode === 'numeric';
    input.inputMode = on ? 'text' : 'numeric';
    input.placeholder = t(on ? 'auth.recoveryPlaceholder' : 'auth.codePlaceholder');
    letters.textContent = t(on ? 'auth.useApp' : 'auth.useRecovery');
    input.focus();
  });
  const form = submit(h('form', { class: 'auth-form' },
    field({ label: t('auth.user'), name: 'user', placeholder: t('auth.user'), labelHidden: true, autocomplete: 'username', autocapitalize: 'none', spellcheck: 'false', required: true }),
    passwordField({ label: t('auth.password'), name: 'password', placeholder: t('auth.password'), labelHidden: true, autocomplete: 'current-password', required: true }),
    code, h('div', { class: 'auth-aside' }, letters),
    h('button', { class: 'btn primary wide', type: 'submit', text: t('auth.signIn') })),
  async (v) => {
    const r = await api('POST', '/api/auth/login', v);
    await enter();
    if (r.recoveryLeft <= 3) toast(t('auth.recoveryLow', { n: r.recoveryLeft }), { kind: 'danger' });
  });
  const body = [form];
  let card;
  if (me.hub) {
    const go = h('button', { class: 'btn neutral wide', type: 'button' }, icon('globe'), t('auth.hub', { name: me.hub.name }));
    go.addEventListener('click', async () => {
      go.disabled = true;
      try { const r = await api('POST', '/api/auth/hub/begin'); go.replaceChildren(t('auth.hubWait', { name: me.hub.name })); location.assign(r.url); }
      catch (e) { go.disabled = false; problem(card, errorText(e)); }
    });
    body.unshift(go, h('div', { class: 'auth-or', role: 'separator', text: t('auth.or') }));
  }
  card = page({ title: t('auth.title'), body });
  if (hubError && hasKey('err.' + hubError)) problem(card, t('err.' + hubError));
  form.querySelector('input').focus();
}

/** The person invited by an admin: choose a password, set up the authenticator, keep the recovery codes. The link works once. */
function invite(me, token, page, enter) {
  let begun = null;
  api('POST', '/api/invite/check', { token }).then((who) => one(who), (e) => {
    page({ title: t('invite.title'), body: [notice(errorText(e), 'danger'), h('a', { class: 'btn wide', href: '/', text: t('auth.signIn') })] });
  });

  function one(who) {
    const form = submit(h('form', { class: 'auth-form' },
      field({ label: t('auth.password'), name: 'password', type: 'password', placeholder: t('invite.passwordPlaceholder'), labelHidden: true, autocomplete: 'new-password', required: true }),
      h('button', { class: 'btn primary wide', type: 'submit', text: t('common.next') })),
    async (v) => { begun = await api('POST', '/api/invite/begin', { token, password: v.password }); two(who); });
    page({ title: t('invite.title'), line: t('invite.intro', { user: who.user, app: me.app.name, role: roleName(me, who.role) }), step: { n: 1, of: 3 }, body: form });
    form.querySelector('input').focus();
  }

  function two(who) {
    const form = submit(h('form', { class: 'auth-form' },
      authenticator(begun.uri, begun.secret),
      field({ label: t('setup.totpCode'), name: 'totp', placeholder: t('setup.totpCode'), labelHidden: true, class: 'input code', inputmode: 'numeric', autocomplete: 'one-time-code', required: true }),
      h('div', { class: 'form-actions' },
        h('button', { class: 'btn ghost', type: 'button', text: t('common.back'), onclick: () => one(who) }),
        h('button', { class: 'btn primary', type: 'submit', text: t('common.next') }))),
    async (v) => {
      try { await api('POST', '/api/invite/finish', { token, totp: v.totp }); }
      catch (e) { if (e instanceof ApiError && e.code === 'totp.wrong') e.fields = { totp: 'totp.wrong' }; throw e; }
      history.replaceState(null, '', location.pathname);          // the link is used up: it does not stay in the address bar
      three();
    });
    page({ title: t('setup.totpTitle'), line: t('setup.totpIntro'), step: { n: 2, of: 3 }, body: form });
    form.querySelector('input').focus();
  }

  function three() {
    page({ title: t('setup.recoveryTitle'), line: t('setup.recoveryIntro'), step: { n: 3, of: 3 }, body: finish(begun.recovery, enter) });
  }
}

/** The last step of both assistants: the codes, "I have saved them", and the button that goes in. */
function finish(codes, enter) {
  const ok = h('input', { type: 'checkbox' });
  const go = h('button', { class: 'btn primary wide', type: 'button', text: t('setup.finish'), disabled: true, onclick: enter });
  ok.addEventListener('change', () => { go.disabled = !ok.checked; });
  return h('div', { class: 'auth-form' }, recoveryCodes(codes), h('label', { class: 'check' }, ok, h('span', { text: t('setup.recoverySaved') })), go);
}

/** The setup assistant: 1 the first account (with the code from the server log), 2 the authenticator, 3 the recovery codes. */
function setup(app, page, enter) {
  let first = null;

  function one() {
    const form = submit(h('form', { class: 'auth-form' },
      field({ label: t('setup.code'), name: 'code', placeholder: t('setup.codePlaceholder'), labelHidden: true, autocomplete: 'off', autocapitalize: 'characters', spellcheck: 'false', required: true }),
      field({ label: t('auth.user'), name: 'user', placeholder: t('setup.userPlaceholder'), labelHidden: true, autocomplete: 'username', autocapitalize: 'none', spellcheck: 'false', required: true }),
      field({ label: t('auth.password'), name: 'password', type: 'password', placeholder: t('setup.passwordPlaceholder'), labelHidden: true, autocomplete: 'new-password', required: true }),
      h('button', { class: 'btn primary wide', type: 'submit', text: t('setup.next') })),
    async (v) => { first = { ...v, ...(await api('POST', '/api/setup/begin', v)) }; two(); });
    page({ title: t('setup.title'), line: t('setup.intro', { name: app.name }), step: { n: 1, of: 3 }, body: form });
    form.querySelector('input').focus();
  }

  function two() {
    const form = submit(h('form', { class: 'auth-form' },
      authenticator(first.uri, first.secret),
      field({ label: t('setup.totpCode'), name: 'totp', placeholder: t('setup.totpCode'), labelHidden: true, class: 'input code', inputmode: 'numeric', autocomplete: 'one-time-code', required: true }),
      h('div', { class: 'form-actions' },
        h('button', { class: 'btn ghost', type: 'button', text: t('common.back'), onclick: one }),
        h('button', { class: 'btn primary', type: 'submit', text: t('common.next') }))),
    async (v) => {
      try { await api('POST', '/api/setup/finish', { code: first.code, user: first.user, totp: v.totp }); }
      catch (e) { if (e instanceof ApiError && e.code === 'totp.wrong') e.fields = { totp: 'totp.wrong' }; throw e; }
      three();
    });
    page({ title: t('setup.totpTitle'), line: t('setup.totpIntro'), step: { n: 2, of: 3 }, body: form });
    form.querySelector('input').focus();
  }

  function three() {
    page({ title: t('setup.recoveryTitle'), line: t('setup.recoveryIntro'), step: { n: 3, of: 3 }, body: finish(first.recovery, enter) });
  }
  one();
}

/** The QR code for the authenticator app, and the key to type where a camera does not help. */
export function authenticator(uri, secret) {
  const qr = h('div', { class: 'qr' }, svg(qrSvg(uri, { label: t('setup.qrLabel') })));
  return h('div', { class: 'stack s' }, qr,
    h('p', { class: 'field-hint center', text: t('setup.secretHint') }),
    h('div', { class: 'line' }, h('code', { class: 'secret grow', text: secret.replace(/(.{4})/g, '$1 ').trim() }),
      h('button', { class: 'btn ghost icon-only', type: 'button', 'aria-label': t('common.copy'), onclick: () => copyText(secret) }, icon('copy'))));
}

/** The recovery codes with buttons to copy them or keep them as a file. */
export function recoveryCodes(codes) {
  const text = codes.join('\n');
  const save = () => {
    const a = h('a', { href: URL.createObjectURL(new Blob([text + '\n'], { type: 'text/plain' })), download: 'recovery-codes.txt' });
    a.click();
    URL.revokeObjectURL(a.href);
  };
  return h('div', { class: 'stack s' },
    h('div', { class: 'codes' }, codes.map((c) => h('span', { text: c }))),
    h('div', { class: 'cluster end' },
      h('button', { class: 'btn ghost', type: 'button', onclick: () => copyText(text) }, icon('copy'), t('common.copy')),
      h('button', { class: 'btn ghost', type: 'button', onclick: save }, icon('download'), t('common.download'))));
}
