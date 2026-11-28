/* =====================================================================
 * The pieces of pages (0.4, steps 4 and 5): what an app builds its pages from once it has the shell and the controls of base.js.
 *   table           a white table that turns into two-line rows on a phone: one action column, rows that fold open, a tree of groups, selection
 *   tabs, chips     a bar of tabs and a row of filter chips; on a phone one row you swipe sideways (periods are tabs({ size: 's' }))
 *   numberBox, numberStrip   a tonal icon, a big number, a label, an optional change; the strip is a row you swipe on a phone
 *   chart           a stacked area chart in SVG, a legend that hides a series, a dark box with every value on hover and on tap
 *   liveFeed        rows that arrive at the top without anything being rebuilt; pause and resume
 *   timeline        day headings and cards on a line with a round state node
 *   ring            a ring with a number (days left)
 *   avatar          a round mark for a row or a head: a picture, else a symbol on its category colour, else the initial
 *   foldCard        a card whose head opens its body
 *   flash           a row or card lights up briefly: an event in place (no toast for events)
 *   swipeFade       the soft edge where a row you swipe has more (the menu pill uses the same)
 * Everything is built with h() (SEC-05) and styled by base.css (CODE-06); colours of a chart are given by the app.
 * The catalogue (/base/catalog.html) shows each of them with the code.
 * ===================================================================== */
import { h, icon, swipeFade, openMenu, selectionBar, emptyState, toast, errorText, ApiError } from './base.js';
import { t, fmtNumber } from './i18n.js';

const reduceMotion = () => matchMedia('(prefers-reduced-motion: reduce)').matches;
let uid = 0;
const nextId = (prefix) => `${prefix}-${++uid}`;
const isNode = (v) => v && typeof v === 'object' && v.nodeType;
const content = (v) => (isNode(v) || Array.isArray(v) ? v : v === undefined || v === null || v === false ? null : String(v));

export { swipeFade };

/* ---------- events in place, swipe rows ---------- */

/** An event in place (0.4): the row or card lights up briefly. Use it instead of a toast for something that happened on the page ("a new request", "a certificate renewed"). */
export function flash(el) {
  if (!el) return el;
  el.classList.remove('flash');
  void el.offsetWidth;                       // so that the animation starts again when it is called twice
  el.classList.add('flash');
  clearTimeout(el._flashTimer);
  el._flashTimer = setTimeout(() => el.classList.remove('flash'), 2200);
  return el;
}

function centre(row, child) {
  const a = row.getBoundingClientRect(), b = child.getBoundingClientRect();
  row.scrollTo({ left: row.scrollLeft + b.left - a.left - (a.width - b.width) / 2, behavior: reduceMotion() ? 'auto' : 'smooth' });
}

/* ---------- tabs and chips ---------- */

/**
 * A bar of tabs (0.4): a white bar, the chosen tab soft; on a phone one row you swipe sideways. Also for periods (size: 's').
 * items: [{ value, label, icon?, count?, controls? }]; value: the chosen one; onchange(value). Returns the element; el.select(value) chooses from code.
 * Tabs switch what the page shows below them (role tablist). A bar of links to places is a capsule().
 */
export function tabs({ label, items, value, onchange, size }) {
  const el = h('div', { class: 'tabbar swipe' + (size === 's' ? ' s' : ''), role: 'tablist', 'aria-label': label });
  const buttons = items.map((i) => h('button', { class: 'tabbar-item', type: 'button', role: 'tab', 'aria-selected': 'false', tabindex: '-1', 'aria-controls': i.controls, dataset: { value: i.value }, onclick: () => choose(i.value, true) },
    i.icon ? icon(i.icon) : null, h('span', { text: i.label }), i.count !== undefined && i.count !== null ? h('span', { class: 'tabbar-count', text: String(i.count) }) : null));
  el.append(...buttons);
  function choose(v, user) {
    for (const b of buttons) {
      const on = b.dataset.value === String(v);
      b.setAttribute('aria-selected', String(on));
      b.tabIndex = on ? 0 : -1;
      if (on) centre(el, b);
    }
    if (user && onchange) onchange(v);
  }
  el.addEventListener('keydown', (e) => {
    const at = buttons.indexOf(document.activeElement);
    if (at < 0 || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(e.key)) return;
    e.preventDefault();
    const next = e.key === 'Home' ? 0 : e.key === 'End' ? buttons.length - 1 : (at + (e.key === 'ArrowRight' ? 1 : -1) + buttons.length) % buttons.length;
    buttons[next].focus();
    choose(buttons[next].dataset.value, true);
  });
  el.select = (v) => choose(v, false);
  choose(value, false);
  swipeFade(el);
  return el;
}

/**
 * A row of filter chips (0.4): wraps on a wide screen, one row you swipe on a phone.
 * items: [{ value, label, icon? }]. One choice by default (value is the chosen one); multiple: true makes value an array and each chip a toggle.
 * onchange(value). Returns the element; el.get() gives the current value.
 */
export function chips({ label, items, value, multiple = false, onchange }) {
  let current = multiple ? new Set(value || []) : value;
  const el = h('div', { class: 'chips swipe', role: 'group', 'aria-label': label });
  const buttons = items.map((i) => h('button', { class: 'chip', type: 'button', 'aria-pressed': 'false', dataset: { value: i.value }, onclick: () => {
    if (multiple) { if (current.has(i.value)) current.delete(i.value); else current.add(i.value); } else current = i.value;
    paint();
    if (onchange) onchange(el.get());
  } }, i.icon ? icon(i.icon) : null, i.label));
  function paint() { for (const b of buttons) b.setAttribute('aria-pressed', String(multiple ? current.has(b.dataset.value) : b.dataset.value === String(current))); }
  el.append(...buttons);
  el.get = () => (multiple ? [...current] : current);
  paint();
  swipeFade(el);
  return el;
}

