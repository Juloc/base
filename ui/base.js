/* =====================================================================
 * The one script of the look (CODE-06: plain JavaScript modules, no build step, no framework).
 * What it gives an app:
 *   h, replace, icon, svg   build DOM without innerHTML (SEC-05); replace() swaps children with the rules of h()
 *   api, ApiError, errorText talk to the own server (adds the header the guard asks for, turns error codes into text)
 *   theme, prefs            light or dark, and the person's settings on the server
 *   toast                   a short message that goes away by itself
 *   openDialog, confirmDialog, closeDialogs  a big window that does not block the page, one at a time; a sheet on a phone; closes when the page changes (DIALOG-01);
 *                           actions: [{ label, icon?, kind?, side?, enter?, run(dialog), more?: [{ label, icon?, danger?, run(dialog) }] }]
 *   openMenu                a small menu at a button
 *   splitButton             a main action with an arrow that opens a menu of alternatives (also as a dialog action with more: [...])
 *   field, passwordField, submit, showErrors   forms with errors at the fields (FORM-01, FORM-02)
 *   dropdown, switchField, segmented, choiceTiles   the choosing controls of the family
 *   actionBar, selectionBar, floatBar   the bars above the content; navBadge  a dot or a count on a place of the menu
 *   swipeFade               the soft edge of a row you swipe sideways
 *   emptyState, loading, skeleton, notice, badge
 *   shell, router           the frame of the app (menu, head, person menu) and the pages inside it
 *   pwa                     the service worker, the quiet "offline" line and the offer of a new version (the app calls it once)
 * The catalogue (/base/catalog.html) shows each of them with the code.
 * ===================================================================== */
import { t, hasKey, getPref as getLangPref, setPref as setLangPref } from './i18n.js';
import { glyph } from './icons.js';

/* ---------- DOM ---------- */

/**
 * Makes an element. props: class, text, dataset, on<event> handlers, everything else as an attribute (true: empty attribute; null, undefined, false: left out).
 * Children: nodes, texts, arrays of them; null and false are skipped. Text always goes in as text, never as markup.
 */
export function h(tag, props, ...children) {
  const el = document.createElement(tag);
  if (props) for (const [k, v] of Object.entries(props)) {
    if (v === undefined || v === null || v === false) continue;
    if (k === 'class') el.className = v;
    else if (k === 'text') el.textContent = v;
    else if (k === 'dataset') Object.assign(el.dataset, v);
    else if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2).toLowerCase(), v);
    else if (v === true) el.setAttribute(k, '');
    else el.setAttribute(k, String(v));
  }
  add(el, children);
  return el;
}
function add(el, children) {
  for (const c of children) {
    if (c === undefined || c === null || c === false) continue;
    if (Array.isArray(c)) add(el, c);
    else el.append(c.nodeType ? c : document.createTextNode(String(c)));
  }
}

/** Replaces the children of el by kids, with the rules of h(): null, undefined and false are left out, arrays are flattened.
 *  Use it instead of el.replaceChildren(), which writes "null" as text for a child that is null. Returns el. */
export function replace(el, ...kids) {
  el.replaceChildren();
  add(el, kids);
  return el;
}

/** Turns SVG markup that this code made itself (an icon, a QR code) into a node. The only place that parses markup; never give it text from a person or a server. */
export function svg(markup) {
  const tpl = document.createElement('template');
  tpl.innerHTML = markup;
  return tpl.content.firstElementChild;
}

/** An icon of the family as a node. cls: "l" (larger), "duo" (areas tinted). */
export const icon = (name, cls) => svg(glyph(name, cls));

/** Replaces every <span data-icon="name"> below root by its icon (for markup written by hand, as in the catalogue). */
export function hydrateIcons(root = document) {
  for (const el of root.querySelectorAll('[data-icon]')) el.replaceWith(icon(el.dataset.icon, el.className || undefined));
}

let uid = 0;
const nextId = (prefix) => `${prefix}-${++uid}`;

/* ---------- API ---------- */

export class ApiError extends Error {
  constructor(status, body) {
    super((body && body.message) || 'Request failed');
    this.status = status;
    this.code = (body && body.error) || 'failed';
    this.fields = (body && body.fields) || null;
    this.retryAfter = (body && body.retryAfter) || 0;
  }
}
const STATUS_CODES = { 0: 'network', 400: 'input', 401: 'auth.none', 403: 'forbidden', 404: 'not_found', 413: 'too_large', 429: 'too_many' };

/**
 * A request to the app's own server. Everything that changes something carries the header the guard asks for (SEC-04).
 * Returns the JSON of the answer (null for an empty one); throws ApiError with the code of the server.
 */
export async function api(method, url, body) {
  const init = { method, headers: {}, credentials: 'same-origin' };
  if (method !== 'GET') init.headers['X-Base'] = '1';
  if (body !== undefined) { init.headers['Content-Type'] = 'application/json'; init.body = JSON.stringify(body); }
  let res;
  try { res = await fetch(url, init); }
  catch { setOffline(true); throw new ApiError(0, { error: 'network', message: 'The server cannot be reached.' }); }
  setOffline(false);
  if (res.status === 204) return null;
  let data = null;
  try { data = await res.json(); } catch { /* an answer without JSON */ }
  if (res.ok) return data;
  const error = new ApiError(res.status, data || { error: STATUS_CODES[res.status] || 'failed' });
  // the session ended (signed out on another device, or it ran out): back to the sign-in
  if (error.code === 'auth.none' && signedIn) { signedIn = false; location.reload(); }
  throw error;
}
let signedIn = false;
/** Tells the script that a person is signed in: from now on a lost session reloads the page (which shows the sign-in). */
export function markSignedIn() { signedIn = true; }

/** The text for an error in the person's language: by the code of the server, else its English message. */
export function errorText(e) {
  if (!(e instanceof ApiError)) return t('err.failed');
  const key = 'err.' + e.code;
  if (hasKey(key)) return t(key, { seconds: e.retryAfter, minutes: Math.max(1, Math.ceil(e.retryAfter / 60)) });
  return e.message || t('err.failed');
}

/* ---------- light or dark ---------- */

const SCHEME_KEY = 'base.scheme';
export const theme = {
  /** 'auto' (the device decides), 'light' or 'dark'. */
  get() { try { const v = localStorage.getItem(SCHEME_KEY); return v === 'light' || v === 'dark' ? v : 'auto'; } catch { return 'auto'; } },
  set(value) {
    const v = value === 'light' || value === 'dark' ? value : 'auto';
    try { v === 'auto' ? localStorage.removeItem(SCHEME_KEY) : localStorage.setItem(SCHEME_KEY, v); } catch { /* no storage */ }
    theme.apply();
    return v;
  },
  apply() {
    const v = theme.get(), root = document.documentElement;
    if (v === 'auto') delete root.dataset.scheme; else root.dataset.scheme = v;
    // the browser's own bar takes the colour of the page
    let meta = document.querySelector('meta[name="theme-color"]');
    if (!meta) { meta = h('meta', { name: 'theme-color' }); document.head.append(meta); }
    meta.setAttribute('content', getComputedStyle(document.body || root).backgroundColor);
  },
  isDark: () => (theme.get() === 'auto' ? matchMedia('(prefers-color-scheme: dark)').matches : theme.get() === 'dark'),
};

