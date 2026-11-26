/* The script of the catalogue page: shows the code of every example (read from the example itself), runs the examples that are
 * JavaScript, and lets one try light, dark and any accent hue. English only: the page is for people who build apps. */
import { h, icon, hydrateIcons, toast, openDialog, confirmDialog, openMenu, floatBar, actionBar, selectionBar, navBadge, dropdown, choiceTiles, switchField, field, passwordField, submit, segmented, theme, badge, ApiError } from './base.js';
import { table, tabs, chips, numberBox, numberStrip, chart, liveFeed, timeline, ring, foldCard, flash } from './parts.js';
import { GLYPHS } from './icons.js';

/* ---------- the examples that are JavaScript: the source shown is the function itself ---------- */
const demos = {
  toast() {
    toast('Entry saved', { kind: 'ok' });
  },
  toastUndo() {
    toast('Entry deleted', { action: { label: 'Undo', run: () => toast('Restored', { kind: 'ok' }) } });
  },
  toastError() {
    toast('The server cannot be reached. Check the connection.', { kind: 'danger' });
  },
  dialog() {
    openDialog({
      title: 'Edit entry',
      body: h('form', { class: 'form' },
        field({ label: 'Title', name: 'title', value: 'Buy milk', placeholder: 'What is it about?' }),
        field({ label: 'List', name: 'list', type: 'select', value: 'home', options: [['home', 'Home'], ['work', 'Work'], ['later', 'Later']] }),
        field({ label: 'Note', name: 'note', type: 'textarea', placeholder: 'Anything to remember' })),
      actions: [
        { label: 'Delete', kind: 'danger', side: 'start', run: () => { toast('Entry deleted'); } },
        { label: 'Cancel', kind: 'ghost' },
        { label: 'Save', kind: 'primary', run: () => { toast('Entry saved', { kind: 'ok' }); } },
      ],
    });
  },
  dialogColumns() {
    openDialog({
      title: 'New route',
      size: 'l',
      body: h('div', { class: 'dialog-cols' },
        h('div', { class: 'dialog-col' },
          field({ label: 'Name', name: 'name', placeholder: 'app.example.com' }),
          field({ label: 'Target', name: 'target', placeholder: '192.0.2.10:8080', hint: 'Host and port.' }),
          switchField({ label: 'Active', hint: 'A route that is off is kept but not served.', name: 'active', checked: true })),
        h('div', { class: 'dialog-col' },
          choiceTiles({ name: 'mode', label: 'Mode', value: 'proxy', options: [['proxy', 'Forward', 'Pass the request on.'], ['static', 'Files', 'Serve a folder.']] }),
          field({ label: 'Certificate', name: 'cert', type: 'select', value: 'auto', options: [['auto', 'Automatic'], ['own', 'Own certificate'], ['none', 'None']] }))),
      actions: [{ label: 'Cancel', kind: 'ghost' }, { label: 'Add route', kind: 'primary', run: () => { toast('Route added', { kind: 'ok' }); } }],
    });
  },
  async ask() {
    const yes = await confirmDialog({ title: 'Delete this entry?', text: '“Buy milk” is deleted for good.', confirmLabel: 'Delete', danger: true });
    toast(yes ? 'Deleted' : 'Kept');
  },
  menu(button) {
    openMenu(button, [
      { heading: 'probe' },
      { label: 'Settings', icon: 'sliders', run: () => toast('Settings') },
      { label: 'Dark appearance', icon: 'moon', run: () => toast('Dark') },
      '-',
      { label: 'Sign out', icon: 'logout', danger: true, run: () => toast('Signed out') },
    ]);
  },
  floatBar() {
    floatBar({
      text: '3 changes waiting', icon: 'info',
      actions: [{ label: 'Discard', kind: 'ghost' }, { label: 'Apply changes', kind: 'primary', run: () => { toast('Applied', { kind: 'ok' }); } }],
    });
  },
  actionBar() {
    let n = 2;
    const bar = actionBar({
      actions: [{ label: 'Apply (2)', kind: 'primary', icon: 'check', run: () => { toast('Applied', { kind: 'ok' }); } }, { label: 'More', icon: 'plus', run: () => { n++; bar.update({ actions: [{ label: `Apply (${n})`, kind: 'primary', icon: 'check' }] }); return false; } }],
    });
  },
  selectionBar() {
    const bar = selectionBar({
      count: 3,
      actions: [{ label: 'Delete', icon: 'trash', kind: 'danger', run: () => { toast('Deleted'); bar.close(); } }, { label: 'More', icon: 'plus', run: () => bar.update({ count: 4 }) }],
      onClear: () => toast('Selection cleared'),
    });
  },
  dropdown() {
    return h('div', { class: 'stack' },
      field({ label: 'Language', name: 'lang', type: 'select', value: 'en', options: [['de', 'Deutsch'], ['en', 'English'], ['system', 'System']] }),
      h('div', { class: 'field' }, h('span', { class: 'field-label', text: 'Alone, with a hidden label' }),
        dropdown({ name: 'unit', label: 'Unit', value: '', placeholder: 'Choose a unit', options: [['s', 'Seconds'], ['m', 'Minutes'], ['h', 'Hours', 'longest']] })));
  },
  dropdownSearch() {
    const hosts = ['example.com', 'www.example.com', 'api.example.com', 'blog.example.org', 'cdn.example.net', 'mail.example.com', 'files.example.org', 'git.example.com', 'wiki.example.net', 'status.example.com', 'vpn.example.org', 'docs.example.com'];
    return field({ label: 'Host', name: 'host', type: 'select', value: 'example.com', options: hosts.map((x, i) => [x, x, String(100 - i * 7)]), onchange: (v) => toast(v) });
  },
  choiceTiles() {
    return choiceTiles({ name: 'when', label: 'When', value: 'now', options: [['now', 'Right away', 'The change goes live at once.'], ['later', 'At night', 'Applied at 03:00.'], ['ask', 'Ask me', 'You confirm each change.']], onchange: (v) => toast(v) });
  },
  navBadge() {
    const places = [['home', 'Overview'], ['list', 'Routes'], ['globe', 'Domains'], ['shield', 'Protection'], ['activity', 'Traffic'], ['database', 'Logs'], ['sliders', 'Settings']];
    const pill = h('nav', { class: 'shell-tabs', 'aria-label': 'Places' }, h('div', { class: 'tabs-scroll more-right' },
      places.map(([ic, label], i) => h('a', { class: 'tab', href: '#/demo-' + i, dataset: { nav: '#/demo-' + i }, 'aria-current': i === 0 ? 'page' : null },
        h('span', { class: 'tab-pill' }, icon(ic)), h('span', { text: label })))));
    navBadge('/demo-1', true);
    navBadge('/demo-3', 3);
    return pill;
  },
  tableFold() {
    const hosts = [
      { id: 1, name: 'app.example.com', target: '192.0.2.10:8080', state: 'Active', on: true, note: 'Certificate until 12 Mar. Last change by an administrator.' },
      { id: 2, name: 'blog.example.org', target: '192.0.2.11:2368', state: 'Waiting', on: true, note: 'Waits for the DNS record to appear.' },
      { id: 3, name: 'files.example.net', target: '192.0.2.12:9000', state: 'Active', on: false, note: 'Switched off; kept but not served.' },
    ];
    const t = table({
      label: 'Hosts',
      rows: hosts,
      columns: [{ key: 'name', label: 'Name', render: (r) => h('b', { text: r.name }) }, { key: 'target', label: 'Target', render: (r) => h('span', { class: 'mono', text: r.target }) }, { key: 'state', label: 'State', render: (r) => badge(r.state, r.state === 'Active' ? 'ok' : '') }],
      actions: (r) => [{ label: 'Renew', kind: 'main', run: () => { toast('Renewed ' + r.name, { kind: 'ok' }); t.flash(r.id); } }, { label: 'Edit', icon: 'edit', run: () => toast('Edit ' + r.name) }, { label: 'Delete', icon: 'trash', kind: 'danger', run: () => toast('Delete ' + r.name) }],
      active: { label: 'Active', get: (r) => r.on, set: async (r, on) => { r.on = on; t.set({ rows: hosts }); } },
      detail: (r) => h('p', { class: 'muted', text: r.note }),
      selection: { actions: [{ label: 'Delete', icon: 'trash', kind: 'danger', run: (rows, tb) => { toast(rows.length + ' deleted'); tb.clearSelection(); } }] },
    });
    return t.el;
  },
  tableTree() {
    const t = table({
      label: 'Routes by domain',
      columns: [{ key: 'name', label: 'Path' }, { key: 'target', label: 'Target', render: (r) => h('span', { class: 'mono', text: r.target }) }],
      groups: [
        { id: 'a', title: 'example.com', count: 3, onAdd: () => toast('Add a route'), menu: [{ label: 'Rename', icon: 'edit', run: () => toast('Rename') }], active: true, onActive: (on) => toast(on ? 'Domain on' : 'Domain off'),
          rows: [{ id: 'a1', name: '/', target: '192.0.2.10:8080' }, { id: 'a2', name: '/api', target: '192.0.2.10:9000', children: [{ id: 'a21', name: '/api/v1', target: '192.0.2.10:9001' }, { id: 'a22', name: '/api/v2', target: '192.0.2.10:9002' }] }, { id: 'a3', name: '/docs', target: '192.0.2.13:80' }] },
        { id: 'b', title: 'example.org', count: 1, onAdd: () => toast('Add a route'), active: false, rows: [{ id: 'b1', name: '/', target: '192.0.2.14:3000' }] },
      ],
      actions: () => [{ label: 'Edit', icon: 'edit', run: () => toast('Edit') }],
    });
    return t.el;
  },
  tabs() {
    const out = h('p', { class: 'muted', text: 'Chosen: week · all' });
    const state = { period: 'week', kind: 'all' };
    const show = () => { out.textContent = `Chosen: ${state.period} · ${state.kind}`; };
    return h('div', { class: 'stack' },
      tabs({ label: 'Sections', value: 'rules', items: [{ value: 'rules', label: 'Rules', icon: 'list' }, { value: 'blocks', label: 'Blocked', count: 12 }, { value: 'log', label: 'Log' }, { value: 'settings', label: 'Settings' }], onchange: (v) => toast(v) }),
      tabs({ label: 'Period', size: 's', value: 'week', items: [['hour', '1 hour'], ['day', '24 hours'], ['week', '7 days'], ['month', '30 days']].map(([value, label]) => ({ value, label })), onchange: (v) => { state.period = v; show(); } }),
      chips({ label: 'Kind', value: 'all', items: [['all', 'All'], ['ok', 'Success'], ['redirect', 'Redirects'], ['client', 'Client errors'], ['server', 'Server errors'], ['bots', 'Bots']].map(([value, label]) => ({ value, label })), onchange: (v) => { state.kind = v; show(); } }),
      out);
  },
  numbers() {
    return h('div', { class: 'stack' },
      numberStrip([
        numberBox({ icon: 'activity', value: 18420, label: 'Requests', change: { text: '12 %', dir: 'up', tone: 'ok' } }),
        numberBox({ icon: 'shield', value: 37, label: 'Blocked', change: { text: '5 %', dir: 'down' } }),
        numberBox({ icon: 'alert', value: 4, label: 'Errors', change: { text: '2', dir: 'up', tone: 'danger' } }),
        numberBox({ icon: 'clock', value: '48 ms', label: 'Median time' }),
      ], 'Today'),
      h('div', { class: 'cluster' }, ring(21, 30, '', { label: '21 of 30 days left' }), ring(9, 30, 'warn'), ring(2, 30, 'danger'), ring(30, 30, 'ok', { text: 'OK', label: 'valid' })));
  },
  chart() {
    const hours = Array.from({ length: 24 }, (_, i) => String(i).padStart(2, '0') + ':00');
    const wave = (i, k) => Math.round(40 + 30 * Math.sin(i / 3.2 + k) + (i * 7 % 11) * k);
    const data = hours.map((label, i) => ({ label, ok: wave(i, 1) + 30, redirect: wave(i, 2) / 3, client: (i * 5 % 7) + 1, server: i % 9 === 0 ? 6 : 0 }));
    const c = chart({ title: 'Requests per hour', data,
      series: [{ key: 'ok', label: 'Success', color: 'oklch(0.78 0.12 209)' }, { key: 'redirect', label: 'Redirects', color: 'oklch(0.9 0.05 209)' }, { key: 'client', label: 'Client errors', color: 'oklch(0.85 0.12 80)' }, { key: 'server', label: 'Server errors', color: 'oklch(0.6 0.2 5)' }] });
    return c.el;
  },
  feed() {
    const f = liveFeed({ title: 'Live', max: 8 });
    const rows = [['GET', '/index.html', 200, 'ok', 'info'], ['GET', '/missing', 404, 'warn', 'warn'], ['POST', '/api/login', 500, 'danger', 'danger'], ['GET', '/logo.svg', 304, 'muted', 'muted']];
    let n = 0;
    const add = () => { const [m, p, s, , tone] = rows[n++ % rows.length]; f.add({ title: h('span', null, h('b', { text: m + ' ' }), p), sub: 'app.example.com', time: new Date().toLocaleTimeString(), meta: String(s), tone, dotLabel: String(s) }); };
    add(); add(); add();
    const timer = setInterval(() => { if (!f.el.isConnected) clearInterval(timer); else add(); }, 2000);
    return f.el;
  },
  timeline() {
    return timeline({ label: 'Changes', days: [
      { label: 'Today', items: [
        { state: 'ok', title: 'Certificate renewed', time: '09:12', meta: 'app.example.com' },
        { state: 'warn', title: 'Renewal retried', time: '03:00', meta: 'blog.example.org', body: 'The provider answered slowly; the second try worked.' }] },
      { label: 'Yesterday', items: [{ state: 'danger', title: 'Record could not be set', time: '22:40', meta: 'files.example.net', body: 'The token of the provider was refused.' }] },
    ] });
  },
  foldCard() {
    return h('div', { class: 'stack' },
      foldCard({ title: 'example.com', sub: 'Certificate until 12 March', lead: ring(21, 30), actions: [h('button', { class: 'btn mini', type: 'button', text: 'Renew', onclick: () => toast('Renewed', { kind: 'ok' }) })], body: () => h('p', { class: 'muted', text: 'Made when the card is opened the first time.' }) }),
      foldCard({ title: 'example.org', sub: 'Opened to begin with', open: true, body: h('p', { class: 'muted', text: 'Any node can be the body.' }) }));
  },
  flash() {
    const box = h('div', { class: 'panel stack' }, h('p', { class: 'muted', text: 'Something happened here.' }));
    return h('div', { class: 'stack' }, h('button', { class: 'btn', type: 'button', text: 'Light it up', onclick: () => flash(box) }), box);
  },
  form() {
    // stands in for a request: the server answers with the fields that are wrong
    const save = async (values) => {
      await new Promise((done) => setTimeout(done, 500));
      const fields = {};
      if (!values.title) fields.title = 'required';
      if (values.password.length < 12) fields.password = 'password.short';
      if (Object.keys(fields).length) throw new ApiError(400, { error: 'invalid', fields });
      toast('Saved', { kind: 'ok' });
    };
    return submit(h('form', { class: 'form' },
      field({ label: 'Title', name: 'title', required: true }),
      passwordField({ label: 'Password', name: 'password', hint: 'At least 12 characters.' }),
      h('div', { class: 'form-actions' }, h('button', { class: 'btn primary', type: 'submit', text: 'Save' }))), save);
  },
};