/* ---------- avatar ---------- */

const catClass = (cat) => (cat === undefined || cat === null || cat === '' ? '' : /^cat-\d$/.test(String(cat)) ? String(cat) : `cat-${Number(cat) % 10}`);
const initialOf = (name) => {
  const text = String(name || '').trim();
  if (!text) return '';
  if (typeof Intl !== 'undefined' && Intl.Segmenter) for (const { segment } of new Intl.Segmenter().segment(text)) return segment;
  return [...text][0];
};
/**
 * A round mark for who or what a row is about (0.4.5): a merchant, a person, an account. In this order it shows
 *   image   a picture (a URL the app serves), if it loads;
 *   icon    else a symbol of icons.js on the soft colour of its category (cat: 0 to 9 or 'cat-3', the --cat-N colours);
 *   name    else the initial of the name (on the category's colour when there is one, else on the soft accent).
 * { name, image?, icon?, cat?, size: 's' (32 px, the menu) | 'row' (40 px, default) | 'head' (64 px, the head of a page or dialog), shape: 'round' (default) | 'square', label? }.
 * Without label it is decoration beside the name (hidden from a screen reader); with label it is an image with that name.
 * One list never mixes shapes (decision 88): table({ shape }) and dayList({ shape }) set the shape of every mark inside them.
 */
export function avatar({ name = '', image, icon: symbol, cat, size = 'row', shape = 'round', label } = {}) {
  const el = h('span', { class: `avatar${size === 'row' || size === 'head' ? ' av-' + size : ''}${shape === 'square' ? ' av-square' : ''}`, role: label ? 'img' : null, 'aria-label': label || null, 'aria-hidden': label ? null : 'true' });
  const fallback = () => {
    el.classList.remove('av-image');
    const c = catClass(cat);
    if (c) el.classList.add(c, 'cat-soft');
    el.replaceChildren(symbol ? icon(symbol) : initialOf(name));
  };
  if (image) {
    el.classList.add('av-image');
    el.append(h('img', { src: image, alt: '', loading: 'lazy', decoding: 'async', onerror: fallback }));
  } else fallback();
  return el;
}

/* ---------- number box, ring ---------- */

/**
 * A number box (0.4): a tonal icon square, a big number, a small label, an optional change.
 * { icon, value (number or text), label, change?: { text, dir: 'up' | 'down' | 'flat', tone?: 'ok' | 'danger' }, href? }. With href the box is a link.
 * The change has an arrow and a text, so colour is never the only signal.
 */
export function numberBox({ icon: name, value, label, change, href }) {
  return h(href ? 'a' : 'div', { class: 'number-box', href }, name ? h('span', { class: 'number-icon' }, icon(name)) : null,
    h('span', { class: 'number-text' },
      h('b', { class: 'number-value' }, typeof value === 'number' ? fmtNumber(value) : value, change ? h('span', { class: `number-change ${change.dir || 'flat'} ${change.tone || ''}`.trim() }, change.dir === 'up' ? icon('up') : change.dir === 'down' ? icon('down') : null, change.text) : null),
      h('span', { class: 'number-label', text: label })));
}
/** Number boxes in a row: a grid on a wide screen, a strip you swipe on a phone. boxes: nodes from numberBox(). */
export function numberStrip(boxes, label) {
  return swipeFade(h('div', { class: 'number-strip swipe', role: label ? 'group' : null, 'aria-label': label }, boxes));
}

/**
 * A ring with a number inside (0.4), for days left or a share: ring(21, 30, 'warn'). state: '' (the accent), 'ok', 'warn' or 'danger'.
 * options: { text } what stands inside (default the value), { label } the words for a screen reader (default "21 of 30").
 */
export function ring(value, max, state = '', { text, label } = {}) {
  const part = max > 0 ? Math.max(0, Math.min(100, (value / max) * 100)) : 0;
  const el = h('span', { class: `ring ${state}`.trim(), role: 'img', 'aria-label': label || t('ring.of', { value, max }) }, h('span', { class: 'ring-text', 'aria-hidden': 'true', text: text ?? String(value) }));
  el.style.setProperty('--ring-p', part.toFixed(1));
  return el;
}

/* ---------- folding card ---------- */

/**
 * A card whose head opens its body (0.4). { title, sub?, lead? (a node: a ring, an icon), actions? (nodes at the right, outside the toggle), body (a node or a function that makes it on first opening), open?, onToggle(open) }.
 * Returns { el, toggle(open) }. The head is a button with aria-expanded; the actions stay reachable beside it.
 */
export function foldCard({ title, sub, lead, actions = [], body, open = false, onToggle }) {
  const id = nextId('fold');
  const bodyEl = h('div', { class: 'fold-body', id, hidden: true });
  let built = false;
  const toggleBtn = h('button', { class: 'fold-toggle', type: 'button', 'aria-expanded': 'false', 'aria-controls': id, onclick: () => card.toggle() },
    lead || null, h('span', { class: 'fold-text' }, h('b', { text: title }), sub ? h('span', { text: sub }) : null), icon('down'));
  const el = h('section', { class: 'panel fold-card' }, h('div', { class: 'fold-head' }, toggleBtn, actions.length ? h('div', { class: 'fold-extra' }, actions) : null), bodyEl);
  const card = {
    el,
    toggle(on = toggleBtn.getAttribute('aria-expanded') !== 'true') {
      if (on && !built) { built = true; bodyEl.append(typeof body === 'function' ? body() : body); }
      toggleBtn.setAttribute('aria-expanded', String(on));
      bodyEl.hidden = !on;
      el.classList.toggle('open', on);
      if (onToggle) onToggle(on);
    },
  };
  if (open) card.toggle(true);
  return card;
}

