/* The script of the catalogue page: shows the code of every example (read from the example itself), runs the examples that are
 * JavaScript, and lets one try light, dark and any accent hue. English only: the page is for people who build apps. */
import { h, icon, hydrateIcons, toast, openDialog, confirmDialog, openMenu, floatBar, actionBar, selectionBar, navBadge, dropdown, choiceTiles, switchField, field, passwordField, submit, segmented, theme, ApiError } from './base.js';
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