/* ---------- the person's settings on the server (they follow to every device) ---------- */

let settings = {};
export const prefs = {
  /** Loads the settings after sign-in and applies language and scheme chosen on another device. Returns true when the language changed (reload). */
  async load() {
    settings = (await api('GET', '/api/settings')).settings || {};
    if (settings.scheme && settings.scheme !== theme.get()) theme.set(settings.scheme);
    if (settings.lang && settings.lang !== getLangPref()) { setLangPref(settings.lang); return true; }
    return false;
  },
  get: (key, fallback) => (settings[key] === undefined ? fallback : settings[key]),
  async set(key, value) { await api('PUT', '/api/settings/' + encodeURIComponent(key), { value }); settings[key] = value; },
};

/* ---------- toast ---------- */

let toastRegion;
/**
 * A short message at the bottom that goes away by itself. kind: 'info' (default), 'ok', 'danger'. action: { label, run } adds one button (undo).
 * For something the person must not miss, use a notice in the page instead.
 */
export function toast(text, { kind = 'info', action, timeout } = {}) {
  if (!toastRegion) { toastRegion = h('div', { class: 'toasts', role: 'region', 'aria-live': 'polite', 'aria-label': t('toast.region') }); document.body.append(toastRegion); }
  const close = () => { clearTimeout(timer); el.remove(); };
  const el = h('div', { class: `toast ${kind}`, role: kind === 'danger' ? 'alert' : 'status' },
    kind === 'ok' ? icon('check-circle') : kind === 'danger' ? icon('alert') : null,
    h('div', { class: 'toast-text', text }),
    action ? h('button', { class: 'btn', type: 'button', text: action.label, onclick: () => { close(); action.run(); } }) : null,
    h('button', { class: 'btn icon-only', type: 'button', 'aria-label': t('common.close'), onclick: close }, icon('x')));
  let timer = setTimeout(close, timeout || (kind === 'danger' || action ? 8000 : 4500));
  el.addEventListener('pointerenter', () => clearTimeout(timer));
  el.addEventListener('pointerleave', () => { timer = setTimeout(close, 2500); });
  toastRegion.append(el);
  while (toastRegion.children.length > 3) toastRegion.firstElementChild.remove();
  return close;
}

/* ---------- dialog ---------- */

const FOCUSABLE = 'input:not([type=hidden]):not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled]), a[href], [tabindex]:not([tabindex="-1"])';
const openDialogs = new Set();
const openLists = new Set();         // the open lists of dropdowns: they close with the page, too
// What the person last pressed or typed into. Safari does not focus a button on click, so the focus alone does not tell what opened a dialog.
let lastPress = null;
addEventListener('pointerdown', (e) => { lastPress = e.target; }, true);
addEventListener('keydown', (e) => { lastPress = e.target; }, true);

/** Closes every open dialog and dropdown list. The router does it when the page changes; an app may call it before it navigates by other means. */
export function closeDialogs() {
  for (const d of [...openDialogs]) d.close();
  for (const close of [...openLists]) close();
}

/**
 * Opens a dialog: big and centred on a wide screen (it can be moved by its title and does not block the page behind it), a sheet from below on a phone.
 * Solid, with a faint scrim; it closes by itself when the page changes (the router calls closeDialogs()).
 *  title    the heading
 *  body     a node, or a function (dialog) => node. Two columns: a .dialog-cols with a .dialog-col in each column.
 *  actions  buttons at the bottom, fixed there on a phone: { label, kind: 'primary' | 'danger' | 'ghost', side: 'start' | 'end', run(dialog) }.
 *           side 'start' puts the button bottom left (delete, deactivate), the others stand bottom right (cancel, then the main action).
 *           run may be async; while it runs the button is busy. The dialog closes after run unless it returns false or throws. An action without run just closes.
 *  size     's' (480 px: a question), 'm' (720 px, the default), 'l' (940 px, tables and two columns); wide: true is 'l'
 *  onClose  called once when it is closed, however that happened
 * Escape and the close button close it; the focus goes into the dialog and back to where it was (DIALOG-01).
 * One at a time (DIALOG-01): opened from the page, it replaces an open dialog; the button that opened the open one, pressed again,
 * only brings that one forward (nothing typed is lost; the new dialog is not made and its onClose runs at once). Opened from inside
 * an open dialog (the question before deleting), it stands above that one.
 * Returns { el, body, close() }.
 */
export function openDialog({ title, body, actions = [], onClose, wide = false, size }) {
  const opener = document.activeElement;
  const trigger = pressed(opener && opener !== document.body ? opener : lastPress);
  const fromDialog = !!trigger && [...openDialogs].some((d) => d.el.contains(trigger));
  if (!fromDialog) {
    const same = trigger && [...openDialogs].find((d) => d.trigger === trigger);
    if (same) {
      const field = same.body.querySelector(FOCUSABLE) || same.el.querySelector(FOCUSABLE);
      if (field) field.focus();
      if (onClose) onClose();
      return same;
    }
    for (const d of [...openDialogs]) d.close();
  }
  const titleId = nextId('dlg');
  const dialog = { el: null, body: null, close, trigger };
  const bodyEl = h('div', { class: 'dialog-body' });
  const head = h('div', { class: 'dialog-head' },
    h('h2', { class: 'dialog-title', id: titleId, text: title }),
    h('button', { class: 'btn ghost icon-only', type: 'button', 'aria-label': t('common.close'), onclick: () => close() }, icon('x')));
  // an action runs, shows itself busy and closes the dialog unless run returns false; an ApiError becomes a toast
  const perform = async (btn, run) => {
    if (!run) { close(); return; }
    try { if (await busy(btn, run(dialog)) !== false) close(); }
    catch (e) { if (!(e instanceof ApiError)) throw e; toast(errorText(e), { kind: 'danger' }); }
  };
  let enterButton = null;
  const button = (a) => {
    // with a symbol the name is also the button's label: on a phone a button at the start shows only its symbol (base.css)
    const btn = h('button', { class: `btn ${a.kind || ''}`.trim(), type: 'button', 'aria-label': a.icon ? a.label : null, title: a.icon && a.side === 'start' ? a.label : null }, a.icon ? icon(a.icon) : null, h('span', { text: a.label }));
    btn.addEventListener('click', () => perform(btn, a.run));
    if (a.enter || (a.more && a.more.length)) enterButton = btn;
    if (!a.more || !a.more.length) return btn;
    // a split button (0.4.5): the main action and a small arrow with a menu of alternatives; the menu items run like actions of the dialog
    return splitOf(btn, a, (item) => perform(btn, item.run));
  };
  const foot = actions.length ? h('div', { class: 'dialog-foot' },
    actions.some((a) => a.side === 'start') ? h('div', { class: 'dialog-foot-start' }, actions.filter((a) => a.side === 'start').map(button)) : null,
    h('div', { class: 'dialog-foot-end' }, actions.filter((a) => a.side !== 'start').map(button))) : null;
  const sizeClass = { s: ' size-s', l: ' size-l' }[size || (wide ? 'l' : 'm')] || '';
  const el = h('dialog', { class: 'dialog' + sizeClass, 'aria-labelledby': titleId }, head, bodyEl, foot);
  const scrim = h('div', { class: 'dialog-scrim' });
  dialog.el = el; dialog.body = bodyEl;
  bodyEl.append(typeof body === 'function' ? body(dialog) : body);

  let closed = false;
  function close() {
    if (closed) return;
    closed = true;
    openDialogs.delete(dialog);
    el.close(); el.remove(); scrim.remove();
    if (opener && opener.isConnected && typeof opener.focus === 'function') opener.focus();
    if (onClose) onClose();
  }
  el.addEventListener('keydown', (e) => { if (e.key === 'Escape') { e.stopPropagation(); close(); } });
  // Enter in a field runs the action marked enter (or the one with a menu): one key for the usual way out
  bodyEl.addEventListener('keydown', (e) => {
    if (e.key !== 'Enter' || !enterButton || e.isComposing || e.defaultPrevented || e.shiftKey || e.altKey || e.ctrlKey || e.metaKey) return;
    if (!(e.target instanceof HTMLInputElement) || ['checkbox', 'radio', 'button', 'submit', 'file'].includes(e.target.type)) return;
    e.preventDefault();
    enterButton.click();
  });
  makeMovable(el, head);
  openDialogs.add(dialog);
  document.body.append(scrim, el);
  el.show();                                   // show(), not showModal(): the page behind stays usable
  // into the first field; a dialog without fields starts on its first button (the harmless one), not on the close button
  const first = bodyEl.querySelector(FOCUSABLE) || (foot && foot.querySelector(FOCUSABLE)) || el.querySelector(FOCUSABLE);
  if (first) first.focus();
  return dialog;
}