/* ---------- timeline ---------- */

const STATE_ICON = { ok: 'check', warn: 'alert', danger: 'x' };
/**
 * A timeline (0.4): day headings and cards on a line, each with a round node that tells the state.
 * days: [{ label: 'Today', items: [{ state: 'ok' | 'warn' | 'danger', title, time?, meta? (text under the title), body? (a node), icon? }] }].
 * The state is also said in words for a screen reader (colour is never the only signal).
 */
export function timeline({ days, label }) {
  return h('div', { class: 'timeline', role: 'list', 'aria-label': label },
    days.map((day) => h('div', { class: 'timeline-day', role: 'listitem' },
      h('h3', { class: 'timeline-heading', text: day.label }),
      h('ol', { class: 'timeline-items' }, day.items.map((it) => {
        const state = it.state || 'ok';
        return h('li', { class: `timeline-item ${state}` },
          h('span', { class: 'timeline-node' }, icon(it.icon || STATE_ICON[state] || 'check')),
          h('div', { class: 'timeline-card' },
            h('div', { class: 'timeline-top' }, h('b', { text: it.title }), h('span', { class: 'sr-only', text: t('timeline.' + state) }), it.time ? h('span', { class: 'timeline-time', text: it.time }) : null),
            it.meta ? h('div', { class: 'timeline-meta' }, content(it.meta)) : null,
            it.body ? h('div', { class: 'timeline-body' }, content(it.body)) : null));
      })))));
}

/* ---------- live feed ---------- */

/**
 * A live feed (0.4): rows that arrive at the top without anything being rebuilt, a colour dot per row, a button that pauses it.
 * { title?, label, max = 40 }. add({ title, sub?, time?, meta?, tone: 'info' | 'ok' | 'warn' | 'danger' | 'muted', dotLabel? }) puts a row on top (title and sub may be nodes);
 * while paused, rows wait and the button says how many; resume() shows them. Rows beyond max drop off at the bottom. The dot has a word for a screen reader when you give dotLabel.
 * Returns { el, add, pause(), resume(), clear(), paused }.
 */
export function liveFeed({ title, label, max = 40 } = {}) {
  const list = h('ul', { class: 'feed-list', role: 'log', 'aria-live': 'off', 'aria-label': label || title });
  const empty = h('p', { class: 'feed-empty', text: t('feed.empty') });
  const waiting = h('span', { class: 'feed-waiting' });
  const toggle = h('button', { class: 'btn mini quiet', type: 'button', onclick: () => (feed.paused ? feed.resume() : feed.pause()) });
  const el = h('section', { class: 'panel feed', 'aria-label': label || title },
    h('div', { class: 'feed-head' }, h('span', { class: 'live-dot', 'aria-hidden': 'true' }), title ? h('h3', { class: 'feed-title', text: title }) : h('span', { class: 'grow' }), waiting, toggle), list, empty);
  let queue = [];
  function paint() {
    toggle.replaceChildren(icon(feed.paused ? 'play' : 'pause'), feed.paused ? t('feed.resume') : t('feed.pause'));
    waiting.textContent = queue.length ? t('feed.waiting', { n: queue.length }) : '';
    el.classList.toggle('paused', feed.paused);
  }
  function insert(item) {
    empty.hidden = true;
    const row = h('li', { class: 'feed-row' }, h('span', { class: `feed-dot ${item.tone || 'info'}` }, item.dotLabel ? h('span', { class: 'sr-only', text: item.dotLabel }) : null),
      h('div', { class: 'feed-title-line' }, content(item.title)), item.time ? h('span', { class: 'feed-time', text: item.time }) : null,
      item.sub ? h('div', { class: 'feed-sub' }, content(item.sub)) : null, item.meta ? h('span', { class: 'feed-meta', text: item.meta }) : null);
    list.prepend(row);
    flash(row);
    while (list.children.length > max) list.lastElementChild.remove();
  }
  const feed = {
    el, paused: false,
    add(item) { if (feed.paused) { queue.unshift(item); queue.length = Math.min(queue.length, max); paint(); } else insert(item); },
    pause() { feed.paused = true; paint(); },
    resume() { feed.paused = false; const waitingRows = queue.reverse(); queue = []; for (const i of waitingRows) insert(i); paint(); },
    clear() { list.replaceChildren(); queue = []; empty.hidden = false; paint(); },
  };
  toggle.setAttribute('aria-pressed', 'false');
  paint();
  return feed;
}

/* ---------- chart ---------- */

