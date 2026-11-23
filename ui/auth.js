/* =====================================================================
 * The door of every app, the same everywhere: the setup assistant at the first start and the sign-in after that.
 *   start({ icon, onSignedIn })   asks the server who is there and shows the assistant, the sign-in, or hands over to the app.
 * The server decides everything; this file only draws (AUTH-08: the sign-in never says which part was wrong).
 * ===================================================================== */
import { t, LANG_CHOICES, getPref, setPref } from './i18n.js';
import { h, icon, svg, api, field, passwordField, submit, notice, theme, prefs, markSignedIn, copyText, errorText, ApiError, toast } from './base.js';
import { qrSvg } from './qr.js';

/**
 * Starts the app. onSignedIn(me) is called with { user, roles, app: { id, name, version, base } } once somebody is signed in.
 * icon: the icon of the app (a name from icons.js), shown above the forms.
 */
export async function start({ icon: appIcon = 'grid', onSignedIn }) {
  let me;
  try { me = await api('GET', '/api/auth/me'); }
  catch (e) {
    document.body.replaceChildren(h('main', { class: 'auth' }, notice(errorText(e), 'danger'),
      h('button', { class: 'btn', type: 'button', text: t('common.retry'), onclick: () => location.reload() })));
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
  const page = (card) => document.body.replaceChildren(frame(me.app, appIcon, card));
  if (me.setupNeeded) setup(me.app, page, enter); else signIn(me.app, page, enter);
}

/** The page around a form: the mark and name of the app on top, language and light or dark below. */
function frame(app, appIcon, card) {
  const lang = h('select', { class: 'select', 'aria-label': t('settings.language'), onchange: (e) => { setPref(e.target.value); location.reload(); } },
    LANG_CHOICES.map(([v, l]) => h('option', { value: v, text: l })));
  lang.value = getPref();
  const dark = h('button', { class: 'btn ghost icon-only', type: 'button', 'aria-label': t('shell.dark'), onclick: () => { theme.set(theme.isDark() ? 'light' : 'dark'); dark.replaceChildren(icon(theme.isDark() ? 'sun' : 'moon')); } }, icon(theme.isDark() ? 'sun' : 'moon'));
  return h('main', { class: 'auth' },
    h('div', { class: 'auth-card' },
      h('div', { class: 'auth-head' }, h('span', { class: 'brand-mark' }, icon(appIcon)), h('div', { class: 'h2', text: app.name })),
      card),
    h('div', { class: 'auth-foot' }, lang, dark));
}

function signIn(app, page, enter) {
  const form = submit(h('form', { class: 'card form' },
    h('h1', { class: 'h1', text: t('auth.title') }),
    field({ label: t('auth.user'), name: 'user', autocomplete: 'username', autocapitalize: 'none', spellcheck: 'false', required: true }),
    passwordField({ label: t('auth.password'), name: 'password', autocomplete: 'current-password', required: true }),
    field({ label: t('auth.code'), name: 'code', hint: t('auth.codeHint'), class: 'input code', inputmode: 'numeric', autocomplete: 'one-time-code', required: true }),
    h('button', { class: 'btn primary wide', type: 'submit', text: t('auth.signIn') })),
  async (v) => {
    const r = await api('POST', '/api/auth/login', v);
    await enter();
    if (r.recoveryLeft <= 3) toast(t('auth.recoveryLow', { n: r.recoveryLeft }), { kind: 'danger' });
  });
  page(form);
  form.querySelector('input').focus();
}

/** The setup assistant: 1 the first account (with the code from the server log), 2 the authenticator, 3 the recovery codes. */
function setup(app, page, enter) {
  const steps = (n) => h('div', { class: 'steps', role: 'img', 'aria-label': t('setup.step', { n, of: 3 }) }, [1, 2, 3].map((i) => h('i', { class: i <= n ? 'on' : '' })));
  let first = null;

  function one() {
    const form = submit(h('form', { class: 'card form' },
      steps(1),
      h('div', { class: 'stack s' }, h('h1', { class: 'h1', text: t('setup.title') }), h('p', { class: 'muted', text: t('setup.intro', { name: app.name }) })),
      field({ label: t('setup.code'), name: 'code', hint: t('setup.codeHint'), class: 'input code', autocomplete: 'off', autocapitalize: 'characters', spellcheck: 'false', required: true }),
      field({ label: t('auth.user'), name: 'user', hint: t('setup.userHint'), autocomplete: 'username', autocapitalize: 'none', spellcheck: 'false', required: true }),
      passwordField({ label: t('auth.password'), name: 'password', hint: t('setup.passwordHint'), autocomplete: 'new-password', required: true }),
      h('button', { class: 'btn primary wide', type: 'submit', text: t('common.next') })),
    async (v) => { first = { ...v, ...(await api('POST', '/api/setup/begin', v)) }; two(); });
    page(form);
    form.querySelector('input').focus();
  }

  function two() {
    const form = submit(h('form', { class: 'card form' },
      steps(2),
      h('div', { class: 'stack s' }, h('h1', { class: 'h1', text: t('setup.totpTitle') }), h('p', { class: 'muted', text: t('setup.totpIntro') })),
      authenticator(first.uri, first.secret),
      field({ label: t('setup.totpCode'), name: 'totp', class: 'input code', inputmode: 'numeric', autocomplete: 'one-time-code', required: true }),
      h('div', { class: 'form-actions' },
        h('button', { class: 'btn ghost', type: 'button', text: t('common.back'), onclick: one }),
        h('button', { class: 'btn primary', type: 'submit', text: t('common.next') }))),
    async (v) => {
      try { await api('POST', '/api/setup/finish', { code: first.code, user: first.user, totp: v.totp }); }
      catch (e) { if (e instanceof ApiError && e.code === 'totp.wrong') e.fields = { totp: 'totp.wrong' }; throw e; }
      three();
    });
    page(form);
    form.querySelector('input').focus();
  }

  function three() {
    const ok = h('input', { type: 'checkbox' });
    const go = h('button', { class: 'btn primary wide', type: 'button', text: t('setup.finish'), disabled: true, onclick: enter });
    ok.addEventListener('change', () => { go.disabled = !ok.checked; });
    page(h('div', { class: 'card form' },
      steps(3),
      h('div', { class: 'stack s' }, h('h1', { class: 'h1', text: t('setup.recoveryTitle') }), h('p', { class: 'muted', text: t('setup.recoveryIntro') })),
      recoveryCodes(first.recovery),
      h('label', { class: 'check' }, ok, h('span', { text: t('setup.recoverySaved') })),
      go));
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