/** The control a press landed on (the button around an icon, the row around a cell); nothing for the page itself. */
function pressed(node) {
  if (!(node instanceof Element) || node === document.body || node === document.documentElement) return null;
  return node.closest('button, a[href], [role=button], [tabindex], tr, li') || node;
}

/** Lets a window be moved by its head with mouse or finger (only where it is a window, not a sheet). */
function makeMovable(el, handle) {
  handle.addEventListener('pointerdown', (e) => {
    if (e.target.closest('button') || !matchMedia('(min-width: 600px)').matches) return;
    const box = el.getBoundingClientRect(), dx = e.clientX - box.left, dy = e.clientY - box.top;
    const move = (m) => {
      el.classList.add('moved');
      el.style.left = Math.min(Math.max(8 - box.width / 2, m.clientX - dx), innerWidth - 80) + 'px';
      el.style.top = Math.min(Math.max(8, m.clientY - dy), innerHeight - 56) + 'px';
    };
    const up = () => { handle.removeEventListener('pointermove', move); handle.removeEventListener('pointerup', up); handle.removeEventListener('pointercancel', up); };
    handle.setPointerCapture(e.pointerId);
    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', up);
    handle.addEventListener('pointercancel', up);
  });
}

/** Asks before something that cannot be undone (DIALOG-01). Resolves to true when the person confirms. The button says what happens, never just "OK". */
export function confirmDialog({ title, text, confirmLabel, danger = false, icon: symbol }) {
  return new Promise((resolve) => {
    let answer = false;
    openDialog({
      title, size: 's',
      body: h('p', { class: 'muted', text }),
      actions: [
        { label: t('common.cancel'), kind: 'ghost' },
        { label: confirmLabel, icon: symbol, kind: danger ? 'danger' : 'primary', run: () => { answer = true; } },
      ],
      onClose: () => resolve(answer),
    });
  });
}

/* ---------- menu ---------- */

/**
 * A small menu at a button. items: { label, icon, run, danger, href } or '-' (a line) or { heading }. Arrow keys move, Escape closes.
 */
export function openMenu(anchor, items) {
  const close = () => {
    menu.remove();
    document.removeEventListener('pointerdown', outside, true);
    anchor.setAttribute('aria-expanded', 'false');
  };
  const outside = (e) => { if (!menu.contains(e.target)) close(); };
  const menu = h('div', { class: 'menu', role: 'menu' }, items.map((it) => {
    if (it === '-') return h('div', { class: 'menu-sep', role: 'separator' });
    if (it.heading) return h('div', { class: 'menu-label', text: it.heading });
    const props = { class: 'menu-item' + (it.danger ? ' danger' : ''), role: it.checked === undefined ? 'menuitem' : 'menuitemradio', 'aria-checked': it.checked === undefined ? null : String(it.checked), onclick: () => { close(); if (it.run) it.run(); } };
    const inner = [it.icon ? icon(it.icon) : null, h('span', { class: 'grow', text: it.label }), it.checked ? icon('check') : null];
    return it.href ? h('a', { ...props, href: it.href }, inner) : h('button', { ...props, type: 'button' }, inner);
  }));
  menu.addEventListener('keydown', (e) => {
    const list = [...menu.querySelectorAll('.menu-item')], i = list.indexOf(document.activeElement);
    if (e.key === 'Escape') { close(); anchor.focus(); }
    else if (e.key === 'ArrowDown') { e.preventDefault(); list[(i + 1) % list.length].focus(); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); list[(i - 1 + list.length) % list.length].focus(); }
    else if (e.key === 'Tab') close();
  });
  document.body.append(menu);
  // place it at the anchor, inside the window
  const a = anchor.getBoundingClientRect(), m = menu.getBoundingClientRect();
  const below = a.bottom + 6 + m.height <= innerHeight - 8;
  menu.style.top = Math.max(8, below ? a.bottom + 6 : a.top - 6 - m.height) + 'px';
  menu.style.left = Math.max(8, Math.min(a.right - m.width, innerWidth - m.width - 8, Math.max(a.left, 8))) + 'px';
  anchor.setAttribute('aria-expanded', 'true');
  setTimeout(() => document.addEventListener('pointerdown', outside, true));
  const first = menu.querySelector('.menu-item');
  if (first) first.focus();
  return close;
}

/**
 * A split button (0.4.5): a main action and a small arrow beside it that opens a menu of alternatives, e.g. "Speichern und anwenden" with "Nur speichern".
 * splitButton({ label, icon?, kind?: 'primary' | 'danger' | 'ghost', run, more: [{ label, icon?, danger?, run }] }) returns the element.
 * The arrow is a button of its own (aria-haspopup=menu) and opens openMenu(). In a dialog write the same as an action: { label, kind, run, more }.
 */
export function splitButton({ label, icon: symbol, kind = 'primary', run, more = [] }) {
  const btn = h('button', { class: `btn ${kind}`.trim(), type: 'button' }, symbol ? icon(symbol) : null, h('span', { text: label }));
  btn.addEventListener('click', () => busy(btn, run && run()));
  return splitOf(btn, { kind, more }, (item) => busy(btn, item.run && item.run()));
}

function splitOf(main, { kind, more }, choose) {
  const arrow = h('button', { class: `btn ${kind || ''} split-more`.trim(), type: 'button', 'aria-haspopup': 'menu', 'aria-expanded': 'false', 'aria-label': t('common.moreChoices') }, icon('down'));
  arrow.addEventListener('click', () => openMenu(arrow, more.map((m) => ({ label: m.label, icon: m.icon, danger: m.danger, run: () => choose(m) }))));
  return h('div', { class: 'btn-split' }, main, arrow);
}

/* ---------- forms ---------- */