const SVG_NS = 'http://www.w3.org/2000/svg';
function sv(tag, attrs) {
  const e = document.createElementNS(SVG_NS, tag);
  for (const [k, v] of Object.entries(attrs || {})) e.setAttribute(k, String(v));
  return e;
}
/** The smooth curve through points (x ascending) as cubic segments [c1x, c1y, c2x, c2y, x, y]: monotone, so a layer never swings below the one under it. */
function curve(pts) {
  const n = pts.length;
  if (n < 2) return [];
  const dx = [], slope = [], m = new Array(n);
  for (let i = 0; i < n - 1; i++) { dx[i] = pts[i + 1][0] - pts[i][0]; slope[i] = (pts[i + 1][1] - pts[i][1]) / dx[i]; }
  m[0] = slope[0]; m[n - 1] = slope[n - 2];
  for (let i = 1; i < n - 1; i++) m[i] = slope[i - 1] * slope[i] <= 0 ? 0 : (slope[i - 1] + slope[i]) / 2;
  for (let i = 0; i < n - 1; i++) {
    if (slope[i] === 0) { m[i] = 0; m[i + 1] = 0; continue; }
    const a = m[i] / slope[i], b = m[i + 1] / slope[i], s = a * a + b * b;
    if (s > 9) { const k = 3 / Math.sqrt(s); m[i] = k * a * slope[i]; m[i + 1] = k * b * slope[i]; }
  }
  return pts.slice(0, -1).map((p, i) => [p[0] + dx[i] / 3, p[1] + (m[i] * dx[i]) / 3, pts[i + 1][0] - dx[i] / 3, pts[i + 1][1] - (m[i + 1] * dx[i]) / 3, pts[i + 1][0], pts[i + 1][1]]);
}
const num = (v) => (Math.round(v * 100) / 100).toString();
/** The closed path of one layer: along its top, back along its bottom. */
function layerPath(top, bottom) {
  const t1 = curve(top), b1 = curve(bottom);
  let d = `M${num(top[0][0])} ${num(top[0][1])}`;
  for (const s of t1) d += `C${s.map(num).join(' ')}`;
  const last = bottom[bottom.length - 1];
  d += `L${num(last[0])} ${num(last[1])}`;
  for (let i = b1.length - 1; i >= 0; i--) { const s = b1[i], from = bottom[i]; d += `C${num(s[2])} ${num(s[3])} ${num(s[0])} ${num(s[1])} ${num(from[0])} ${num(from[1])}`; }
  return d + 'Z';
}
// The top of the scale: four equal steps, each a round number (1 to 10 times a power of ten, in round steps); whole numbers when the data are whole (counts), so no guide reads "1.5".
function niceMax(v, whole) {
  if (!(v > 0)) return 4;
  const raw = v / 4;
  const e = 10 ** Math.floor(Math.log10(raw));
  for (const m of [1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10]) {
    const step = m * e;
    if (step < raw || (whole && !Number.isInteger(step))) continue;
    return 4 * step;
  }
  return 4 * 10 * e;
}

/**
 * A stacked area chart (0.4), SVG without a library: smooth layers, a thin line in the colour of the card between them, three faint guide lines, an axis below,
 * a legend that hides a series on click, a dark box with every value on hover (a mouse), on a tap, and with the arrow keys.
 * { title, series: [{ key, label, color? }] (the first is the bottom layer; colour is any CSS colour, e.g. 'var(--accent)'; without one the n-th series takes the category colour --cat-n, 0.4.4),
 *   data: [{ label, title?, [key]: number }], format?: (n) => text, actions?: node (at the right of the title, for example tabs({ size: 's' })), height? (px, default 190) }.
 * Returns { el, update(data) }. A text that sums the chart up (sizes, peaks) stands in the page for a screen reader.
 */
