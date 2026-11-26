/* =====================================================================
 * The card of the door (0.4): sign-in, first setup, invitation, and the pages an app has of its own around a sign-in (caddy's gate).
 *   authCard({ icon, title, line, target?, step?, body, foot })   one white card: the mark of the app, a title, one line, what the page needs
 *   stateCard({ icon, tone, title, text, actions })                 the same card for one state: working, done, not allowed, link expired
 *   mountAuth(card)                                                 puts the card in the middle of an empty page
 * Both return the card; an app puts it on the page with mountAuth() or inside its own <main class="auth">.
 * ===================================================================== */
import { t, LANG_CHOICES, getPref, setPref } from './i18n.js';
import { h, icon, theme, openMenu } from './base.js';

/** Language and light or dark as two quiet text buttons, the bottom line of the card. */
export function authFoot() {
  const shown = (LANG_CHOICES.find(([v]) => v === getPref()) || LANG_CHOICES[0])[1];
  const lang = h('button', { class: 'quiet', type: 'button', 'aria-haspopup': 'menu', 'aria-label': t('settings.language') + ': ' + shown }, icon('globe'), shown);
  lang.addEventListener('click', () => openMenu(lang, LANG_CHOICES.map(([v, label]) => ({ label, checked: v === getPref(), run: () => { setPref(v); location.reload(); } }))));
  const label = () => t(theme.isDark() ? 'auth.toLight' : 'auth.toDark');
  const dark = h('button', { class: 'quiet', type: 'button' }, icon(theme.isDark() ? 'sun' : 'moon'), label());
  dark.addEventListener('click', () => { theme.set(theme.isDark() ? 'light' : 'dark'); dark.replaceChildren(icon(theme.isDark() ? 'sun' : 'moon'), label()); });
  return h('div', { class: 'auth-foot' }, lang, dark);
}

/** The marker of a setup in steps: n of `of` bars are filled. */
export function steps(n, of = 3) {
  return h('div', { class: 'steps', role: 'img', 'aria-label': t('setup.step', { n, of }) }, Array.from({ length: of }, (_, i) => h('i', { class: i < n ? 'on' : '' })));
}

/**
 * The card. icon: a name from icons.js (the app's own mark; 'shield' by default). title and line: the heading and one sentence under it.
 * target: { host, note } is the soft box "afterwards you go straight there" before a protected page; note defaults to that sentence.
 * step: { n, of } adds the marker of a setup. body: nodes (a form, buttons). foot: false leaves out language and theme.
 */
export function authCard({ icon: mark = 'shield', title, line, target, step, body, foot = true }) {
  return h('div', { class: 'auth-card' },
    h('div', { class: 'auth-head' },
      h('span', { class: 'brand-mark' }, icon(mark)),
      h('div', null, h('h1', { class: 'auth-title', text: title }), line ? h('p', { class: 'auth-line', text: line }) : null)),
    step ? steps(step.n, step.of) : null,
    target ? h('div', { class: 'auth-target' }, icon('lock'), h('div', null, h('b', { text: target.host }), h('span', { text: target.note || t('auth.thenThere') }))) : null,
    body,
    foot ? authFoot() : null);
}

/**
 * One state, in the same card: a round mark, one sentence, one or two buttons.
 * tone: 'run' (soft accent, the mark turns), 'ok' (green), 'danger' (raspberry). actions: [{ label, icon?, href?, run? }]; the first is the main one.
 */
export function stateCard({ icon: mark = 'info', tone = 'run', title, text, actions = [], foot = false }) {
  const buttons = actions.map((a, i) => {
    const cls = 'btn' + (i ? ' ghost' : '');
    const inner = [a.icon ? icon(a.icon) : null, a.label];
    return a.href ? h('a', { class: cls, href: a.href }, inner) : h('button', { class: cls, type: 'button', onclick: a.run }, inner);
  });
  return h('div', { class: 'auth-card' },
    h('div', { class: 'state', role: tone === 'danger' ? 'alert' : 'status' },
      h('span', { class: 'state-mark ' + tone }, icon(mark)),
      h('h1', { class: 'auth-title', text: title }),
      text ? h('p', { class: 'auth-line', text }) : null,
      buttons.length ? h('div', { class: 'state-actions' }, buttons) : null),
    foot ? authFoot() : null);
}

/** Puts a card in the middle of an empty page (it replaces what the body holds). */
export function mountAuth(card) {
  document.body.replaceChildren(h('main', { class: 'auth' }, card));
}