/**
 * A field: label, control, hint, and a place for the error (FORM-01: every control has a visible label).
 * type: any input type, or 'textarea', or 'select' (a dropdown() of our own; options: [[value, label], ...], and search, placeholder, onchange, disabled go to it).
 * Other props go to the control (required, maxlength, autocomplete, inputmode ...). Explain in the placeholder, keep the label short.
 * labelHidden: the label stays for a screen reader but is not drawn (the sign-in card: the placeholder says the same, FORM-01).
 */
export function field({ label, name, type = 'text', value = '', hint, options, rows, labelHidden = false, ...rest }) {
  const id = nextId('f');
  let control, shown;
  if (type === 'textarea') { control = h('textarea', { class: 'textarea', id, name, rows: rows || 4, ...rest }); control.value = value; }
  else if (type === 'select') {
    const { search, placeholder, onchange, disabled } = rest;
    shown = dropdown({ id, name, value, options, labelledBy: id + '-label', search, placeholder, onchange, disabled });
    control = shown.button;
  }
  else { control = h('input', { class: 'input', id, name, type, ...rest }); control.value = value; }
  if (!shown) shown = type === 'search' ? h('div', { class: 'search' }, icon('search'), control) : control;      // a search field is a pill with its symbol
  const describedBy = [];
  const hintEl = hint ? h('p', { class: 'field-hint', id: id + '-hint', text: hint }) : null;
  if (hintEl) describedBy.push(hintEl.id);
  describedBy.push(id + '-error');
  control.setAttribute('aria-describedby', describedBy.join(' '));
  return h('div', { class: 'field', dataset: { field: name } },
    h('label', { for: id, id: id + '-label', class: labelHidden ? 'sr-only' : null, text: label }), shown, hintEl,
    h('p', { class: 'field-error', id: id + '-error', hidden: true }));
}

/**
 * A dropdown of our own instead of the browser's select: a button with the value and a chevron, a styled list, optional search, the whole keyboard.
 *  name, value, options [[value, label, hint?], ...], placeholder (shown while no option matches the value)
 *  search      a search field at the top of the list that filters as one types (default: on for more than 10 options)
 *  label       the accessible name when there is no visible label (field() gives labelledBy: the id of its label)
 *  onchange    called with the new value; the hidden input also fires "change", so a form reads it as before (formValues)
 * Keys: Enter, Space, arrows open; arrows, Home, End, PageUp, PageDown move; Enter chooses; Escape closes; typing jumps to a match (in the search field it filters).
 * On a phone the list is a sheet from below, on a wide screen it hangs at the button and fits the window. Closes on a click outside and when the page changes.
 * Returns the element; .value reads and sets the value, .button is the combobox button, .input the hidden input, .setOptions(list) replaces the options.
 */