export function chart({ title, series, data, format = (n) => fmtNumber(n), actions, height = 190 }) {
  series = series.map((s, i) => (s.color ? s : { ...s, color: `var(--cat-${i % 10})` }));     // decision 87
  const hidden = new Set();
  let points = data || [];
  let at = -1;
  const summaryId = nextId('chart');
  const summary = h('p', { class: 'sr-only', id: summaryId });
  const svgEl = sv('svg', { class: 'ch-svg', viewBox: '0 0 100 100', preserveAspectRatio: 'none', 'aria-hidden': 'true', focusable: 'false' });
  const yLabels = h('div', { class: 'ch-y', 'aria-hidden': 'true' });
  const axis = h('div', { class: 'ch-axis', 'aria-hidden': 'true' });
  const cursor = h('div', { class: 'ch-cursor', hidden: true });
  const tip = h('div', { class: 'ch-tip', hidden: true, role: 'status' });
  const emptyText = h('p', { class: 'ch-empty', hidden: true, text: t('chart.empty') });
  const plot = h('div', { class: 'ch-plot', tabindex: '0', role: 'group', 'aria-label': title, 'aria-describedby': summaryId }, yLabels, svgEl, cursor, tip, emptyText);
  plot.style.setProperty('--ch-h', height + 'px');
  const legend = h('div', { class: 'ch-legend', role: 'group', 'aria-label': t('chart.legend') }, series.map((s) => {
    const sw = h('i', { class: 'ch-swatch' });
    sw.style.background = s.color;
    return h('button', { class: 'ch-key', type: 'button', 'aria-pressed': 'true', dataset: { key: s.key }, onclick: (e) => {
      if (hidden.has(s.key)) hidden.delete(s.key);
      else if (series.length - hidden.size > 1) hidden.add(s.key);          // the last one stays: a chart of nothing says nothing
      e.currentTarget.setAttribute('aria-pressed', String(!hidden.has(s.key)));
      render();
    } }, sw, s.label);
  }));
  const el = h('figure', { class: 'panel chart' }, title || actions ? h('figcaption', { class: 'ch-head' }, h('b', { text: title }), actions || null) : null, legend, summary, h('div', { class: 'ch-body' }, plot, axis));
  const visible = () => series.filter((s) => !hidden.has(s.key));
  const total = (p, list = visible()) => list.reduce((a, s) => a + (Number(p[s.key]) || 0), 0);

  function render() {
    const list = visible(), n = points.length;
    svgEl.replaceChildren();
    yLabels.replaceChildren();
    axis.replaceChildren();
    emptyText.hidden = n > 0;
    hideTip();
    summary.textContent = n ? t('chart.summary', { title: title || '', n, from: points[0].label, to: points[n - 1].label, series: series.map((s) => t('chart.series', { name: s.label, total: format(points.reduce((a, p) => a + (Number(p[s.key]) || 0), 0)), peak: format(Math.max(...points.map((p) => Number(p[s.key]) || 0))) })).join(' ') }) : t('chart.empty');
    if (!n) return;
    const whole = points.every((p) => list.every((x) => Number.isInteger(Number(p[x.key]) || 0)));
    const top = niceMax(Math.max(...points.map((p) => total(p, list))), whole);
    const xs = points.map((_, i) => (n === 1 ? 50 : (i / (n - 1)) * 100));
    for (const g of [25, 50, 75]) {
      svgEl.append(sv('line', { class: 'ch-guide', x1: 0, x2: 100, y1: 100 - g, y2: 100 - g }));
      const label = h('span', { text: format((top * g) / 100) });
      label.style.top = 100 - g + '%';
      yLabels.append(label);
    }
    let base = points.map(() => 0);
    for (const s of list) {
      const lower = base.map((v, i) => [xs[i], 100 - (v / top) * 100]);
      base = base.map((v, i) => v + (Number(points[i][s.key]) || 0));
      const upper = base.map((v, i) => [xs[i], 100 - (v / top) * 100]);
      const dup = (a) => (n === 1 ? [[0, a[0][1]], [100, a[0][1]]] : a);
      const layer = sv('path', { class: 'ch-layer', d: layerPath(dup(upper), dup(lower)) });
      layer.style.fill = s.color;
      svgEl.append(layer);
    }
    const labelCount = Math.min(n, 6);
    for (let k = 0; k < labelCount; k++) {
      const i = labelCount === 1 ? 0 : Math.round((k * (n - 1)) / (labelCount - 1));
      const span = h('span', { class: k === 0 ? 'first' : k === labelCount - 1 ? 'last' : '', text: points[i].label });
      span.style.left = xs[i] + '%';
      axis.append(span);
    }
  }
  function showTip(i) {
    if (!points.length) return;
    at = Math.max(0, Math.min(points.length - 1, i));
    const p = points[at], list = visible(), pct = points.length === 1 ? 50 : (at / (points.length - 1)) * 100;
    cursor.hidden = false;
    cursor.style.left = pct + '%';
    tip.replaceChildren(h('b', { text: p.title || p.label }),
      ...list.slice().reverse().map((s) => { const sw = h('i', { class: 'ch-swatch' }); sw.style.background = s.color; return h('div', { class: 'ch-tip-row' }, sw, h('span', { text: s.label }), h('span', { text: format(Number(p[s.key]) || 0) })); }),
      list.length > 1 ? h('div', { class: 'ch-tip-row total' }, h('span', { text: t('chart.total') }), h('span', { text: format(total(p, list)) })) : null);
    tip.hidden = false;
    tip.style.left = pct > 55 ? 'auto' : `calc(${pct}% + 14px)`;
    tip.style.right = pct > 55 ? `calc(${100 - pct}% + 14px)` : 'auto';
  }
  function hideTip() { at = -1; cursor.hidden = true; tip.hidden = true; }
  const indexAt = (e) => { const r = plot.getBoundingClientRect(); return Math.round(((e.clientX - r.left) / r.width) * (points.length - 1)); };
  plot.addEventListener('pointermove', (e) => showTip(indexAt(e)));
  plot.addEventListener('pointerdown', (e) => showTip(indexAt(e)));
  plot.addEventListener('pointerleave', (e) => { if (e.pointerType === 'mouse') hideTip(); });
  plot.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') hideTip();
    else if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(e.key)) { e.preventDefault(); showTip(e.key === 'Home' ? 0 : e.key === 'End' ? points.length - 1 : (at < 0 ? points.length - 1 : at) + (e.key === 'ArrowRight' ? 1 : -1)); }
  });
  plot.addEventListener('blur', hideTip);
  document.addEventListener('pointerdown', (e) => { if (!plot.contains(e.target)) hideTip(); });     // a tap elsewhere closes the box
  render();
  return { el, update(next) { points = next || []; render(); } };
}

/* ---------- table ---------- */

/**
 * A table (0.4): a white panel, a small grey head, 50 px rows with fine lines. Wide it is a real table; on a phone every row is two lines (the name, below it the other columns)
 * with the switch or the main action at the right and the rest in a menu.
 *   label       what the table is (for a screen reader)
 *   columns     [{ key, label, render?(row) -> node or text, align?: 'end', phone?: 'title' | 'sub' | 'hide', from?: 'm' | 'l' | 'xl' }]; the first column is the title on a phone, the others the second line (or hidden).
 *               The layout follows the width of the table's own panel, not of the window (a container query, 0.4.5): below 640 px the rows have two lines,
 *               and a column with from shows only from 800 ('m'), 1000 ('l') or 1200 px ('xl'); on two-line rows phone decides instead.
 *   rows        [{ id, ..., children?: [rows] }]   rows with children fold open as a tree: a rounded rail per level, a round node where a row branches off it, child rows tinted (0.4.5)
 *   groups      [{ id, title, count?, rows, onAdd?(g), addLabel?, menu?: [openMenu items] or (g) => items, active?: boolean, onActive?(on, g), activeLabel? }]  folding groups with a head; an inactive group greys its rows
 *   rowKey      (row) => key, default row.id
 *   actions     (row) => [{ label, icon?, kind?: 'main' | 'danger', run(row, button) }]   the action column: at most one 'main' (a tonal text button), the others quiet icon buttons
 *   active      { label?, get(row) -> boolean, set(row, on) -> promise }   the switch of a row, always the last column; a row that is off is greyed
 *   detail      (row) => node   a row folds open to this (several at once); a row with children folds its children instead
 *   selection   { actions: [{ label, icon?, kind?, run(rows, table) }], mode?: 'always' | 'button', button?: boolean }   check boxes and the selection bar.
 *               'always' (default): check boxes on a wide table; on two-line rows through the "Select" button or a long press.
 *               'button' (0.4.5): plain rows until "Select" (or choose(true), or a long press) turns choosing on; Esc, "Done", clearing the bar or unticking
 *               the last row turn it off. button: false leaves the button out, for an app that puts its own (e.g. in its bar) and calls choose().
 *   shape       'round' | 'square'   the shape of every mark in the table (avatars, the category square, the check box), so one list never mixes them
 *   onRowClick  (row) => void   what a click on the row does (instead of folding the detail)
 *   empty       { icon?, title, text?, action? }   what stands in place of the table when there is no row
 * Returns { el, set({ rows?, groups? }), flash(key), selected(), clearSelection(), choose(on), choosing(), destroy() }; set() draws again and keeps what is open and what is selected.
 */
