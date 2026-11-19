(() => {
  'use strict';

  const dock = 'data-dukhi-inspector-open';
  const sessionKey = 'dukhi.inspector.tab';
  const tabs = { console: 'Консоль', elements: 'Элементы', network: 'Сеть' };
  const levels = { log: 'Лог', info: 'Инфо', debug: 'Отладка', warn: 'Внимание', error: 'Ошибка' };
  const logs = [];
  const requests = [];
  const filters = { console: '', network: '' };
  let tab = 'console';
  let host, body, actions, buttons, selection, picker, pickButton, highlight, pendingRender;

  function addEntry(entries, entry, kind) {
    entries.push(entry);
    if (entries.length > 300) entries.shift();
    if (host && tab === kind && !pendingRender) {
      pendingRender = requestAnimationFrame(() => {
        pendingRender = 0;
        if (host && tab !== 'elements') renderEntries();
      });
    }
  }

  document.addEventListener('dukhi:inspector-console', event => {
    if (typeof event.detail !== 'string' || event.detail.length > 30000) return;
    try {
      const [level, text] = JSON.parse(event.detail);
      if (Object.hasOwn(levels, level) && typeof text === 'string') {
        addEntry(logs, { level, text, time: new Date().toLocaleTimeString('ru-RU') }, 'console');
      }
    } catch {
      // Ignore malformed page events.
    }
  });

  function recordRequest(entry) {
    if (!/^https?:\/\//.test(entry.name)) return;
    const url = new URL(entry.name);
    addEntry(requests, {
      url: (url.origin + url.pathname + url.search).slice(0, 4096),
      kind: entry.initiatorType || 'document',
      status: entry.responseStatus || '—',
      ms: Math.round(entry.duration),
    }, 'network');
  }

  new PerformanceObserver(list => list.getEntries().forEach(recordRequest))
    .observe({ type: 'resource', buffered: true });
  window.addEventListener('load', () => {
    setTimeout(() => performance.getEntriesByType('navigation').forEach(recordRequest), 0);
  }, { once: true });

  function node(tag, text, className) {
    const el = document.createElement(tag);
    if (text != null) el.textContent = text;
    if (className) el.className = className;
    return el;
  }

  function button(text, action, className) {
    const el = node('button', text, className);
    el.type = 'button';
    el.addEventListener('click', action);
    return el;
  }

  function renderEntries() {
    const query = filters[tab].trim().toLowerCase();
    const oldScroll = body.scrollTop;
    const follow = body.scrollHeight - body.clientHeight - oldScroll < 24;
    const fragment = document.createDocumentFragment();
    if (tab === 'console') {
      for (const log of logs) {
        if (query && !`${levels[log.level]} ${log.text}`.toLowerCase().includes(query)) continue;
        const row = node('div', null, `log ${log.level}`);
        row.append(node('small', `${log.time} · ${levels[log.level]}`), node('pre', log.text));
        fragment.append(row);
      }
    } else {
      for (const request of requests) {
        const info = `${request.kind} · ${request.status} · ${request.ms} мс`;
        if (query && !`${info} ${request.url}`.toLowerCase().includes(query)) continue;
        const row = node('div', null, 'request');
        row.append(node('small', info), node('div', request.url));
        fragment.append(row);
      }
    }
    if (!fragment.childNodes.length) {
      fragment.append(node('p', query ? 'Совпадений нет' : 'Записей пока нет', 'empty'));
    }
    body.replaceChildren(fragment);
    body.scrollTop = follow ? body.scrollHeight : oldScroll;
  }

  function elementLabel(el) {
    const name = el.localName;
    const id = (el.getAttribute('id') || '').slice(0, 80);
    const classes = (el.getAttribute('class') || '').slice(0, 120).trim().split(/\s+/).filter(Boolean);
    return name + (id ? `#${id}` : '') + classes.slice(0, 3).map(value => `.${value}`).join('');
  }

  function renderElements() {
    if (!selection?.isConnected) selection = document.documentElement;
    const parent = selection.parentElement;
    const preview = Array.from(selection.attributes).slice(0, 20).map(({ name, value }) => {
      return ` ${name}="${name === 'value' ? '…' : value.slice(0, 160)}"`;
    }).join('');
    const rect = selection.getBoundingClientRect();
    body.replaceChildren();
    if (parent) body.append(button(`↑ ${elementLabel(parent)}`, () => select(parent), 'element'));
    body.append(node('pre', `<${selection.localName}${preview}>`, 'selected'));
    body.append(node('small', `${Math.round(rect.width)} × ${Math.round(rect.height)} px`, 'dimensions'));
    const children = Array.from(selection.children).filter(child => child !== host);
    if (children.length) body.append(node('p', 'Дочерние элементы', 'heading'));
    for (const child of children.slice(0, 150)) {
      body.append(button(elementLabel(child), () => select(child), 'element child'));
    }
    if (children.length > 150) body.append(node('p', `Показано 150 из ${children.length}. Остальные можно выбрать на странице.`, 'empty'));
  }

  function select(el) {
    selection = el;
    renderElements();
    body.scrollTop = 0;
  }

  function stopPicking() {
    picker?.abort();
    picker = null;
    pickButton?.setAttribute('aria-pressed', 'false');
    highlight?.remove();
    highlight = null;
  }

  function startPicking() {
    if (picker) {
      stopPicking();
      return;
    }
    picker = new AbortController();
    pickButton.setAttribute('aria-pressed', 'true');
    const options = { capture: true, signal: picker.signal };
    highlight = node('div', null, 'highlight');
    highlight.hidden = true;
    host.shadowRoot.append(highlight);
    const target = event => event.target instanceof Element && event.target !== host ? event.target : null;
    const suppress = event => {
      if (!target(event)) return;
      event.preventDefault();
      event.stopImmediatePropagation();
    };
    window.addEventListener('pointerover', event => {
      const el = target(event);
      highlight.hidden = !el;
      if (!el) return;
      const { x, y, width, height } = el.getBoundingClientRect();
      Object.assign(highlight.style, { left: `${x}px`, top: `${y}px`, width: `${width}px`, height: `${height}px` });
    }, options);
    for (const name of ['pointerdown', 'pointerup', 'mousedown', 'mouseup']) {
      window.addEventListener(name, suppress, options);
    }
    window.addEventListener('click', event => {
      const el = target(event);
      if (!el) return;
      suppress(event);
      stopPicking();
      select(el);
    }, options);
  }

  function saveState(value) {
    try { sessionStorage.setItem(sessionKey, value); } catch { /* Storage may be disabled. */ }
  }

  function showTab(kind) {
    stopPicking();
    tab = kind;
    saveState(kind);
    for (const [name, el] of Object.entries(buttons)) el.setAttribute('aria-selected', name === kind);
    body.setAttribute('aria-labelledby', `tab-${kind}`);
    if (tab === 'elements') {
      pickButton = button('Выбрать на странице', startPicking);
      pickButton.title = 'Нажмите на элемент. Esc отменяет выбор.';
      pickButton.setAttribute('aria-pressed', 'false');
      actions.replaceChildren(pickButton, button('Обновить', renderElements));
      renderElements();
    } else {
      const filter = node('input');
      filter.type = 'search';
      filter.placeholder = 'Фильтр';
      filter.setAttribute('aria-label', 'Фильтр записей');
      filter.value = filters[kind];
      filter.addEventListener('input', () => {
        filters[kind] = filter.value;
        renderEntries();
      });
      actions.replaceChildren(filter, button('Очистить', () => {
        (kind === 'console' ? logs : requests).length = 0;
        renderEntries();
      }));
      renderEntries();
    }
    body.scrollTop = tab === 'elements' ? 0 : body.scrollHeight;
  }

  function closePanel() {
    stopPicking();
    host.remove();
    host = null;
    document.documentElement.removeAttribute(dock);
    saveState('');
  }

  function openPanel(kind) {
    host = node('aside');
    host.id = 'dukhi-inspector';
    host.setAttribute('aria-label', 'Инспектор страницы AnimeSSS');
    const shadow = host.attachShadow({ mode: 'open' });
    shadow.adoptedStyleSheets = [sheet];
    const header = node('header');
    const bar = node('div', null, 'tabs');
    bar.setAttribute('role', 'tablist');
    buttons = Object.fromEntries(Object.entries(tabs).map(([name, label]) => {
      const el = button(label, () => showTab(name));
      el.id = `tab-${name}`;
      el.setAttribute('role', 'tab');
      el.setAttribute('aria-controls', 'inspector-view');
      bar.append(el);
      return [name, el];
    }));
    const close = button('×', closePanel, 'close');
    close.title = 'Закрыть (F12)';
    close.setAttribute('aria-label', 'Закрыть инспектор');
    header.append(bar, close);
    actions = node('div', null, 'actions');
    body = node('div', null, 'view');
    body.id = 'inspector-view';
    body.setAttribute('role', 'tabpanel');
    body.tabIndex = 0;
    const surface = node('div', null, 'surface');
    surface.append(header, actions, body);
    shadow.append(surface);
    document.documentElement.append(host);
    document.documentElement.setAttribute(dock, '');
    showTab(kind);
  }

  const sheet = new CSSStyleSheet();
  sheet.replaceSync(`
    .surface { height: 100%; background: #17141e; color: #ede6ef; font: 13px/1.45 "Segoe UI", sans-serif;
      border-inline-start: 1px solid #533d56; display: grid; grid-template-rows: auto auto minmax(0, 1fr); }
    * { box-sizing: border-box; }
    header, .tabs, .actions { display: flex; align-items: center; gap: 4px; }
    header { justify-content: space-between; background: #241c2a; padding: 6px; border-bottom: 1px solid #403246; }
    .tabs, .actions { flex-wrap: wrap; }
    button, input { font: inherit; color: inherit; background: #292232; border: 1px solid #49394e; border-radius: 6px; }
    button { cursor: pointer; padding: 4px 8px; }
    button:hover { background: #3a2a40; }
    button:focus-visible, input:focus-visible { outline: 2px solid #dfa9cd; outline-offset: 1px; }
    .tabs button { background: transparent; border-color: transparent; }
    .tabs button[aria-selected="true"], button[aria-pressed="true"] { background: #432e48; border-color: #775770; color: #fff0f9; }
    .close { font-size: 19px; line-height: 1; }
    .actions { padding: 8px; border-bottom: 1px solid #3a2e40; }
    input { padding: 4px 8px; min-width: 70px; flex: 1; }
    .view { overflow: auto; overscroll-behavior: contain; }
    .log, .request { padding: 7px 10px; border-bottom: 1px solid #33283a; overflow-wrap: anywhere; }
    small, .empty, .heading { color: #baabbd; }
    .log.warn { background: #342a1f; color: #f4d398; }
    .log.error { background: #381e2a; color: #ffc0c8; }
    pre { margin: 2px 0 0; font: 12px/1.5 Consolas, monospace; white-space: pre-wrap; overflow-wrap: anywhere; }
    .request div, .element { font-family: Consolas, monospace; }
    .empty, .heading, .dimensions { display: block; margin: 10px; }
    .selected { margin: 8px; padding: 8px; background: #292232; border-radius: 6px; }
    .element { display: block; text-align: left; overflow-wrap: anywhere; width: calc(100% - 16px); margin: 4px 8px; }
    .child { width: calc(100% - 26px); margin-inline-start: 18px; }
    .highlight { position: fixed; pointer-events: none; outline: 2px solid #be94f5;
      background: #be94f53d; }
  `);

  window.addEventListener('keydown', event => {
    if (!event.isTrusted) return;
    if (event.key === 'Escape' && picker) {
      event.preventDefault();
      event.stopImmediatePropagation();
      stopPicking();
    }
    if (event.key !== 'F12' || event.ctrlKey || event.altKey || event.shiftKey || event.metaKey) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    if (event.repeat) return;
    if (host) closePanel();
    else if (document.documentElement) openPanel(tab);
  }, { capture: true });

  document.addEventListener('DOMContentLoaded', () => {
    let saved;
    try { saved = sessionStorage.getItem(sessionKey); } catch { /* Storage may be disabled. */ }
    if (!host && Object.hasOwn(tabs, saved)) openPanel(saved);
  }, { once: true });
})();