export function dropdown({ name, value = '', options, id = nextId('dd'), label, labelledBy, placeholder, search, onchange, disabled = false }) {
  const listId = id + '-list', valueId = id + '-value';
  const useSearch = search === undefined ? options.length > 10 : !!search;
  let current = String(value), items = [], active = -1, ui = null, typed = '', typedAt = 0;
  const hidden = h('input', { type: 'hidden', name, value: current });
  const valueEl = h('span', { class: 'dd-value', id: valueId });
  const nameEl = !labelledBy && label ? h('span', { class: 'sr-only', id: id + '-name', text: label }) : null;
  const button = h('button', {
    class: 'dd-button', type: 'button', id, role: 'combobox', 'aria-haspopup': 'listbox', 'aria-expanded': 'false', 'aria-controls': listId,
    'aria-labelledby': (labelledBy || (nameEl ? nameEl.id : valueId)) + (labelledBy || nameEl ? ' ' + valueId : ''), disabled,
  }, valueEl, icon('down'));
  const dd = h('div', { class: 'dd' }, button, hidden, nameEl);
  const fold = (x) => String(x).normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();

  function paint() {
    const hit = options.find(([v]) => String(v) === current);
    valueEl.textContent = hit ? hit[1] : (placeholder === undefined ? t('form.choose') : placeholder);
    valueEl.classList.toggle('none', !hit);
  }
  function setValue(v, fire) {
    current = String(v); hidden.value = current; paint();
    if (fire) { hidden.dispatchEvent(new Event('input', { bubbles: true })); hidden.dispatchEvent(new Event('change', { bubbles: true })); if (onchange) onchange(current); }
  }
  const visible = () => items.filter((i) => !i.el.hidden);
  function setActive(item, scroll = true) {
    active = item ? items.indexOf(item) : -1;
    for (const i of items) i.el.classList.toggle('active', i === item);
    const focusEl = ui.search || button;
    if (item) { focusEl.setAttribute('aria-activedescendant', item.el.id); if (scroll) item.el.scrollIntoView({ block: 'nearest' }); }
    else focusEl.removeAttribute('aria-activedescendant');
  }
  function filter() {
    const q = ui.search ? fold(ui.search.value.trim()) : '';
    for (const i of items) i.el.hidden = !!q && !fold(i.label).includes(q);
    const shown = visible();
    ui.none.hidden = shown.length > 0;
    setActive(shown.find((i) => i.value === current && !q) || shown[0] || null);
  }
  function place() {
    if (!ui) return;
    const list = ui.list;
    if (!matchMedia('(min-width: 600px)').matches) {                  // the sheet: the CSS places it; keep it above the keyboard
      const vv = window.visualViewport;
      list.style.maxHeight = vv ? Math.round(vv.height * 0.72) + 'px' : '';
      list.style.bottom = vv ? Math.max(0, innerHeight - vv.height - vv.offsetTop) + 'px' : '';
      return;
    }
    const r = button.getBoundingClientRect(), gap = 6, margin = 8;
    const width = Math.min(Math.max(r.width, useSearch ? 280 : 200), innerWidth - margin * 2);
    list.style.width = width + 'px';
    list.style.left = Math.max(margin, Math.min(r.left, innerWidth - width - margin)) + 'px';
    list.style.maxHeight = ''; list.style.top = '0'; list.style.bottom = '';
    const natural = Math.min(list.offsetHeight, 360), below = innerHeight - r.bottom - gap - margin, above = r.top - gap - margin;
    if (natural <= below || below >= above) { list.style.top = r.bottom + gap + 'px'; list.style.maxHeight = Math.max(120, Math.min(360, below)) + 'px'; }
    else { list.style.top = 'auto'; list.style.bottom = innerHeight - r.top + gap + 'px'; list.style.maxHeight = Math.max(120, Math.min(360, above)) + 'px'; }
  }
  function choose(item) { if (!item) return; const changed = item.value !== current; close(true); if (changed) setValue(item.value, true); }
  function close(refocus) {
    if (!ui) return;
    const { list, scrim, off } = ui;
    ui = null; active = -1; typed = '';
    list.remove(); if (scrim) scrim.remove(); off();
    openLists.delete(closeNow);
    dd.classList.remove('open'); button.setAttribute('aria-expanded', 'false'); button.removeAttribute('aria-activedescendant');
    if (refocus) button.focus();
  }
  const closeNow = () => close(false);
  function move(delta) {
    const shown = visible();
    if (!shown.length) return;
    const at = shown.indexOf(items[active]);
    setActive(shown[at < 0 ? (delta > 0 ? 0 : shown.length - 1) : Math.min(shown.length - 1, Math.max(0, at + delta))]);
  }
  function jump(text) {                                    // type-ahead: the next entry that starts with what was typed
    const now = Date.now();
    typed = now - typedAt > 700 ? text : typed + text; typedAt = now;
    const shown = visible(), from = Math.max(0, shown.indexOf(items[active]));
    const order = [...shown.slice(from + (typed.length === 1 ? 1 : 0)), ...shown.slice(0, from + (typed.length === 1 ? 1 : 0))];
    setActive(order.find((i) => fold(i.label).startsWith(fold(typed))) || items[active] || null);
  }
  function onKey(e) {
    const isOpen = !!ui, printable = e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey;
    if (!isOpen) {
      if (['ArrowDown', 'ArrowUp', 'Enter', ' ', 'Home', 'End'].includes(e.key) || printable) { e.preventDefault(); show(printable && e.key !== ' ' ? e.key : ''); }
      return;
    }
    switch (e.key) {
      case 'ArrowDown': e.preventDefault(); move(1); break;
      case 'ArrowUp': e.preventDefault(); move(-1); break;
      case 'PageDown': e.preventDefault(); move(8); break;
      case 'PageUp': e.preventDefault(); move(-8); break;
      case 'Home': case 'End':
        if (ui.search && !e.ctrlKey) break;                  // in the search field they move the caret; with Ctrl they move in the list
        e.preventDefault(); { const shown = visible(); setActive(e.key === 'Home' ? shown[0] : shown[shown.length - 1]); } break;
      case 'Enter': e.preventDefault(); choose(items[active]); break;
      case 'Escape': e.preventDefault(); e.stopPropagation(); close(true); break;
      case 'Tab': close(false); break;
      case ' ': if (!ui.search) { e.preventDefault(); choose(items[active]); } break;
      default: if (printable && !ui.search) { e.preventDefault(); jump(e.key); }
    }
  }
  function show(first = '') {
    if (ui || button.disabled) return;
    const phone = !matchMedia('(min-width: 600px)').matches;
    const searchEl = useSearch ? h('input', { type: 'text', role: 'combobox', 'aria-autocomplete': 'list', 'aria-expanded': 'true', 'aria-controls': listId, 'aria-label': t('form.search'), placeholder: t('form.search'), autocomplete: 'off', spellcheck: 'false' }) : null;
    items = options.map(([v, l, hint], n) => {
      const sel = String(v) === current;
      const el = h('div', { class: 'dd-option', role: 'option', id: `${id}-o${n}`, 'aria-selected': String(sel) },
        h('span', { class: 'dd-label', text: l }), hint ? h('span', { class: 'dd-hint', text: hint }) : null, sel ? icon('check') : null);
      return { value: String(v), label: String(l), el };
    });
    const none = h('div', { class: 'dd-none', text: t('form.nothingFound'), hidden: true });
    const optionsEl = h('div', { class: 'dd-options', role: 'listbox', id: listId, 'aria-labelledby': labelledBy || (nameEl ? nameEl.id : valueId) }, items.map((i) => i.el), none);
    const list = h('div', { class: 'dd-list' }, phone ? h('div', { class: 'dd-title', text: label || (labelledBy && document.getElementById(labelledBy)?.textContent) || '' }) : null,
      searchEl ? h('div', { class: 'dd-search' }, icon('search'), searchEl) : null, optionsEl);
    const scrim = phone ? h('div', { class: 'dd-scrim' }) : null;
    // the focus stays where it is (the button, or the search field); the mouse must not take it away
    list.addEventListener('mousedown', (e) => { if (!e.target.closest('.dd-search')) e.preventDefault(); });
    optionsEl.addEventListener('click', (e) => { const el = e.target.closest('.dd-option'); choose(items.find((i) => i.el === el)); });
    optionsEl.addEventListener('pointermove', (e) => { const el = e.target.closest('.dd-option'), item = items.find((i) => i.el === el); if (item && item !== items[active]) setActive(item, false); });
    const outside = (e) => { if (!list.contains(e.target) && !button.contains(e.target)) close(false); };
    const reposition = (e) => { if (!e || !list.contains(e.target)) place(); };
    document.addEventListener('pointerdown', outside, true);
    addEventListener('resize', reposition); addEventListener('scroll', reposition, true);
    if (window.visualViewport) window.visualViewport.addEventListener('resize', reposition);
    const off = () => {
      document.removeEventListener('pointerdown', outside, true);
      removeEventListener('resize', reposition); removeEventListener('scroll', reposition, true);
      if (window.visualViewport) window.visualViewport.removeEventListener('resize', reposition);
    };
    ui = { list, scrim, search: searchEl, none, off };
    if (searchEl) { searchEl.addEventListener('keydown', onKey); searchEl.addEventListener('input', filter); }
    openLists.add(closeNow);
    document.body.append(...(scrim ? [scrim] : []), list);
    dd.classList.add('open'); button.setAttribute('aria-expanded', 'true');
    place();
    if (searchEl) { searchEl.focus(); if (first) { searchEl.value = first; } }
    filter();
    if (first && !searchEl) jump(first);
  }
  button.addEventListener('keydown', onKey);
  button.addEventListener('click', () => (ui ? close(true) : show()));
  Object.defineProperties(dd, {
    value: { get: () => current, set: (v) => setValue(v, false) },
    button: { value: button }, input: { value: hidden },
  });
  dd.setOptions = (list) => { options = list; paint(); };
  paint();
  return dd;
}

/** A password field with a button to show what was typed. */
export function passwordField(props) {
  const f = field({ ...props, type: 'password' });
  const input = f.querySelector('input');
  const toggle = h('button', { class: 'btn ghost icon-only', type: 'button', 'aria-label': t('form.showPassword'), 'aria-pressed': 'false' }, icon('eye'));
  toggle.addEventListener('click', () => {
    const show = input.type === 'password';
    input.type = show ? 'text' : 'password';
    toggle.setAttribute('aria-pressed', String(show));
    toggle.replaceChildren(icon(show ? 'eye-off' : 'eye'));
  });
  const group = h('div', { class: 'input-group' });
  input.replaceWith(group);
  group.append(input, toggle);
  return f;
}

/** A switch with its text. Returns the label; the checkbox inside is .querySelector('input'). */
export function switchField({ label, hint, name, checked = false, onchange }) {
  const input = h('input', { type: 'checkbox', role: 'switch', name, onchange });
  input.checked = checked;
  return h('label', { class: 'switch reverse' }, input, h('span', { class: 'switch-track' }),
    h('span', { class: 'switch-text' }, h('span', { text: label }), hint ? h('span', { class: 'field-hint', text: hint }) : null));
}

/** A choice of a few options shown side by side. options: [[value, label, iconName?], ...]. */
export function segmented({ name, label, value, options, onchange }) {
  return h('div', { class: 'seg', role: 'radiogroup', 'aria-label': label }, options.map(([v, l, ic]) => {
    const input = h('input', { type: 'radio', name, value: v, onchange: () => onchange && onchange(v) });
    input.checked = v === value;
    return h('label', null, input, h('span', null, ic ? icon(ic) : null, l));
  }));
}