/* ---------- code next to each example ---------- */
function dedent(text) {
  const lines = text.replace(/\t/g, '  ').split('\n');
  while (lines.length && !lines[0].trim()) lines.shift();
  while (lines.length && !lines[lines.length - 1].trim()) lines.pop();
  const indent = Math.min(...lines.filter((l) => l.trim()).map((l) => l.match(/^ */)[0].length));
  return lines.map((l) => l.slice(indent)).join('\n');
}
const code = (text) => h('pre', { class: 'cat-code' }, h('code', { text }));
const body = (fn) => dedent(fn.toString().replace(/^[^{]*\{/, '').replace(/\}\s*$/, ''));

for (const demo of document.querySelectorAll('.cat-demo')) {
  if (demo.dataset.nocode !== undefined) continue;
  const pair = h('div', { class: 'cat-pair' });
  demo.replaceWith(pair);
  if (demo.dataset.run) {
    const fn = demos[demo.dataset.run];
    if (demo.dataset.inline !== undefined) demo.append(fn());
    else demo.append(h('button', { class: 'btn', type: 'button', text: 'Try it', onclick: (e) => fn(e.currentTarget) }));
    pair.append(demo, code(body(fn)));
  } else {
    pair.append(demo, code(dedent(demo.innerHTML)));      // read before the icons are put in: this is the markup one writes
  }
}
hydrateIcons();

/* ---------- colours, sizes, icons ---------- */
const COLOURS = ['bg', 'surface', 'surface-1', 'surface-2', 'surface-3', 'surface-4', 'text', 'text-2', 'line', 'line-strong', 'hair', 'edge', 'seg-track', 'seg-on', 'accent-from-hue', 'accent', 'on-accent', 'accent-ink', 'accent-soft', 'on-accent-soft',
  'secondary-soft', 'on-secondary-soft', 'tertiary-soft', 'on-tertiary-soft', 'danger', 'on-danger', 'danger-soft', 'on-danger-soft', 'ok', 'ok-soft', 'on-ok-soft', 'warn', 'warn-soft', 'on-warn-soft'];
document.getElementById('cat-swatches').append(...COLOURS.map((name) => {
  const chip = h('i');
  chip.style.background = `var(--${name})`;
  return h('div', { class: 'swatch' }, chip, h('code', { class: 'mono', text: '--' + name }));
}));
const scale = document.getElementById('cat-scale');
for (const s of ['s1', 's2', 's3', 's4', 's5', 's6', 's7']) {
  const bar = h('div', { class: 'scale-bar' });
  bar.style.width = `var(--${s})`;
  scale.append(h('div', { class: 'scale-row' }, h('code', { class: 'mono', text: '--' + s }), bar));
}
for (const r of ['r-xs', 'r-s', 'r-m', 'r-row', 'r-btn', 'r-l', 'r-xl', 'r-full']) {
  const box = h('div', { class: 'radius-box' });
  box.style.borderRadius = `var(--${r})`;
  scale.append(h('div', { class: 'scale-row' }, h('code', { class: 'mono', text: '--' + r }), box));
}
document.getElementById('cat-icons').append(...Object.keys(GLYPHS).map((name) => h('div', { class: 'icon-cell' }, icon(name), name)));

/* ---------- the menu of the page, the scheme, the hue ---------- */
document.getElementById('cat-nav').append(...[...document.querySelectorAll('.cat-item')].map((s) => h('a', { class: 'nav-item', href: '#' + s.id, text: s.querySelector('h2').textContent })));
document.getElementById('cat-scheme').append(segmented({
  name: 'scheme', label: 'Scheme', value: theme.get(), onchange: (v) => theme.set(v),
  options: [['auto', 'System', 'monitor'], ['light', 'Light', 'sun'], ['dark', 'Dark', 'moon']],
}));
const hue = document.getElementById('cat-hue'), hueValue = document.getElementById('cat-hue-value');
const colour = document.getElementById('cat-color'), root = document.documentElement.style;
colour.addEventListener('input', () => root.setProperty('--accent-color', colour.value));
document.getElementById('cat-color-off').addEventListener('click', () => root.removeProperty('--accent-color'));
document.getElementById('cat-presets').append(...[['Cyan', '#00e5ff'], ['Yellow', '#eaff00'], ['Navy', '#1a237e'], ['Violet', '#7c4dff'], ['Orange', '#ff6f00'], ['Black', '#000000']].map(([label, value]) =>
  h('button', { class: 'btn mini', type: 'button', text: label + ' ' + value, onclick: () => { colour.value = value; root.setProperty('--accent-color', value); } })));
hue.addEventListener('input', () => { document.documentElement.style.setProperty('--accent-hue', hue.value); hueValue.textContent = hue.value; theme.apply(); });
fetch('/api/health').then((r) => r.json()).then((d) => { document.getElementById('cat-version').textContent = 'base ' + d.base; }).catch(() => { document.getElementById('cat-version').remove(); });