export function table(options) {
  const { label, columns, rowKey = (r) => r.id, actions, active, detail, selection, onRowClick, empty, shape } = options;
  const onDemand = !!selection && selection.mode === 'button';
  let rows = options.rows || [], groups = options.groups || null;
  const open = new Set(), shut = new Set(), selected = new Set();
  let selecting = false, bar = null, byKey = new Map();
  const phoneOf = (c, i) => c.phone || (i === 0 ? 'title' : 'sub');
  const fromOf = (c) => (['m', 'l', 'xl'].includes(c.from) ? 't-from-' + c.from : '');
  const colCount = (selection ? 1 : 0) + columns.length + (actions ? 1 : 0) + (active ? 1 : 0);
  const hasKids = (r) => Array.isArray(r.children) && r.children.length > 0;
  const deepKids = (r) => hasKids(r) || (Array.isArray(r.children) && r.children.some(deepKids));
  const treeFolds = () => !!detail || (groups ? groups.some((g) => g.rows.some(deepKids)) : rows.some(deepKids));
  const cellOf = (c, row) => content(c.render ? c.render(row) : row[c.key]);

  const selectAll = h('input', { class: 't-check', type: 'checkbox', 'aria-label': t('table.selectAll'), onchange: (e) => {
    if (e.currentTarget.checked) for (const k of byKey.keys()) selected.add(k); else selected.clear();
    if (onDemand && !selected.size) { setSelecting(false); return; }
    render(); syncBar();
  } });
  const head = h('tr', null,
    selection ? h('th', { class: 'c-sel', scope: 'col' }, selectAll) : null,
    columns.map((c, i) => h('th', { scope: 'col', class: `${i === 0 ? 'c-title' : ''} ${c.align === 'end' ? 'r' : ''} ${phoneOf(c, i) === 'hide' ? 'm-hide' : ''} ${i === 0 ? '' : fromOf(c)}`.trim(), text: c.label })),
    actions ? h('th', { class: 'c-act', scope: 'col' }, h('span', { class: 'sr-only', text: t('table.actions') })) : null,
    active ? h('th', { class: 'c-active', scope: 'col', text: active.label || t('table.active') }) : null);
  const tbody = h('tbody');
  const selectBtn = selection && selection.button !== false ? h('button', { class: 'btn mini', type: 'button', 'aria-pressed': 'false', onclick: () => setSelecting(!selecting) }) : null;
  const tools = selectBtn ? h('div', { class: 'table-tools' }, selectBtn) : null;
  const tableEl = h('table', { class: 'table rich', 'aria-label': label }, h('thead', null, head), tbody);
  const emptyEl = empty ? h('div', { class: 'table-empty', hidden: true }, emptyState(empty)) : null;
  const el = h('div', { class: `panel table-panel${selection ? ' has-select' : ''}${onDemand ? ' on-demand' : ''}${shape === 'round' || shape === 'square' ? ' marks-' + shape : ''}` }, tools, h('div', { class: 'table-wrap' }, tableEl), emptyEl);

  // Esc leaves choosing (not when it closes a menu or a list of choices first)
  const onKey = (e) => { if (e.key === 'Escape' && !e.defaultPrevented && !(e.target.closest && e.target.closest('.menu, .dd-list, .dialog'))) setSelecting(false); };
  function setSelecting(on) {
    if (selecting === on) return;
    selecting = on;
    el.classList.toggle('selecting', on);
    if (on) document.addEventListener('keydown', onKey); else document.removeEventListener('keydown', onKey);
    if (!on) { selected.clear(); render(); syncBar(); }
    paintTools();
  }
  function paintTools() {
    if (!selectBtn) return;
    selectBtn.textContent = selecting ? t('table.selectDone') : t('table.select');
    selectBtn.setAttribute('aria-pressed', String(selecting));
  }
  function syncBar() {
    if (!selection) return;
    const n = selected.size;
    selectAll.checked = n > 0 && n === byKey.size;
    selectAll.indeterminate = n > 0 && n < byKey.size;
    if (!n) { if (bar) { bar.close(); bar = null; } return; }
    if (bar) { bar.update({ count: n }); return; }
    bar = selectionBar({
      count: n,
      actions: selection.actions.map((a) => ({ ...a, run: () => a.run([...selected].map((k) => byKey.get(k)).filter(Boolean), api) })),
      onClear: () => { bar = null; selected.clear(); if (onDemand) setSelecting(false); else render(); },
    });
  }
  function toggleSelected(key) {
    if (selected.has(key)) selected.delete(key); else selected.add(key);
    if (onDemand && !selected.size) { setSelecting(false); return; }     // unticking the last row leaves choosing
    render({ focus: key + '|check' }); syncBar();
  }

  /* one row of data (and below it, in order, its detail row and its children) */
  function addRow(out, row, ctx) {
    const key = String(rowKey(row));
    byKey.set(key, row);
    const kids = hasKids(row), isOpen = open.has(key), folded = kids && shut.has(key);
    const canFold = kids || !!detail;
    const off = ctx.off || (active && !active.get(row));
    const clickable = !!(onRowClick || (detail && !kids) || selection);
    const tr = h('tr', { class: `t-row${off ? ' off' : ''}${selected.has(key) ? ' selected' : ''}${isOpen && !kids ? ' opened' : ''}${clickable && (onRowClick || detail) ? ' clickable' : ''}${ctx.child ? ' t-child' : ''}${kids && !folded ? ' t-open' : ''}`, dataset: { key } });
    tr.style.setProperty('--depth', ctx.lines.length);
    if (selection) tr.append(h('td', { class: 'c-sel' }, h('input', { class: 't-check', type: 'checkbox', checked: selected.has(key), 'aria-label': t('table.selectRow'), dataset: { focus: key + '|check' }, onchange: () => toggleSelected(key) })));
    const sub = columns.filter((c, i) => phoneOf(c, i) === 'sub');
    columns.forEach((c, i) => {
      const td = h('td', { class: `${i === 0 ? 'c-title' : 'c-col'} ${c.align === 'end' ? 'r' : ''} ${phoneOf(c, i) === 'hide' || phoneOf(c, i) === 'sub' ? 'm-hide' : ''} ${i === 0 ? '' : fromOf(c)}`.trim() });
      if (i === 0) {
        const name = h('div', { class: 't-name' }, ctx.lines.map((l) => h('span', { class: `t-ln ${l}`, 'aria-hidden': 'true' })));
        if (canFold) {
          const opened = kids ? !folded : isOpen;
          name.append(h('button', { class: 't-fold', type: 'button', 'aria-expanded': String(opened), 'aria-label': opened ? t('table.close') : t('table.open'), dataset: { focus: key + '|fold' }, onclick: () => toggleFold(key, kids) }, icon('down')));
        } else if (treeFolds()) name.append(h('span', { class: 't-fold-gap', 'aria-hidden': 'true' }));
        name.append(h('span', { class: 't-text' }, cellOf(c, row)));
        td.append(name);
        const mobileSub = sub.map((s) => cellOf(s, row)).filter((v) => v !== null && v !== '');
        if (mobileSub.length) td.append(h('div', { class: 't-sub-m' }, mobileSub.map((v) => h('span', null, v))));
      } else td.append(cellOf(c, row));
      tr.append(td);
    });
    if (actions) tr.append(actionCell(row));
    if (active) tr.append(h('td', { class: 'c-active' }, activeSwitch(row)));
    tr.addEventListener('click', (e) => rowClick(e, row, key, kids));
    if (selection) longPress(tr, key);
    out.push(tr);
    if (detail && !kids) {
      const dtr = h('tr', { class: 't-detail', hidden: !isOpen, dataset: { for: key } }, h('td', { colspan: colCount }, h('div', { class: 't-detail-body' }, isOpen ? detail(row) : null)));
      dtr._row = row;
      out.push(dtr);
    }
    if (kids && !folded) row.children.forEach((child, i) => addRow(out, child, { off, lines: [...ctx.lines.slice(0, -1), ctx.lines.length ? (ctx.last ? 'blank' : 'pass') : null, i === row.children.length - 1 ? 'end' : 'mid'].filter(Boolean), last: i === row.children.length - 1, child: true }));
  }
  function addRows(out, list, ctx) {
    list.forEach((row, i) => addRow(out, row, { off: ctx.off, lines: ctx.root ? [] : [ctx.in ? (i === list.length - 1 ? 'end' : 'mid') : null].filter(Boolean), last: i === list.length - 1 }));
  }
  function addGroup(out, g) {
    const key = 'g:' + g.id, folded = shut.has(key), off = g.active === false;
    const menuItems = typeof g.menu === 'function' ? g.menu(g) : g.menu;
    const sw = g.active === undefined ? null : h('label', { class: 'switch t-switch' }, h('input', { type: 'checkbox', role: 'switch', checked: g.active, 'aria-label': g.activeLabel || g.title, onchange: (e) => g.onActive && g.onActive(e.currentTarget.checked, g) }), h('span', { class: 'switch-track' }));
    out.push(h('tr', { class: `t-group${off ? ' off' : ''}${!folded && g.rows.length ? ' t-open' : ''}`, dataset: { key } }, h('td', { colspan: colCount }, h('div', { class: 't-ghead' },
      h('button', { class: 't-fold', type: 'button', 'aria-expanded': String(!folded), 'aria-label': (folded ? t('table.open') : t('table.close')) + ': ' + (typeof g.title === 'string' ? g.title : ''), dataset: { focus: key + '|fold' }, onclick: () => toggleFold(key, true) }, icon('down')),
      h('span', { class: 't-gname' }, content(g.title)), g.count !== undefined ? h('span', { class: 't-gcount', text: String(g.count) }) : null, h('span', { class: 'grow' }),
      g.onAdd ? h('button', { class: 'btn mini quiet icon-only', type: 'button', 'aria-label': g.addLabel || t('table.add'), onclick: () => g.onAdd(g) }, icon('plus')) : null,
      menuItems ? h('button', { class: 'btn mini quiet icon-only', type: 'button', 'aria-label': t('table.more'), 'aria-haspopup': 'menu', onclick: (e) => openMenu(e.currentTarget, menuItems) }, icon('more')) : null, sw))));
    if (folded) { collectKeys(g.rows); return; }
    g.rows.forEach((row, i) => addRow(out, row, { off, lines: [i === g.rows.length - 1 ? 'end' : 'mid'], last: i === g.rows.length - 1 }));
  }
  function collectKeys(list) { for (const r of list) { byKey.set(String(rowKey(r)), r); if (r.children) collectKeys(r.children); } }

  function actionCell(row) {
    const list = (actions(row) || []).filter(Boolean);
    const main = list.find((a) => a.kind === 'main');
    const rest = list.filter((a) => a !== main);
    const button = (a, quiet) => h('button', { class: `btn mini ${quiet ? `quiet icon-only${a.kind === 'danger' ? ' danger-ink' : ''}` : ''}`.trim(), type: 'button', 'aria-label': quiet ? a.label : null, title: quiet ? a.label : null, onclick: (e) => a.run(row, e.currentTarget) }, a.icon ? icon(a.icon) : null, quiet ? null : a.label);
    const wide = h('div', { class: 't-acts' }, main ? button(main, false) : null, rest.map((a) => button(a, true)));
    // phone: the switch if there is one, else the main action; everything else in a menu
    const keep = active ? null : main, inMenu = active ? list : rest;
    const phone = h('div', { class: 't-acts-m' }, keep ? button(keep, false) : null,
      inMenu.length ? h('button', { class: 'btn mini quiet icon-only', type: 'button', 'aria-label': t('table.more'), 'aria-haspopup': 'menu', onclick: (e) => openMenu(e.currentTarget, inMenu.map((a) => ({ label: a.label, icon: a.icon, danger: a.kind === 'danger', run: () => a.run(row, e.currentTarget) }))) }, icon('more')) : null);
    return h('td', { class: 'c-act' }, wide, phone);
  }
  function activeSwitch(row) {
    const input = h('input', { type: 'checkbox', role: 'switch', checked: !!active.get(row), 'aria-label': active.label || t('table.active') });
    input.addEventListener('change', async () => {
      const on = input.checked;
      try { await active.set(row, on); }
      catch (err) { input.checked = !on; if (!(err instanceof ApiError)) throw err; toast(errorText(err), { kind: 'danger' }); }
    });
    return h('label', { class: 'switch t-switch' }, input, h('span', { class: 'switch-track' }));
  }

  function toggleFold(key, tree) {
    const wasOpen = tree ? !shut.has(key) : open.has(key);
    if (tree) { if (wasOpen) shut.add(key); else shut.delete(key); }
    else if (wasOpen) open.delete(key); else open.add(key);
    if (tree) { render({ focus: key + '|fold' }); return; }
    // a detail row opens in place: nothing is rebuilt
    const tr = tbody.querySelector(`tr.t-row[data-key="${CSS.escape(key)}"]`), d = tbody.querySelector(`tr.t-detail[data-for="${CSS.escape(key)}"]`);
    if (!tr || !d) return;
    if (!wasOpen && !d.firstElementChild.firstElementChild.firstChild) d.firstElementChild.firstElementChild.append(content(detail(byKey.get(key))) || '');
    d.hidden = wasOpen;
    tr.classList.toggle('opened', !wasOpen);
    const b = tr.querySelector('.t-fold');
    b.setAttribute('aria-expanded', String(!wasOpen));
    b.setAttribute('aria-label', wasOpen ? t('table.open') : t('table.close'));
  }
  let pressed = false;
  function rowClick(e, row, key, kids) {
    if (pressed) { pressed = false; return; }
    if (e.target.closest('button, a, input, label, .t-acts, .t-acts-m')) return;
    if (selecting) return toggleSelected(key);
    if (onRowClick) onRowClick(row);
    else if (detail && !kids) toggleFold(key, false);
  }
  /* a long press on a phone starts the selection */
  function longPress(tr, key) {
    let timer = 0, x = 0, y = 0;
    const stop = () => clearTimeout(timer);
    tr.addEventListener('pointerdown', (e) => {
      if (e.pointerType !== 'touch' || e.target.closest('button, a, input, label')) return;
      x = e.clientX; y = e.clientY;
      timer = setTimeout(() => { pressed = true; setSelecting(true); selected.add(key); render({ focus: key + '|check' }); syncBar(); if (navigator.vibrate) navigator.vibrate(12); }, 500);
    });
    tr.addEventListener('pointermove', (e) => { if (Math.hypot(e.clientX - x, e.clientY - y) > 8) stop(); });
    for (const ev of ['pointerup', 'pointercancel', 'pointerleave']) tr.addEventListener(ev, stop);
    tr.addEventListener('contextmenu', (e) => { if (selection && e.pointerType !== 'mouse' && el.clientWidth < 640) e.preventDefault(); });     // two-line rows (the container query)
  }

  function render({ focus } = {}) {
    byKey = new Map();
    const out = [];
    if (groups) groups.forEach((g) => addGroup(out, g)); else addRows(out, rows, { root: true });
    for (const k of [...selected]) if (!byKey.has(k)) selected.delete(k);
    tbody.replaceChildren(...out);
    const none = byKey.size === 0 && !(groups && groups.length);
    tableEl.hidden = none && !!empty;
    if (emptyEl) emptyEl.hidden = !none;
    el.classList.toggle('has-folds', treeFolds());
    paintTools();
    if (focus) { const target = [...tbody.querySelectorAll('[data-focus]')].find((n) => n.dataset.focus === focus); if (target) target.focus({ preventScroll: true }); }
    if (selection) { selectAll.checked = selected.size > 0 && selected.size === byKey.size; selectAll.indeterminate = selected.size > 0 && selected.size < byKey.size; }
  }
  const api = {
    el,
    set(next) { if (next.rows) rows = next.rows; if (next.groups) groups = next.groups; render(); syncBar(); },
    flash(key) { return flash(tbody.querySelector(`tr.t-row[data-key="${CSS.escape(String(key))}"]`)); },
    selected: () => [...selected].map((k) => byKey.get(k)).filter(Boolean),
    clearSelection() { selected.clear(); if (onDemand) setSelecting(false); else { render(); syncBar(); } },
    choose(on = !selecting) { setSelecting(!!on); },
    choosing: () => selecting,
    destroy() { if (bar) { bar.close(); bar = null; } document.removeEventListener('keydown', onKey); },
  };
  window.addEventListener('hashchange', api.destroy, { once: true });     // the selection bar belongs to the page: it goes with it
  render();
  return api;
}