/**
 * Two to four tiles with a title and a line, one chosen (a bigger choice than a segmented(): each option needs a sentence).
 * options: [[value, title, line], ...]. Radio buttons inside a radiogroup, so the arrow keys and screen readers work. onchange(value).
 */
export function choiceTiles({ name, label, value, options, onchange }) {
  return h('div', { class: 'choices', role: 'radiogroup', 'aria-label': label }, options.map(([v, title, line]) => {
    const input = h('input', { type: 'radio', name, value: v, onchange: () => onchange && onchange(v) });
    input.checked = v === value;
    return h('label', { class: 'choice' }, input, h('span', { class: 'choice-title', text: title }), line ? h('span', { class: 'choice-line', text: line }) : null);
  }));
}

/** Makes a row that overflows sideways show a soft edge where there is more (class more-left, more-right on the element). */
export function swipeFade(el) {
  const update = () => {
    el.classList.toggle('more-left', el.scrollLeft > 4);
    el.classList.toggle('more-right', el.scrollLeft < el.scrollWidth - el.clientWidth - 4);
  };
  el.addEventListener('scroll', update, { passive: true });
  if (typeof ResizeObserver !== 'undefined') new ResizeObserver(update).observe(el);
  update();
  return el;
}
/**
 * A group of places in a capsule, for the head of a page or above a list: links, the current one a filled pill inside the capsule.
 * items: [{ label, href, icon?, current? }]. Choices that are not places are a segmented() (the same look).
 */
export function capsule({ label, items }) {
  return swipeFade(h('nav', { class: 'capsule swipe', 'aria-label': label }, items.map((i) =>
    h('a', { class: 'capsule-item', href: i.href, 'aria-current': i.current ? 'page' : null }, i.icon ? icon(i.icon) : null, i.label))));
}

/**
 * The bar of actions above the content (0.4): a white card, centred above the menu on a phone and at the bottom of the content on a wide screen.
 * For something that waits for a decision ("Apply changes (3)"): one or two buttons, and optionally a text and a symbol.
 * { text?, icon?, actions: [{ label, icon?, kind: 'primary' | 'ghost' | 'danger', run(bar) }] }. An action closes the bar unless its run returns false.
 * Returns { el, close(), update({ text, icon, actions }) }: update changes the words in place ("Apply changes (4)").
 */
export function actionBar(options) {
  const bar = { el: h('div', { class: 'action-bar float-bar', role: 'region' }), close: () => bar.el.remove(), update: render };
  function render({ text, icon: name, actions = [] }) {
    bar.el.setAttribute('aria-label', text || t('bar.actions'));
    bar.el.replaceChildren(...[name ? icon(name) : null, text ? h('span', { class: 'action-bar-text', text }) : null].filter(Boolean),
      ...actions.map((a) => h('button', { class: `btn ${a.kind || ''}`.trim(), type: 'button', onclick: async (e) => {
        try { if (!a.run || await busy(e.currentTarget, a.run(bar)) !== false) bar.close(); }
        catch (err) { if (!(err instanceof ApiError)) throw err; toast(errorText(err), { kind: 'danger' }); }
      } }, a.icon ? icon(a.icon) : null, a.label)));
  }
  render(options);
  document.body.append(bar.el);
  return bar;
}

/** The old name of actionBar(): the same bar. */
export const floatBar = actionBar;

/**
 * The dark bar while rows are selected: "3 selected", the actions that work on them, and a button that clears the selection. It takes the place of the action bar.
 * { count, actions: [{ label, icon?, kind: 'danger'?, run(bar) }], onClear }. Returns { el, close(), update({ count }) }; the app keeps the selection and closes the bar when it is empty.
 */
export function selectionBar({ count = 0, actions = [], onClear }) {
  const text = h('span', { class: 'selection-bar-text', 'aria-live': 'polite' });
  const bar = { el: null, close: () => bar.el.remove(), update: ({ count: n }) => { text.textContent = t('bar.selected', { n }); } };
  bar.el = h('div', { class: 'selection-bar', role: 'region', 'aria-label': t('bar.selected', { n: '' }).trim() }, text,
    ...actions.map((a) => h('button', { class: `btn ${a.kind || ''}`.trim(), type: 'button', onclick: async (e) => {
      try { await busy(e.currentTarget, a.run && a.run(bar)); }
      catch (err) { if (!(err instanceof ApiError)) throw err; toast(errorText(err), { kind: 'danger' }); }
    } }, a.icon ? icon(a.icon) : null, a.label)),
    h('button', { class: 'btn icon-only', type: 'button', 'aria-label': t('bar.clear'), onclick: () => { bar.close(); if (onClear) onClear(); } }, icon('x')));
  bar.update({ count });
  document.body.append(bar.el);
  return bar;
}

/** The values of the named controls of a form (checkboxes as true or false, texts trimmed except passwords). */
export function formValues(form) {
  const values = {};
  for (const el of form.elements) {
    if (!el.name) continue;
    if (el.type === 'checkbox') values[el.name] = el.checked;
    else if (el.type === 'radio') { if (el.checked) values[el.name] = el.value; }
    else values[el.name] = el.type === 'password' ? el.value : el.value.trim();
  }
  return values;
}

export function clearErrors(form) {
  for (const f of form.querySelectorAll('.field.invalid')) {
    f.classList.remove('invalid');
    const p = f.querySelector('.field-error'); p.hidden = true; p.replaceChildren();
    const c = f.querySelector('.input, .select, .textarea, .dd-button'); if (c) c.removeAttribute('aria-invalid');
  }
  const summary = form.querySelector('.form-error');
  if (summary) summary.remove();
}

/**
 * Shows the errors of a failed request in the form (FORM-02): at each wrong field, in words; anything that belongs to no field as a notice on top.
 * The first wrong field gets the focus.
 */
export function showErrors(form, e) {
  clearErrors(form);
  let first = null, shown = 0;
  for (const [name, code] of Object.entries((e instanceof ApiError && e.fields) || {})) {
    const f = form.querySelector(`.field[data-field="${CSS.escape(name)}"]`);
    if (!f) continue;
    const text = hasKey('field.' + code) ? t('field.' + code) : hasKey('err.' + code) ? t('err.' + code) : t('field.invalid');
    f.classList.add('invalid');
    const p = f.querySelector('.field-error'); p.replaceChildren(icon('alert'), h('span', { text })); p.hidden = false;
    const c = f.querySelector('.input, .select, .textarea, .dd-button');
    if (c) { c.setAttribute('aria-invalid', 'true'); first = first || c; }
    shown++;
  }
  if (!shown) form.prepend(h('div', { class: 'notice danger form-error', role: 'alert' }, icon('alert'), h('div', { class: 'notice-text', text: errorText(e) })));
  if (first) first.focus();
}

/** While the promise runs, the button shows a spinner and cannot be pressed twice. Returns what the promise returns. */
export async function busy(button, promise) {
  if (!promise || typeof promise.then !== 'function') return promise;
  button.classList.add('busy'); button.disabled = true;
  try { return await promise; }
  finally { button.classList.remove('busy'); button.disabled = false; }
}

/**
 * Wires a form: on submit it clears old errors, makes the submit button busy and calls fn(values).
 * An ApiError thrown by fn is shown in the form; anything else is a bug and goes on.
 */
