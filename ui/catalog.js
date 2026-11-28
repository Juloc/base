/* The script of the catalogue page: shows the code of every example (read from the example itself), runs the examples that are
 * JavaScript, and lets one try light, dark and any accent hue. English only: the page is for people who build apps. */
import { h, icon, hydrateIcons, toast, openDialog, confirmDialog, openMenu, splitButton, floatBar, actionBar, selectionBar, navBadge, dropdown, choiceTiles, switchField, field, passwordField, submit, segmented, theme, badge, ApiError } from './base.js';
import { table, tabs, chips, numberBox, numberStrip, chart, liveFeed, timeline, ring, foldCard, flash, avatar, dayList, dayKey } from './parts.js';
import { authCard, stateCard } from './gate.js';
import { GLYPHS } from './icons.js';
import { fmtNumber } from './i18n.js';

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
        { label: 'Delete', icon: 'trash', kind: 'danger', side: 'start', run: () => { toast('Entry deleted'); } },
        { label: 'Cancel', kind: 'ghost' },
        { label: 'Save', icon: 'check', kind: 'primary', enter: true, run: () => { toast('Entry saved', { kind: 'ok' }); } },
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
      actions: [
        { label: 'Deactivate', icon: 'power', side: 'start', run: () => { toast('Route switched off'); } },
        { label: 'Cancel', kind: 'ghost' },
        { label: 'Save and apply', icon: 'check', kind: 'primary', run: () => { toast('Route saved and applied', { kind: 'ok' }); },
          more: [{ label: 'Only save', icon: 'download', run: () => { toast('Route saved, not applied yet'); } }] },
      ],
    });
  },
  split() {
    return h('div', { class: 'cluster' },
      splitButton({ label: 'Save and apply', icon: 'check', run: () => toast('Saved and applied', { kind: 'ok' }), more: [{ label: 'Only save', icon: 'download', run: () => toast('Saved') }, { label: 'Apply later', icon: 'clock', run: () => toast('Planned') }] }),
      splitButton({ label: 'Export', icon: 'download', kind: '', run: () => toast('CSV'), more: [{ label: 'As JSON', run: () => toast('JSON') }] }));
  },
  async ask() {
    const yes = await confirmDialog({ title: 'Delete this entry?', text: '“Buy milk” is deleted for good.', confirmLabel: 'Delete', icon: 'trash', danger: true });
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
    // built like the routes page of a real app: a block per domain, the domain's own address, its paths and subdomains under it, paths of a subdomain under that
    const R = (id, kind, name, target, more = {}) => ({ id, kind, name, target, hits: '', on: true, ...more });
    const host = (r) => {
      const sym = { host: 'globe', path: 'list', redirect: 'external', none: 'globe' }[r.kind];
      const [first, ...rest] = r.kind === 'path' ? [r.name] : r.name.split('.');
      return h('span', { class: 'cat-host' + (r.kind === 'none' ? ' quiet' : '') },
        avatar({ name: r.name, icon: sym, size: 's', shape: 'square' }),
        h('span', { class: 'cat-host-name' }, h('b', { class: r.kind === 'path' ? 'mono' : '', text: first }), rest.length ? h('span', { class: 'muted', text: '.' + rest.join('.') }) : null),
        r.lock ? h('span', { class: 'cat-host-mark', title: 'Sign-in required' }, icon('lock')) : null,
        r.cert ? badge(r.cert, 'warn') : null);
    };
    const target = (r) => r.kind === 'redirect' ? h('span', { class: 'cat-chip mono' }, icon('external'), h('span', { text: r.target })) : h('span', { class: 'mono muted', text: r.target || '' });
    const t = table({
      label: 'Routes by domain',
      columns: [{ key: 'name', label: 'Address', render: host }, { key: 'target', label: 'Target', render: target, phone: 'sub' }, { key: 'hits', label: 'Requests', align: 'end', phone: 'hide' }],
      groups: [
        { id: 'a', title: 'example.com', count: 7, onAdd: () => toast('Add a route'), menu: [{ label: 'Edit domain', icon: 'edit', run: () => toast('Edit') }], active: true, onActive: (on) => toast(on ? 'Domain on' : 'Domain off'),
          rows: [R('a0', 'host', 'example.com', '192.0.2.10:8080', { hits: '12 480', lock: true, children: [
            R('a1', 'path', '/api', '192.0.2.10:9000', { hits: '' }),
            R('a2', 'path', '/status', '192.0.2.10:9100', { hits: '' }),
            R('a3', 'host', 'app.example.com', '192.0.2.11:3000', { hits: '3 902', cert: '9 days', children: [R('a31', 'path', '/admin', '192.0.2.11:3001', { lock: true })] }),
            R('a4', 'redirect', 'www.example.com', '301 example.com', { hits: '611' }),
            R('a5', 'host', 'files.example.com', '192.0.2.12:9000', { hits: '87', on: false }),
          ] })] },
        { id: 'b', title: 'example.org', count: 1, onAdd: () => toast('Add a route'), active: true,
          rows: [R('b0', 'none', 'example.org', '', { children: [R('b1', 'host', 'blog.example.org', '192.0.2.14:2368', { hits: '1 204' })] })] },
      ],
      actions: (r) => r.kind === 'none' ? [{ label: 'Add a route', icon: 'plus', run: () => toast('Add') }] : [{ label: 'Traffic', icon: 'activity', run: () => toast('Traffic') }, { label: 'Edit', icon: 'edit', run: () => toast('Edit') }],
      active: { label: 'Active', get: (r) => r.on, set: async (r, on) => { r.on = on; toast(on ? 'On' : 'Off'); } },
      selection: { actions: [{ label: 'Delete', icon: 'trash', kind: 'danger', run: (rows, tb) => { toast(rows.length + ' deleted'); tb.clearSelection(); } }] },
    });
    return t.el;
  },
  avatars() {
    const picture = 'data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 40"><rect width="40" height="40" fill="#2f6f5e"/><circle cx="20" cy="16" r="7" fill="#f4e3c1"/><rect x="8" y="26" width="24" height="14" rx="7" fill="#f4e3c1"/></svg>');
    const row = (mark, name, what) => h('div', { class: 'row' }, mark, h('div', { class: 'row-main' }, h('b', { text: name }), h('span', { class: 'muted small', text: what })));
    return h('div', { class: 'stack' },
      h('div', { class: 'list' },
        row(avatar({ name: 'Corner bakery', image: picture }), 'Corner bakery', 'a picture the person chose'),
        row(avatar({ name: 'Market Hall', icon: 'cart', cat: 0 }), 'Market Hall', 'no picture: the category symbol on its colour'),
        row(avatar({ name: 'Bike shop', image: '/missing-logo.png', icon: 'tag', cat: 5 }), 'Bike shop', 'the picture did not load: the symbol'),
        row(avatar({ name: 'Zora Example' }), 'Zora Example', 'no picture, no symbol: the initial'),
        row(avatar({ name: 'Ölmühle', cat: 3 }), 'Ölmühle', 'an initial on a category colour')),
      h('div', { class: 'cluster' }, avatar({ name: 'Ada', size: 's' }), avatar({ name: 'Ada' }), avatar({ name: 'Ada', size: 'head', icon: 'bank', cat: 4, label: 'Bank account' }),
        avatar({ name: 'Ada', shape: 'square', icon: 'card', cat: 6 }), avatar({ name: 'Ada', size: 'head', shape: 'square', image: picture })));
  },
  tableChoose() {
    const rows = [
      { id: 1, who: 'Market Hall', what: 'Groceries', icon: 'cart', cat: 0, amount: '−42.80 €' },
      { id: 2, who: 'Corner Café', what: 'Eating out', icon: 'coffee', cat: 1, amount: '−6.40 €' },
      { id: 3, who: 'Example Energy', what: 'Home', icon: 'plug', cat: 2, amount: '−58.00 €' },
      { id: 4, who: 'Zora Example', what: 'Transfer', amount: '+120.00 €' },
    ];
    const t = table({
      label: 'Bookings',
      rows,
      shape: 'round',                                   // round marks: the avatars and the tick
      columns: [
        { key: 'who', label: 'Who', render: (r) => h('span', { class: 'cluster' }, avatar({ name: r.who, icon: r.icon, cat: r.cat }), h('b', { text: r.who })) },
        { key: 'what', label: 'Category' },
        { key: 'amount', label: 'Amount', align: 'end' },
      ],
      selection: { mode: 'button', actions: [{ label: 'Category', icon: 'tag', run: (picked) => toast(picked.length + ' to sort') }, { label: 'Delete', icon: 'trash', kind: 'danger', run: (picked, tb) => { toast(picked.length + ' deleted'); tb.clearSelection(); } }] },
    });
    return t.el;
  },
  tableRoom() {
    const people = [
      { id: 1, name: 'Ada Example', mail: 'ada@example.com', role: 'Admin', team: 'Platform', seen: 'today' },
      { id: 2, name: 'Ben Example', mail: 'ben@example.org', role: 'Member', team: 'Design', seen: 'yesterday' },
      { id: 3, name: 'Cleo Example', mail: 'cleo@example.net', role: 'Member', team: 'Support', seen: '3 days ago' },
    ];
    const t = table({
      label: 'People',
      rows: people,
      columns: [
        { key: 'name', label: 'Name', render: (r) => h('b', { text: r.name }) },
        { key: 'mail', label: 'Mail' },
        { key: 'role', label: 'Role' },
        { key: 'team', label: 'Team', from: 'm', phone: 'hide' },     // from 800 px of the table's own width
        { key: 'seen', label: 'Last seen', from: 'l', phone: 'hide' }, // from 1000 px
      ],
    });
    return h('div', { class: 'cat-resize' }, t.el);                 // drag the corner: the columns follow the box, not the window
  },
  tableTreeDeep() {
    const t = table({
      label: 'Folders',
      columns: [{ key: 'name', label: 'Name' }, { key: 'size', label: 'Size', align: 'end' }],
      rows: [
        { id: 'd', name: 'Documents', size: '3 files', children: [
          { id: 'd1', name: 'Invoices', size: '2 files', children: [{ id: 'd11', name: 'January.pdf', size: '84 kB' }, { id: 'd12', name: 'February.pdf', size: '91 kB' }] },
          { id: 'd2', name: 'Contract.pdf', size: '210 kB' }] },
        { id: 'p', name: 'Photos', size: '1 file', children: [{ id: 'p1', name: 'Garden.jpg', size: '2.4 MB' }] },
        { id: 'n', name: 'Notes.txt', size: '2 kB' },
      ],
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
  checks() {
    const some = h('input', { type: 'checkbox', 'aria-label': 'All rows' });
    some.indeterminate = true;                                  // set from code only: the head of a table when some rows are chosen
    return h('div', { class: 'stack' },
      h('label', { class: 'check' }, h('input', { type: 'checkbox' }), h('span', { text: 'Off' })),
      h('label', { class: 'check' }, h('input', { type: 'checkbox', checked: true }), h('span', { text: 'On' })),
      h('label', { class: 'check' }, some, h('span', { text: 'Some (indeterminate)' })),
      h('label', { class: 'check' }, h('input', { type: 'checkbox', checked: true, disabled: true }), h('span', { text: 'On, cannot be changed' })),
      h('label', { class: 'check' }, h('input', { type: 'checkbox', disabled: true }), h('span', { text: 'Off, cannot be changed' })));
  },
  categories() {
    const kinds = [['cat-0', 'cart', 'Groceries'], ['cat-1', 'fork', 'Eating out'], ['cat-2', 'house-loan', 'Home'], ['cat-3', 'gift', 'Presents'], ['cat-4', 'car', 'Transport'],
      ['cat-5', 'shirt', 'Clothes'], ['cat-6', 'ticket', 'Leisure'], ['cat-7', 'pill', 'Health'], ['cat-8', 'heart', 'Donations'], ['cat-9', 'tag', 'Other']];
    const strip = h('div', { class: 'cat-strip', 'aria-hidden': 'true' }, kinds.map(([cat]) => h('i', { class: cat })));
    return h('div', { class: 'stack' }, strip, h('div', { class: 'cat-cats' }, kinds.map(([cat, ic, name]) => h('div', { class: `cat-cat ${cat}` },
      h('span', { class: 'cat-mark cat-soft' }, icon(ic)),
      h('span', { class: 'cat-cat-name' }, h('span', { class: 'cat-dot' }), name),
      h('span', { class: 'chip cat-soft' }, icon(ic), name),
      h('code', { class: 'mono small', text: '--' + cat })))));
  },
  chartCategories() {
    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    const kinds = ['Groceries', 'Eating out', 'Home', 'Presents', 'Transport', 'Clothes', 'Leisure', 'Health', 'Donations', 'Other'];
    const data = months.map((label, m) => Object.fromEntries([['label', label], ...kinds.map((k, i) => [k, Math.round(40 + 25 * Math.sin(m / 2 + i) + (m * (i + 3)) % 17)])]));
    return chart({ title: 'Spending by category', data, format: (n) => n + ' €', series: kinds.map((k) => ({ key: k, label: k })) }).el;     // no colours: --cat-0, --cat-1, ...
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
  dayList() {
    // a made-up account: a few bookings per day, 30 days back and 10 ahead; a real app asks its server (GET /api/bookings?dir=older&cursor=...)
    const shops = [['Market Hall', 'cart', 0, 'Groceries'], ['Corner Café', 'coffee', 1, 'Eating out'], ['Example Energy', 'plug', 2, 'Home'], ['City Transit', 'bus', 4, 'Transport'], ['Zora Example', null, null, 'Transfer']];
    const bookingsOf = (offset) => Array.from({ length: (((offset * 7 + 11) % 4) + 4) % 4 }, (_, n) => {
      const [who, symbol, cat, what] = shops[(offset * 3 + n * 2 + 150) % shops.length];
      const date = new Date(); date.setDate(date.getDate() + offset); date.setHours(18 - n * 3, 0, 0, 0);
      return { id: `${offset}.${n}`, who, symbol, cat, what, at: date, cents: who === 'Zora Example' ? 12000 : -((offset * 37 + n * 113 + 9999) % 9000 + 250), planned: offset > 0 };
    });
    const load = async ({ dir, cursor }) => {
      await new Promise((done) => setTimeout(done, 400));
      let offset = cursor !== null ? Number(cursor) : dir === 'older' ? 0 : 1, items = [];
      while (items.length < 12 && offset >= -30 && offset <= 10) {
        const day = bookingsOf(offset);
        items.push(...(dir === 'older' ? day : day.reverse()));                   // older: newest first; newer: oldest first
        offset += dir === 'older' ? -1 : 1;
      }
      return { items, next: offset >= -30 && offset <= 10 ? String(offset) : null };
    };
    const euro = (cents) => (cents > 0 ? '+' : '') + fmtNumber(cents / 100, 2) + ' €';
    const list = dayList({
      label: 'Bookings',
      load,
      day: (b) => dayKey(b.at),
      row: (b) => ({ lead: avatar({ name: b.who, icon: b.symbol, cat: b.cat }), title: b.who, sub: b.planned ? b.what + ' · expected' : b.what, value: euro(b.cents), quiet: b.planned }),
      dayValue: (items) => euro(items.reduce((sum, b) => sum + b.cents, 0)),
      onRowClick: (b) => toast(b.who),
      selection: { actions: [{ label: 'Category', icon: 'tag', run: (picked) => toast(picked.length + ' to sort') }] },
      top: () => document.querySelector('.cat-head').offsetHeight,           // the catalogue's own bar; in an app the shell's bar is found by itself
    });
    return list.el;
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
  authCard() {
    return authCard({ icon: 'shield', title: 'Sign in', line: 'Proxy for example.com',
      body: [
        h('button', { class: 'btn neutral wide', type: 'button' }, icon('globe'), 'Sign in with the hub'),
        h('div', { class: 'auth-or', role: 'separator', text: 'or with your own account' }),
        h('form', { class: 'auth-form' },
          field({ label: 'User name', name: 'user', placeholder: 'User name', labelHidden: true }),
          passwordField({ label: 'Password', name: 'password', placeholder: 'Password', labelHidden: true }),
          field({ label: 'Code', name: 'code', placeholder: 'Code from the app (6 digits)', labelHidden: true, class: 'input code', inputmode: 'numeric', autocomplete: 'one-time-code' }),
          h('button', { class: 'btn primary wide', type: 'button', text: 'Sign in' }))] });
  },
  authTarget() {
    return authCard({ icon: 'shield', title: 'Sign in', line: 'This page is protected.', target: { host: 'notes.example.com' },
      body: h('button', { class: 'btn primary wide', type: 'button', text: 'Continue to notes.example.com' }) });
  },
  stateRun() {
    return stateCard({ icon: 'refresh', tone: 'run', title: 'Signed in', text: 'You are being sent to notes.example.com ...' });
  },
  stateDenied() {
    return stateCard({ icon: 'lock', tone: 'danger', title: 'Not allowed here', text: 'This account may not open notes.example.com. Sign in with another account.',
      actions: [{ label: 'Another account', run: () => toast('Back to the sign-in') }, { label: 'Back to the app', icon: 'home', href: '#statecard' }] });
  },
  stateExpired() {
    return stateCard({ icon: 'clock', tone: 'danger', title: 'Link expired', text: 'The sign-in link was valid for a short time only, or was used already. Just open the page again.',
      actions: [{ label: 'Open notes.example.com again', icon: 'refresh', run: () => toast('Opening ...') }] });
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
