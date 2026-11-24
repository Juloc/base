/* The start app: a list of things to remember. It shows the pattern of every app on the base:
 *   texts through t() (I18N-01), the frame from shell(), pages from router(), data through api(), forms with field() and submit(),
 *   a dialog for adding and editing, a confirmation before deleting, an empty state, settings that follow the person.
 * Replace the list by what the app is for; keep the pattern. */
import { addMessages, t, fmtAgo, fmtDate } from '/base/i18n.js';
import { h, icon, api, shell, router, openDialog, confirmDialog, toast, field, switchField, formValues, showErrors, clearErrors, emptyState, badge, prefs, errorText, ApiError } from '/base/base.js';
import { start } from '/base/auth.js';
import { appearanceCard, securityCard, devicesCard, historyCard, installationCard, aboutCard } from '/base/account.js';
import { peopleCard } from '/base/people.js';
import { hubCard } from '/base/hub.js';
import de from '/i18n/de.js';
import en from '/i18n/en.js';

addMessages('de', de);
addMessages('en', en);
if ('serviceWorker' in navigator) navigator.serviceWorker.register('/sw.js').catch(() => { /* the app works without it */ });

start({ icon: 'bookmark', onSignedIn: run });

function run(me) {
  const frame = shell({
    app: { name: me.app.name, icon: 'bookmark' },
    nav: [
      { label: t('nav.items'), icon: 'list', href: '#/' },
      { label: t('shell.settings'), icon: 'sliders', href: '#/settings' },
    ],
    person: { name: me.user },
    menu: [{ label: t('shell.settings'), icon: 'sliders', href: '#/settings' }],
    // somebody who came through the central sign-in is sent on to its sign-out, which ends its own session and tells the other apps
    onSignOut: async () => { const r = await api('POST', '/api/auth/logout'); if (r && r.hubLogoutUrl) location.assign(r.hubLogoutUrl); else location.reload(); },
  });
  document.body.append(frame.el);
  const pages = router({ '/': listPage, '/items/:id': itemPage, '/settings': () => settingsPage(me) }, frame);

  /* ---------- the list ---------- */
  async function listPage() {
    let query = '', next = null;
    const list = h('ul', { class: 'list' });
    const holder = h('div', { class: 'card flush' }, list);
    const more = h('button', { class: 'btn ghost wide', type: 'button', text: t('common.more'), hidden: true, onclick: () => load(false) });

    async function load(fresh) {
      const hide = prefs.get('items.hideDone', false);
      const url = `/api/items?limit=30&open=${hide}&q=${encodeURIComponent(query)}` + (!fresh && next ? `&cursor=${encodeURIComponent(next)}` : '');
      const page = await api('GET', url);
      if (fresh) list.replaceChildren();
      list.append(...page.items.map(row));
      next = page.next || null;
      more.hidden = !next;
      const empty = !list.children.length;
      holder.replaceChildren(empty
        ? (query || hide ? emptyState({ icon: 'search', title: t('items.none.title'), text: t('items.none.text') })
                         : emptyState({ icon: 'bookmark', title: t('items.empty.title'), text: t('items.empty.text'), action: { label: t('items.new'), icon: 'plus', run: () => edit(null) } }))
        : list);
    }

    function row(item) {
      const tick = h('button', { class: 'tick', type: 'button', 'aria-pressed': String(item.done), 'aria-label': item.done ? t('items.markOpen') : t('items.markDone') }, icon(item.done ? 'check-circle' : 'circle'));
      const li = h('li', null, h('div', { class: 'row item' + (item.done ? ' done' : '') }, tick,
        h('a', { class: 'row-main', href: '#/items/' + item.id },
          h('span', { class: 'row-title', text: item.title }),
          h('span', { class: 'row-sub', text: item.note ? item.note.split('\n')[0] : fmtAgo(item.createdAt) })),
        h('span', { class: 'row-end' }, icon('right'))));
      tick.addEventListener('click', async () => {
        try { li.replaceWith(row(await api('PUT', '/api/items/' + item.id, { title: item.title, note: item.note, done: !item.done }))); }
        catch (e) { toast(errorText(e), { kind: 'danger' }); }
      });
      return li;
    }

    let timer;
    const search = field({ label: t('common.search'), name: 'q', type: 'search', placeholder: t('items.search'), autocomplete: 'off' });
    search.querySelector('label').classList.add('sr-only');
    search.querySelector('input').addEventListener('input', (e) => { clearTimeout(timer); timer = setTimeout(() => { query = e.target.value.trim(); load(true); }, 250); });
    const hideDone = switchField({ label: t('items.hideDone'), name: 'hideDone', checked: prefs.get('items.hideDone', false), onchange: async (e) => {
      try { await prefs.set('items.hideDone', e.target.checked); await load(true); } catch (err) { toast(errorText(err), { kind: 'danger' }); }
    } });
    hideDone.classList.remove('reverse');

    await load(true);
    return {
      title: t('items.title'),
      actions: [h('button', { class: 'btn primary', type: 'button', onclick: () => edit(null) }, icon('plus'), t('items.new'))],
      body: h('div', { class: 'page stack' }, h('div', { class: 'toolbar' }, search, hideDone), holder, more),
    };
  }

  /* ---------- one entry ---------- */
  async function itemPage({ id }) {
    const item = await api('GET', '/api/items/' + encodeURIComponent(id));
    const toggle = async () => { await api('PUT', '/api/items/' + item.id, { title: item.title, note: item.note, done: !item.done }); pages.refresh(); };
    const remove = async () => {
      if (!await confirmDialog({ title: t('items.deleteTitle'), text: t('items.deleteText', { title: item.title }), confirmLabel: t('common.delete'), danger: true })) return;
      try { await api('DELETE', '/api/items/' + item.id); toast(t('items.deleted'), { kind: 'ok' }); pages.go('/'); }
      catch (e) { toast(errorText(e), { kind: 'danger' }); }
    };
    return {
      title: item.title,
      back: '#/',
      actions: [h('button', { class: 'btn ghost icon-only', type: 'button', 'aria-label': t('common.edit'), onclick: () => edit(item) }, icon('edit'))],
      body: h('div', { class: 'page narrow stack' },
        h('section', { class: 'card stack' },
          item.note ? h('p', { class: 'pre', text: item.note }) : h('p', { class: 'muted', text: t('items.noNote') }),
          h('dl', { class: 'facts' },
            h('dt', { text: t('items.state') }), h('dd', null, item.done ? badge(t('items.done'), 'ok') : badge(t('items.open'), 'accent')),
            h('dt', { text: t('items.created') }), h('dd', { text: fmtDate(item.createdAt) }),
            h('dt', { text: t('items.updated') }), h('dd', { text: fmtDate(item.updatedAt) }))),
        h('div', { class: 'cluster between' },
          h('button', { class: 'btn ghost danger', type: 'button', onclick: remove }, icon('trash'), t('common.delete')),
          h('button', { class: 'btn ' + (item.done ? 'outline' : 'primary'), type: 'button', onclick: toggle }, icon(item.done ? 'refresh' : 'check'), item.done ? t('items.markOpen') : t('items.markDone')))),
    };
  }

  /* ---------- add and edit: one form in a dialog ---------- */
  function edit(item) {
    const form = h('form', { class: 'form' },
      field({ label: t('items.field.title'), name: 'title', value: item ? item.title : '', maxlength: 200, required: true, autocomplete: 'off' }),
      field({ label: t('items.field.note'), name: 'note', type: 'textarea', value: item ? item.note : '', hint: t('items.field.noteHint'), maxlength: 4000 }));
    const save = async () => {
      clearErrors(form);
      try {
        if (item) await api('PUT', '/api/items/' + item.id, { ...formValues(form), done: item.done });
        else await api('POST', '/api/items', formValues(form));
      } catch (e) { if (!(e instanceof ApiError)) throw e; showErrors(form, e); return false; }
      toast(item ? t('items.saved') : t('items.added'), { kind: 'ok' });
      pages.refresh();
    };
    const dialog = openDialog({
      title: item ? t('items.edit') : t('items.new'), body: form,
      actions: [{ label: t('common.cancel'), kind: 'ghost' }, { label: item ? t('common.save') : t('items.add'), kind: 'primary', run: save }],
    });
    // Enter in the title field saves, as a form does
    form.addEventListener('submit', (e) => { e.preventDefault(); dialog.el.querySelector('.dialog-foot .btn.primary').click(); });
  }

  /* ---------- settings: the cards of the base, with the app's own in between ---------- */
  function settingsPage(me) {
    const own = h('section', { class: 'card stack' }, h('h2', { class: 'card-title', text: t('settings.list') }),
      switchField({ label: t('items.hideDone'), hint: t('settings.hideDoneHint'), name: 'hideDone', checked: prefs.get('items.hideDone', false),
        onchange: (e) => prefs.set('items.hideDone', e.target.checked).catch((err) => toast(errorText(err), { kind: 'danger' })) }));
    return {
      title: t('settings.title'),
      body: h('div', { class: 'page narrow stack l' }, appearanceCard(), own, securityCard(me), devicesCard(), historyCard(),
        me.roles.includes('admin') ? peopleCard(me) : null, me.roles.includes('admin') ? hubCard(me) : null,
        me.roles.includes('admin') ? installationCard() : null, aboutCard(me)),
    };
  }
}