export function submit(form, fn) {
  form.setAttribute('novalidate', '');
  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    clearErrors(form);
    const button = form.querySelector('button[type=submit], button:not([type])') || document.querySelector(`button[form="${form.id}"]`);
    try { await (button ? busy(button, fn(formValues(form))) : fn(formValues(form))); }
    catch (err) { if (!(err instanceof ApiError)) throw err; showErrors(form, err); }
  });
  return form;
}

/* ---------- small parts ---------- */

/** What a list or a page shows when there is nothing yet: says what belongs here and offers the first step (LIST-02). */
export function emptyState({ icon: name = 'inbox', title, text, action }) {
  return h('div', { class: 'empty' },
    h('div', { class: 'empty-mark' }, icon(name)),
    h('h2', { class: 'empty-title', text: title }),
    text ? h('p', { class: 'empty-text', text }) : null,
    action ? h('button', { class: 'btn primary', type: 'button', onclick: action.run }, action.icon ? icon(action.icon) : null, action.label) : null);
}
export const loading = () => h('div', { class: 'loading', role: 'status', 'aria-label': t('common.loading') }, h('div', { class: 'spinner' }));
/** Grey lines in place of content that is on its way. */
export const skeleton = (lines = 3) => h('div', { class: 'stack s', 'aria-hidden': 'true' }, h('span', { class: 'skeleton title' }), Array.from({ length: lines }, (_, i) => h('span', { class: 'skeleton' + (i === lines - 1 ? ' short' : '') })));
/** A message that stays in the page. kind: 'info' (default), 'ok', 'warn', 'danger'. */
export function notice(text, kind = 'info') {
  const icons = { info: 'info', ok: 'check-circle', warn: 'alert', danger: 'alert' };
  return h('div', { class: `notice ${kind === 'info' ? '' : kind}`.trim(), role: kind === 'danger' ? 'alert' : 'note' }, icon(icons[kind] || 'info'), h('div', { class: 'notice-text', text }));
}
export const badge = (text, kind = '') => h('span', { class: `badge ${kind}`.trim(), text });

/** Copies a text and says so. */
export async function copyText(text) {
  try { await navigator.clipboard.writeText(text); toast(t('common.copied'), { kind: 'ok' }); }
  catch { toast(t('err.copy'), { kind: 'danger' }); }
}

/* ---------- shell ---------- */

const badges = new Map();      // path -> value, so a badge set before the shell exists is applied when it is made
function applyBadges(root) {
  for (const a of root.querySelectorAll('[data-nav]')) {
    a.querySelector('.nav-badge')?.remove();
    const value = badges.get(a.dataset.nav);
    if (!value) continue;
    const dot = value === true;
    const el = h('span', { class: 'nav-badge ' + (dot ? 'dot' : 'count') },
      dot ? null : h('span', { 'aria-hidden': 'true', text: value === '!' ? '!' : Number(value) > 99 ? '99+' : String(value) }),
      h('span', { class: 'sr-only', text: dot ? t('shell.badgeNew') : (value === '!' ? '' : value + ' ') + t('shell.badgeAttention') }));
    (a.querySelector('.tab-pill') || a).append(el);
  }
}
/**
 * A dot or a count on a place of the menu, for something that happened on another page (0.4).
 * value: true is an accent dot (something new), a number or '!' a red count or mark (needs a look), false, 0 or null takes it away.
 * path is the route of the place, with or without the hash ('/items').
 */
export function navBadge(path, value) {
  const key = '#' + String(path).replace(/^#/, '');
  if (value) badges.set(key, value); else badges.delete(key);
  applyBadges(document);
}

/**
 * The frame of the app (SHELL-01): on a phone a head on top and a floating pill at the bottom that holds all places and is swiped sideways; on a wide screen the menu on the left.
 * The person menu (settings, light or dark, sign out) is the same in every app.
 *   app      { name, icon }
 *   nav      [{ label, icon, href }]   href is a hash route such as '#/items'
 *   person   { name }
 *   menu     extra entries of the person menu, in front of "sign out"
 *   onSignOut
 * Returns { el, show(view, path) }. A view is { title, sub, body, back, actions }: sub is a second line under the title, back is the hash to go back to,
 * actions are nodes at the right of the title (the main action first). A nav place with foot: true goes into a second group at the bottom of the side list.
 */
export function shell({ app, nav, person, menu = [], onSignOut }) {
  const title = h('div', { class: 'shell-title', 'aria-hidden': 'true' });       // the small title in the bar: it appears when the page has scrolled
  const backSlot = h('div', null);
  const main = h('main', { class: 'shell-main', id: 'main', tabindex: '-1' });
  const personMenu = (anchor) => openMenu(anchor, [
    { heading: person.name },
    ...menu,
    '-',
    // the appearance has three steps: the device decides (the default), light, dark
    ...[['auto', 'settings.schemeAuto', 'monitor'], ['light', 'settings.schemeLight', 'sun'], ['dark', 'settings.schemeDark', 'moon']].map(([v, key, ic]) =>
      ({ label: t(key), icon: ic, checked: theme.get() === v, run: () => { theme.set(v); prefs.set('scheme', v).catch(() => {}); } })),
    '-',
    { label: t('shell.signOut'), icon: 'logout', run: onSignOut },
  ]);
  const initial = person.name.slice(0, 1);
  const headPerson = h('button', { class: 'person-button', type: 'button', 'aria-label': t('shell.personMenu'), 'aria-haspopup': 'menu', onclick: (e) => personMenu(e.currentTarget) }, h('span', { class: 'avatar', text: initial }));
  const sidePerson = h('button', { class: 'person', type: 'button', 'aria-haspopup': 'menu', onclick: (e) => personMenu(e.currentTarget) },
    h('span', { class: 'avatar', text: initial }), h('span', { class: 'person-name', text: person.name }), icon('more'));
  const link = (cls, n, inner) => h('a', { class: cls, href: n.href, dataset: { nav: n.href } }, inner);
  const navItem = (n) => link('nav-item', n, [icon(n.icon), h('span', { text: n.label })]);
  const brand = (cls) => h('a', { class: cls, href: nav[0] ? nav[0].href : '#/' }, h('span', { class: 'brand-mark' }, h('img', { src: '/icon.svg', alt: '' })), h('span', { text: app.name }));
  const foot = nav.filter((n) => n.foot);
  const side = h('aside', { class: 'shell-side' },
    brand('brand'),
    h('nav', { class: 'nav', 'aria-label': t('shell.nav') }, nav.filter((n) => !n.foot).map(navItem)),
    foot.length ? h('nav', { class: 'nav nav-foot', 'aria-label': t('shell.nav') }, foot.map(navItem)) : null,
    sidePerson);
  const head = h('header', { class: 'shell-head' }, backSlot, brand('head-brand'), title, h('span', { class: 'grow' }), headPerson);
  // the menu pill of a phone holds every place; you swipe it sideways, the current place stands in the middle, the edges fade where more is hidden
  const tabsScroll = h('div', { class: 'tabs-scroll' }, nav.map((n) => link('tab', n, [h('span', { class: 'tab-pill' }, icon(n.icon)), h('span', { text: n.label })])));
  const tabs = h('nav', { class: 'shell-tabs', 'aria-label': t('shell.nav') }, tabsScroll);
  const fades = () => {
    tabsScroll.classList.toggle('more-left', tabsScroll.scrollLeft > 4);
    tabsScroll.classList.toggle('more-right', tabsScroll.scrollLeft < tabsScroll.scrollWidth - tabsScroll.clientWidth - 4);
  };
  const centre = () => {
    const cur = tabsScroll.querySelector('[aria-current]');
    if (cur) {
      const box = cur.getBoundingClientRect(), at = tabsScroll.getBoundingClientRect();
      tabsScroll.scrollTo({ left: tabsScroll.scrollLeft + box.left - at.left - (at.width - box.width) / 2, behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
    }
    fades();
  };
  tabsScroll.addEventListener('scroll', fades, { passive: true });
  addEventListener('resize', fades);
  const el = h('div', { class: 'shell' },
    h('a', { class: 'skip', href: '#main', text: t('shell.skip'), onclick: (e) => { e.preventDefault(); main.focus(); } }),
    side, h('div', { class: 'shell-body' }, head, main), tabs);
  addEventListener('scroll', () => head.classList.toggle('scrolled', scrollY > 4), { passive: true });
  applyBadges(el);

  function show(view, path = '') {
    title.textContent = view.title || '';
    document.title = view.title ? `${view.title} · ${app.name}` : app.name;
    const back = () => h('a', { class: 'btn ghost icon-only', href: view.back, 'aria-label': t('common.back') }, icon('left'));
    backSlot.replaceChildren(view.back ? back() : '');
    backSlot.hidden = !view.back;
    head.classList.toggle('has-back', !!view.back);
    // the head of the page: the big title, a second line, the main action at the right (the start page of JulOS)
    const pageHead = h('div', { class: 'page-head' },
      view.back ? h('span', { class: 'page-back' }, back()) : null,
      h('div', { class: 'page-head-text' }, h('h1', { class: 'page-title', text: view.title || '' }), view.sub ? h('p', { class: 'page-sub', text: view.sub }) : null),
      view.actions && view.actions.length ? h('div', { class: 'page-actions' }, view.actions) : null);
    main.replaceChildren(pageHead, view.body);
    // the place in the menu that contains this page is marked (the longest matching start wins)
    const current = nav.map((n) => n.href.replace(/^#/, '')).filter((p) => path === p || path.startsWith(p.replace(/\/$/, '') + '/') || p === path + '/').sort((a, b) => b.length - a.length)[0];
    for (const a of el.querySelectorAll('[data-nav]')) {
      if (a.dataset.nav === '#' + current) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
    }
    requestAnimationFrame(centre);       // after the page is in the document and the pill has its width
  }
  return { el, main, show };
}

/* ---------- router ---------- */

/**
 * Pages by the hash of the address. routes: { '/items': fn, '/items/:id': fn }; a function gets the parameters and returns a view
 * (or a promise of one) for shell.show. While a page loads, grey lines stand in; a failed load offers "try again".
 * Returns { go(path), refresh() }.
 */
export function router(routes, frame, { fallback = '/' } = {}) {
  const table = Object.entries(routes).map(([pattern, fn]) => {
    const names = [];
    const re = new RegExp('^' + pattern.replace(/:[a-zA-Z]+/g, (m) => { names.push(m.slice(1)); return '([^/]+)'; }) + '/?$');
    return { re, names, fn };
  });
  let run = 0;
  async function render() {
    const path = decodeURI(location.hash.replace(/^#/, '')) || '/';
    let match = null;
    for (const r of table) { const m = r.re.exec(path); if (m) { match = { fn: r.fn, params: Object.fromEntries(r.names.map((n, i) => [n, m[i + 1]])) }; break; } }
    if (!match) { if (path !== fallback) location.replace('#' + fallback); return; }
    const mine = ++run;
    const wait = setTimeout(() => { if (mine === run) frame.show({ title: '', body: h('div', { class: 'page' }, skeleton(4)) }, path); }, 180);
    try {
      const view = await match.fn(match.params);
      if (mine !== run) return;
      clearTimeout(wait);
      frame.show(view, path);
      scrollTo(0, 0);
      frame.main.focus({ preventScroll: true });
    } catch (e) {
      clearTimeout(wait);
      if (mine !== run) return;
      if (!(e instanceof ApiError)) console.error(e);
      frame.show({
        title: t('err.title'),
        body: emptyState({ icon: 'alert', title: t('err.title'), text: e instanceof ApiError ? errorText(e) : t('err.failed'), action: { label: t('common.retry'), icon: 'refresh', run: render } }),
      }, path);
    }
  }
  // a dialog or an open list belongs to the page it was opened on: when the page changes, it goes (a refresh of the same page keeps it)
  addEventListener('hashchange', () => { closeDialogs(); render(); });
  render();
  return { go: (path) => { if (location.hash === '#' + path) render(); else location.hash = path; }, refresh: render };
}

/* ---------- installable: the service worker, the offline line, a new version (0.4) ---------- */

let notes, offline = false, waiting = null;
const offlineLine = h('div', { class: 'status-line', role: 'status' }, icon('cloud'), h('span', { text: t('pwa.offline') }));
const updateLine = h('div', { class: 'status-line', role: 'status' }, icon('refresh'), h('span', { text: t('pwa.update') }),
  h('button', { class: 'quiet', type: 'button', text: t('pwa.reload'), onclick: () => { wantReload = true; waiting.postMessage('skip-waiting'); } }));
let wantReload = false;
/** The two quiet lines (offline, new version) hang at the top of the window, on the page's root element so no change of the body removes them. */
function paintNotes() {
  const lines = [offline ? offlineLine : null, waiting ? updateLine : null].filter(Boolean);
  if (!lines.length) { notes?.remove(); return; }
  if (!notes) notes = h('div', { class: 'status-lines' });
  notes.replaceChildren(...lines);
  if (!notes.isConnected) document.documentElement.append(notes);
}
/** The page knows it has no connection (the device says so, or a request did not get through); a request that gets through says it is back. */
function setOffline(value) { if (value === offline) return; offline = value; paintNotes(); }
addEventListener('offline', () => setOffline(true));
addEventListener('online', () => setOffline(false));
if (navigator.onLine === false) setOffline(true);

/**
 * Makes the app installable and keeps it fresh: registers the service worker (the base serves /sw.js, or the app's own file does), shows
 * "Neue Version · Neu laden" when a new worker waits, and reloads once the person pressed it. Without a connection a quiet line says "offline" in words.
 * Call it once at the start of the app, before start().
 */
export function pwa({ worker = '/sw.js' } = {}) {
  if (!('serviceWorker' in navigator)) return;
  const offer = (w) => { if (navigator.serviceWorker.controller) { waiting = w; paintNotes(); } };
  navigator.serviceWorker.register(worker).then((reg) => {
    if (reg.waiting) offer(reg.waiting);
    reg.addEventListener('updatefound', () => {
      const w = reg.installing;
      if (w) w.addEventListener('statechange', () => { if (w.state === 'installed') offer(w); });
    });
    // somebody who comes back to an open tab gets the new version offered too
    document.addEventListener('visibilitychange', () => { if (!document.hidden) reg.update().catch(() => {}); });
  }).catch(() => { /* the app works without it */ });
  navigator.serviceWorker.addEventListener('controllerchange', () => { if (wantReload) location.reload(); });
}

theme.apply();
