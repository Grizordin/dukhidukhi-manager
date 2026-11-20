(() => {
  'use strict';

  const openAttribute = 'data-dukhi-inspector-open';
  const widthProperty = '--dukhi-inspector-width';
  const tabKey = 'dukhi.inspector.tab';
  const widthKey = 'dukhi.inspector.width';
  const tabs = { elements: 'Элементы', console: 'Консоль', network: 'Сеть' };
  const levels = { log: 'Лог', info: 'Инфо', debug: 'Отладка', warn: 'Предупреждение', error: 'Ошибка' };
  const logs = [], requests = [];
  const filters = { elements: '', console: '', network: '' };
  const expanded = new WeakSet();
  const styleProperties = [
    'display', 'position', 'width', 'height', 'box-sizing', 'margin', 'padding', 'border',
    'gap', 'align-items', 'justify-content', 'overflow', 'z-index', 'font-size', 'font-family',
    'line-height', 'color', 'background-color', 'opacity', 'visibility',
  ];
  const phases = [
    ['DNS', '#a5a5e7', 'domainLookupStart', 'domainLookupEnd'],
    ['Соединение', '#deb1c6', 'connectStart', 'connectEnd'],
    ['Ожидание ответа', '#b3c99c', 'requestStart', 'responseStart'],
    ['Загрузка ответа', '#9fb9e0', 'responseStart', 'responseEnd'],
  ];
  const number = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 1 });
  const clock = new Intl.DateTimeFormat('ru-RU', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  let tab = 'elements', isRecording = true, networkGroup = 'all', severity = 'all';
  let styleTab = 'computed', styleSearch = '', requestTab = 'general', networkSort = ['start', 1];
  let host, surface, toolbar, view, footer, buttons, selectButton, selected, treeRows;
  let inspectorTree, crumbs, elementDetail, networkRows, networkDetail, networkBody;
  let picker, highlight, highlighted, selectedRequest, width, renderFrame, treeObserver, treeRefresh;
  let closeElementMenu;

  function node(tag, text, className) {
    const el = document.createElement(tag);
    if (text != null) el.textContent = text;
    if (className) el.className = className;
    return el;
  }

  function button(text, action, className, label) {
    const el = node('button', text, className);
    el.type = 'button';
    if (label) {
      el.title = label;
      el.setAttribute('aria-label', label);
    }
    el.addEventListener('click', action);
    return el;
  }

  function empty(text) {
    return node('p', text, 'empty');
  }

  function readState(key) {
    try { return sessionStorage.getItem(key); } catch { return null; }
  }

  function saveState(key, value) {
    try { sessionStorage.setItem(key, value); } catch { /* Storage may be disabled. */ }
  }

  function filterInput(placeholder, value, update) {
    const input = node('input');
    input.type = 'search';
    input.placeholder = placeholder;
    input.value = value;
    input.setAttribute('aria-label', placeholder);
    input.addEventListener('input', () => update(input.value));
    return input;
  }

  function groups(items, value, change) {
    const container = node('div', null, 'chips');
    for (const [key, label] of Object.entries(items)) {
      const control = button(label, () => {
        for (const child of container.children) child.setAttribute('aria-pressed', child === control);
        change(key);
      });
      control.setAttribute('aria-pressed', key === value);
      container.append(control);
    }
    return container;
  }

  function pairs(values) {
    const list = node('dl', null, 'kv');
    for (const [key, value] of values) list.append(node('dt', key), node('dd', value));
    return list;
  }

  function copyAction(label, getText, { multiline = false, onCopied, fallback } = {}) {
    const copy = button(label, async () => {
      const value = getText();
      try {
        await navigator.clipboard.writeText(value);
      } catch {
        const target = fallback ? fallback() : copy;
        if (!target?.isConnected) return;
        const field = node(multiline ? 'textarea' : 'input');
        field.value = value;
        field.readOnly = true;
        field.setAttribute('aria-label', label + ' через Ctrl+C');
        target.replaceWith(field);
        field.focus();
        field.select();
        return;
      }
      if (onCopied) onCopied();
      else copy.textContent = 'Скопировано';
    }, 'action');
    return copy;
  }

  function elementHTML(element) {
    if (!element.contains(host)) return element.outerHTML;
    const copy = element.cloneNode(true);
    copy.querySelector('#dukhi-inspector')?.remove();
    copy.removeAttribute(openAttribute);
    copy.style.removeProperty(widthProperty);
    if (copy.getAttribute('style') === '') copy.removeAttribute('style');
    return copy.outerHTML;
  }

  function sectionTabs(labels, active, choose) {
    const bar = node('div', null, 'detail-tabs');
    for (const [key, label] of Object.entries(labels)) {
      const item = button(label, () => choose(key));
      item.setAttribute('aria-selected', key === active);
      bar.append(item);
    }
    return bar;
  }

  function updateView(kind) {
    if (!host || tab !== kind || renderFrame) return;
    renderFrame = requestAnimationFrame(() => {
      renderFrame = 0;
      if (!host) return;
      if (tab === 'console') renderConsole();
      if (tab === 'network') renderNetwork();
      if (tab === 'elements') renderTree();
    });
  }

  document.addEventListener('dukhi:inspector-console', event => {
    if (typeof event.detail !== 'string' || event.detail.length > 30000) return;
    try {
      const [level, text, source, objects] = JSON.parse(event.detail);
      if (!Object.hasOwn(levels, level) || typeof text !== 'string') return;
      const last = logs.at(-1);
      if (last && last.level === level && last.text === text && last.source === source &&
          !last.objects?.length && !objects?.length) {
        last.count++;
      } else {
        logs.push({ level, text, source, objects, time: clock.format(Date.now()), count: 1 });
        if (logs.length > 400) logs.shift();
      }
      updateView('console');
    } catch { /* Ignore malformed page events. */ }
  });

  function showConsole() {
    const search = filterInput('Фильтр консоли', filters.console, value => {
      filters.console = value;
      renderConsole();
    });
    const levelsButton = groups({ all: 'Все', info: 'Сообщения', warn: 'Предупр.', error: 'Ошибки' },
      severity, value => { severity = value; renderConsole(); });
    toolbar.append(button('Очистить', () => {
      logs.length = 0;
      renderConsole();
    }, 'action', 'Очистить консоль'), search, levelsButton);
    search.className = 'search';
    view.append(node('div', null, 'logs scroll'));
    renderConsole();
  }

  function renderConsole() {
    const list = view.firstElementChild;
    const query = filters.console.trim().toLowerCase();
    const atEnd = list.scrollHeight - list.clientHeight - list.scrollTop < 24;
    const scroll = list.scrollTop;
    const openObjects = new Set(Array.from(list.querySelectorAll('details[open]')).map(row => row.dataset.key));
    const fragment = document.createDocumentFragment();
    let errors = 0, warnings = 0, visible = 0;
    for (const [index, log] of logs.entries()) {
      if (log.level === 'error') errors += log.count;
      if (log.level === 'warn') warnings += log.count;
      if (severity === 'info' && ['error', 'warn'].includes(log.level)) continue;
      if (['warn', 'error'].includes(severity) && log.level !== severity) continue;
      if (query && ![log.text, log.source, levels[log.level]].join(' ').toLowerCase().includes(query)) continue;
      visible += log.count;
      const row = node('div', null, 'log ' + log.level);
      const content = node('div', null, 'log-body');
      const meta = node('div', levels[log.level], 'log-meta');
      if (log.count > 1) meta.append(node('span', String(log.count), 'badge'));
      if (typeof log.source === 'string' && log.source) meta.append(node('span', log.source, 'source'));
      content.append(meta, node('pre', log.text));
      if (Array.isArray(log.objects)) {
        for (const [offset, object] of log.objects.entries()) {
          if (!Array.isArray(object?.entries) || typeof object.label !== 'string') continue;
          const fields = object.entries.filter(field => Array.isArray(field) &&
            typeof field[0] === 'string' && typeof field[1] === 'string');
          if (!fields.length) continue;
          const details = node('details');
          details.dataset.key = log.time + ':' + index + ':' + offset;
          details.open = openObjects.has(details.dataset.key);
          details.append(node('summary', object.label), pairs(fields));
          content.append(details);
        }
      }
      row.append(node('time', log.time, 'log-time'), content);
      fragment.append(row);
    }
    if (!fragment.childNodes.length) fragment.append(empty(logs.length ? 'Нет сообщений по этому фильтру' : 'Сообщений пока нет'));
    list.replaceChildren(fragment);
    list.scrollTop = atEnd ? list.scrollHeight : scroll;
    footer.textContent = 'Сообщения: ' + visible + '  ·  Ошибки: ' + errors + '  ·  Предупреждения: ' + warnings;
  }

  function children(element) {
    return Array.from(element.children).filter(child => child !== host);
  }

  function ancestry(element) {
    const path = [];
    for (let current = element; current; current = current.parentElement) path.unshift(current);
    return path;
  }

  function attributeValue(element, key) {
    return ['value', 'nonce'].includes(key) ? '…' : (element.getAttribute(key) || '').slice(0, 400);
  }

  function shortName(element) {
    const classes = (element.getAttribute('class') || '').slice(0, 70).trim().split(/\s+/).filter(Boolean);
    return element.localName + (element.id ? '#' + element.id : '') + classes.slice(0, 2).map(name => '.' + name).join('');
  }

  function tagLabel(element) {
    const label = node('span', null, 'tag');
    label.append('<' + element.localName);
    for (const key of ['id', 'class', 'src', 'href', 'type'].filter(name => element.hasAttribute(name))) {
      label.append(' ', node('span', key, 'attr'), '=', node('span', '"' + attributeValue(element, key) + '"', 'value'));
    }
    label.append('>');
    const text = element.firstChild;
    if (text?.nodeType === Node.TEXT_NODE && !['style', 'script'].includes(element.localName)) {
      const snippet = text.data.slice(0, 100).trim();
      if (snippet) label.append(node('span', ' ' + snippet, 'value'));
    }
    return label;
  }

  function selectElement(element, reveal = true) {
    selected = element;
    if (reveal) ancestry(element).slice(0, -1).forEach(parent => expanded.add(parent));
    renderTree();
    renderElementDetail();
    if (reveal) treeRows.get(element)?.scrollIntoView({ block: 'nearest' });
  }

  function openElementMenu(event, element, line) {
    event.preventDefault();
    event.stopPropagation();
    closeElementMenu?.();
    stopPicking();
    const anchor = line.getBoundingClientRect();
    selectElement(element, false);

    const menu = node('div', null, 'element-menu');
    const lifetime = new AbortController();
    const options = { capture: true, signal: lifetime.signal };
    function dismiss(restoreFocus = false) {
      if (!menu.isConnected) return;
      lifetime.abort();
      menu.remove();
      closeElementMenu = null;
      if (restoreFocus) treeRows.get(element)?.focus({ preventScroll: true });
    }
    closeElementMenu = dismiss;
    const copy = copyAction('Копировать элемент', () => elementHTML(element), {
      multiline: true,
      onCopied() {
        if (!menu.isConnected) return;
        dismiss(true);
        footer.textContent = 'HTML элемента скопирован';
      },
      fallback() {
        if (!menu.isConnected) return null;
        dismiss();
        return elementDetail.querySelector('.element-info .action');
      },
    });
    menu.setAttribute('role', 'menu');
    menu.setAttribute('aria-label', 'Действия с элементом');
    copy.setAttribute('role', 'menuitem');
    menu.append(copy);
    surface.append(menu);
    const bounds = host.getBoundingClientRect();
    const pointer = event.button === 2 || event.pointerType;
    const x = pointer ? event.clientX : anchor.left;
    const y = pointer ? event.clientY : anchor.bottom;
    menu.style.left = Math.max(bounds.left + 4, Math.min(x, bounds.right - menu.offsetWidth - 4)) + 'px';
    menu.style.top = Math.max(bounds.top + 4, Math.min(y, bounds.bottom - menu.offsetHeight - 4)) + 'px';
    copy.focus({ preventScroll: true });
    const treeScroll = inspectorTree.scrollTop;
    window.addEventListener('pointerdown', outside => {
      if (!outside.composedPath().includes(menu)) dismiss();
    }, options);
    window.addEventListener('resize', () => dismiss(), options);
    inspectorTree.addEventListener('scroll', () => {
      if (inspectorTree.scrollTop !== treeScroll) dismiss();
    }, options);
    menu.addEventListener('keydown', key => {
      if (key.key !== 'Escape' && key.key !== 'Tab') return;
      key.preventDefault();
      key.stopPropagation();
      dismiss(true);
    });
  }

  function buildTreeRow(element, depth, branch) {
    const row = node('div', null, 'tree-row');
    row.style.setProperty('--depth', depth);
    row.toggleAttribute('data-selected', element === selected);
    const hasChildren = branch && element.childElementCount > 0 &&
      (element.childElementCount > 1 || element.firstElementChild !== host);
    if (hasChildren) {
      const open = expanded.has(element);
      const toggle = button(open ? '▾' : '▸', () => {
        if (open) expanded.delete(element);
        else expanded.add(element);
        renderTree();
        treeRows.get(element)?.focus({ preventScroll: true });
      }, 'twisty', open ? 'Свернуть' : 'Раскрыть');
      toggle.setAttribute('aria-expanded', open);
      row.append(toggle);
    } else row.append(node('span', null, 'spacer'));
    const line = button(null, () => selectElement(element, false), 'tree-line');
    line.append(tagLabel(element));
    line.title = shortName(element);
    row.addEventListener('contextmenu', event => openElementMenu(event, element, line));
    line.addEventListener('pointerenter', () => highlightElement(element));
    line.addEventListener('pointerleave', () => highlightElement(null));
    line.addEventListener('keydown', event => {
      if (event.key === 'ArrowLeft' && expanded.has(element)) {
        event.preventDefault();
        expanded.delete(element);
      } else if (event.key === 'ArrowRight' && hasChildren) {
        event.preventDefault();
        expanded.add(element);
      } else return;
      renderTree();
      treeRows.get(element)?.focus({ preventScroll: true });
    });
    treeRows.set(element, line);
    row.append(line);
    return row;
  }

  function renderTree() {
    if (!selected?.isConnected) {
      selected = document.body || document.documentElement;
      renderElementDetail();
    }
    const scroll = inspectorTree.scrollTop;
    treeRows = new Map();
    const output = document.createDocumentFragment();
    const query = filters.elements.trim();
    if (query) {
      try {
        const matches = Array.from(document.querySelectorAll(query)).filter(el => el !== host);
        matches.slice(0, 300).forEach(element => output.append(buildTreeRow(element, 0, false)));
        footer.textContent = 'Совпадения: ' + matches.length;
      } catch {
        output.append(empty('Введите корректный CSS-селектор'));
      footer.textContent = '';
      }
    } else {
      const stack = [[document.documentElement, 0]];
      while (stack.length && treeRows.size < 900) {
        const [element, depth] = stack.pop();
        output.append(buildTreeRow(element, depth, true));
        if (expanded.has(element)) {
          for (const child of children(element).reverse()) stack.push([child, depth + 1]);
        }
      }
      footer.textContent = stack.length ? 'Показаны первые 900 элементов. Уточните выбор через CSS-селектор.' :
        'Элементов в дереве: ' + treeRows.size;
    }
    if (!output.childNodes.length) output.append(empty('Элементы не найдены'));
    inspectorTree.replaceChildren(output);
    inspectorTree.scrollTop = scroll;
    const path = ancestry(selected);
    crumbs.replaceChildren(...path.map(element => {
      const item = button(shortName(element), () => {
        clearElementSearch();
        selectElement(element);
      });
      if (element === selected) item.setAttribute('aria-current', 'true');
      return item;
    }));
    crumbs.scrollLeft = crumbs.scrollWidth;
  }

  function renderElementDetail() {
    const title = sectionTabs({ computed: 'Вычисленные стили', attributes: 'Атрибуты' }, styleTab, key => {
      styleTab = key;
      renderElementDetail();
    });
    const content = node('div', null, 'details scroll');
    const element = selected;
    const rect = element.getBoundingClientRect();
    const info = node('div', null, 'element-info');
    info.append(node('p', shortName(element) + '  ·  ' + Math.round(rect.width) + ' × ' +
      Math.round(rect.height) + ' px', 'dimensions'));
    info.append(copyAction('Копировать HTML', () => elementHTML(element), { multiline: true }));
    content.append(info);
    if (styleTab === 'attributes') {
      const attrs = selected.getAttributeNames().map(key => [key, attributeValue(selected, key)]);
      content.append(attrs.length ? pairs(attrs) : empty('Нет атрибутов'));
    } else {
      const values = node('div');
      function showStyles() {
        const query = styleSearch.trim().toLowerCase();
        const css = getComputedStyle(selected);
        const keys = query ? Array.from(css).filter(key => key.includes(query)) : styleProperties;
        values.replaceChildren(keys.length ? pairs(keys.map(key => [key, css.getPropertyValue(key)])) :
          empty('Свойства не найдены'));
      }
      content.append(filterInput('Фильтр CSS-свойств', styleSearch, value => {
        styleSearch = value;
        showStyles();
      }), values);
      showStyles();
    }
    elementDetail.replaceChildren(title, content);
  }

  function clearElementSearch() {
    filters.elements = '';
    const search = toolbar.querySelector('input');
    if (search) search.value = '';
  }

  function showElements() {
    if (!selected?.isConnected) selected = document.body || document.documentElement;
    expanded.add(document.documentElement);
    if (document.body) expanded.add(document.body);
    const search = filterInput('Найти по CSS-селектору', filters.elements, value => {
      filters.elements = value;
      updateView('elements');
    });
    search.className = 'search';
    toolbar.append(search, button('Обновить', () => selectElement(selected), 'action'));
    const pane = node('div', null, 'stack');
    inspectorTree = node('div', null, 'tree scroll');
    crumbs = node('div', null, 'crumbs');
    elementDetail = node('div', null, 'detail');
    pane.append(inspectorTree, crumbs, elementDetail);
    view.append(pane);
    renderTree();
    renderElementDetail();
    treeObserver = new MutationObserver(() => {
      if (treeRefresh) return;
      treeRefresh = setTimeout(() => {
        treeRefresh = 0;
        updateView('elements');
      }, 140);
    });
    treeObserver.observe(document.documentElement, { subtree: true, childList: true });
  }

  function formatTime(value) {
    return number.format(value) + ' мс';
  }

  function bytes(value) {
    if (!value) return '—';
    return value < 1024 ? value + ' Б' : number.format(value / 1024) + ' КБ';
  }

  function record(entry) {
    if (!isRecording || !/^https?:\/\//.test(entry.name)) return;
    const url = new URL(entry.name);
    const kind = entry.initiatorType || 'document';
    requests.push({
      name: url.pathname.split('/').filter(Boolean).at(-1) || url.host,
      url: (url.origin + url.pathname + url.search).slice(0, 4096),
      host: url.host,
      kind,
      status: entry.responseStatus || 0,
      protocol: entry.nextHopProtocol,
      size: entry.transferSize,
      bodySize: entry.encodedBodySize,
      start: entry.startTime,
      ms: entry.duration,
      phases: phases.flatMap(([name, color, start, end]) =>
        entry[start] > 0 && entry[end] > entry[start] ? [{ name, color, ms: entry[end] - entry[start] }] : []),
    });
    if (requests.length > 600) requests.shift();
    updateView('network');
  }

  new PerformanceObserver(list => list.getEntries().forEach(record)).observe({ type: 'resource', buffered: true });
  window.addEventListener('load', () => {
    setTimeout(() => performance.getEntriesByType('navigation').forEach(record), 0);
  }, { once: true });

  function showNetwork() {
    const search = filterInput('Фильтр запросов', filters.network, value => {
      filters.network = value;
      renderNetwork();
    });
    search.className = 'search';
    const recording = button(isRecording ? '● Запись' : 'Ⅱ Пауза', () => {
      isRecording = !isRecording;
      recording.textContent = isRecording ? '● Запись' : 'Ⅱ Пауза';
      recording.setAttribute('aria-pressed', isRecording);
      renderNetwork();
    }, 'action', 'Включить или приостановить запись');
    recording.setAttribute('aria-pressed', isRecording);
    toolbar.append(recording, button('Очистить', () => {
      requests.length = 0;
      setRequest(null);
      renderNetwork();
    }, 'action', 'Очистить список запросов'), search,
    groups({ all: 'Все', api: 'Fetch/XHR', doc: 'Документ', media: 'Медиа', other: 'Прочее' }, networkGroup,
      value => { networkGroup = value; renderNetwork(); }));
    networkBody = node('div', null, 'network');
    const tableFrame = node('div', null, 'scroll');
    const table = node('table');
    table.setAttribute('aria-label', 'Запросы страницы');
    const head = node('thead');
    head.append(node('tr'));
    for (const [key, label] of [['name', 'Имя'], ['status', 'Статус'], ['kind', 'Тип'], ['size', 'Размер'], ['ms', 'Время']]) {
      const cell = node('th', null, key === 'size' ? 'wide' : '');
      const sort = button(label, () => {
        networkSort = [key, networkSort[0] === key ? -networkSort[1] : 1];
        renderNetwork();
      });
      sort.dataset.key = key;
      cell.append(sort);
      head.firstElementChild.append(cell);
    }
    networkRows = node('tbody');
    table.append(head, networkRows);
    tableFrame.append(table);
    networkDetail = node('div', null, 'detail');
    networkBody.append(tableFrame, networkDetail);
    view.append(networkBody);
    setRequest(selectedRequest);
    renderNetwork();
  }

  function requestGroup(request) {
    if (['fetch', 'xmlhttprequest'].includes(request.kind)) return 'api';
    if (['document', 'navigation', 'iframe'].includes(request.kind)) return 'doc';
    if (['audio', 'video', 'img', 'image'].includes(request.kind)) return 'media';
    return 'other';
  }

  function renderNetwork() {
    const query = filters.network.trim().toLowerCase();
    const matched = requests.filter(request => (networkGroup === 'all' || requestGroup(request) === networkGroup) &&
      (!query || [request.url, request.kind, request.status].join(' ').toLowerCase().includes(query)));
    const [key, direction] = networkSort;
    const table = networkRows.parentElement;
    for (const control of table.querySelectorAll('th button')) {
      control.parentElement.setAttribute('aria-sort', control.dataset.key === key ?
        (direction === 1 ? 'ascending' : 'descending') : 'none');
    }
    matched.sort((a, b) => direction * (typeof a[key] === 'string' ? a[key].localeCompare(b[key]) : a[key] - b[key]));
    const rows = document.createDocumentFragment();
    for (const request of matched) {
      const row = node('tr');
      row.toggleAttribute('data-active', request === selectedRequest);
      const cell = node('td');
      const link = button(request.name, () => {
        setRequest(request);
        for (const active of networkRows.querySelectorAll('[data-active]')) active.removeAttribute('data-active');
        row.setAttribute('data-active', '');
      }, 'request-link');
      link.title = request.url;
      cell.append(link);
      const status = node('td', request.status || '—',
        request.status >= 400 ? 'status-error' : request.status >= 200 && request.status < 300 ? 'status-ok' : '');
      row.append(cell, status, node('td', request.kind), node('td', bytes(request.size), 'wide'), node('td', formatTime(request.ms)));
      rows.append(row);
    }
    if (!matched.length) {
      const row = node('tr');
      const cell = node('td', requests.length ? 'Запросы по этому фильтру не найдены' : 'Запросов пока нет', 'empty');
      cell.colSpan = 5;
      row.append(cell);
      rows.append(row);
    }
    networkRows.replaceChildren(rows);
    footer.textContent = 'Запросы: ' + matched.length + ' из ' + requests.length + (isRecording ? '' : '  ·  Запись приостановлена');
  }

  function setRequest(request) {
    selectedRequest = request;
    networkDetail.hidden = !request;
    networkBody.classList.toggle('has-detail', !!request);
    if (!request) {
      networkDetail.replaceChildren();
      return;
    }
    const bar = sectionTabs({ general: 'Общие', params: 'Параметры URL', timing: 'Тайминги' }, requestTab, key => {
      requestTab = key;
      setRequest(request);
    });
    bar.append(button('×', () => { setRequest(null); renderNetwork(); }, 'icon', 'Закрыть сведения о запросе'));
    const details = node('div', null, 'details scroll');
    if (requestTab === 'params') {
      const params = Array.from(new URL(request.url).searchParams);
      details.append(params.length ? pairs(params) : empty('В адресе нет параметров'));
    } else if (requestTab === 'timing') {
      const chart = node('div', null, 'timing-track');
      const total = request.phases.reduce((sum, phase) => sum + phase.ms, 0);
      for (const phase of request.phases) {
        const part = node('span');
        part.style.background = phase.color;
        part.style.width = 100 * phase.ms / Math.max(total, request.ms) + '%';
        part.title = phase.name + ' · ' + formatTime(phase.ms);
        chart.append(part);
      }
      const timings = [['Начало от загрузки страницы', formatTime(request.start)], ['Общее время', formatTime(request.ms)]];
      if (total) details.append(chart);
      details.append(pairs(timings.concat(request.phases.map(phase => [phase.name, formatTime(phase.ms)]))));
    } else {
      details.append(pairs([
        ['URL', request.url], ['Сервер', request.host], ['Статус', request.status || 'Недоступен'],
        ['Тип', request.kind], ['Протокол', request.protocol || '—'], ['Передано', bytes(request.size)],
        ['Размер ресурса', bytes(request.bodySize)], ['Длительность', formatTime(request.ms)],
      ]));
      details.append(copyAction('Копировать URL', () => request.url));
    }
    networkDetail.replaceChildren(bar, details);
  }

  function highlightElement(element) {
    highlighted = element;
    highlight.hidden = !element;
    if (!element) return;
    const { x, y, width, height } = element.getBoundingClientRect();
    Object.assign(highlight.style, { left: x + 'px', top: y + 'px', width: width + 'px', height: height + 'px' });
  }

  function stopPicking() {
    picker?.abort();
    picker = null;
    selectButton?.setAttribute('aria-pressed', 'false');
    if (highlight) highlightElement(null);
  }

  function startPicking() {
    if (picker) {
      stopPicking();
      return;
    }
    if (tab !== 'elements') showTab('elements');
    picker = new AbortController();
    selectButton.setAttribute('aria-pressed', 'true');
    const options = { capture: true, signal: picker.signal };
    const target = event => event.target instanceof Element && event.target !== host ? event.target : null;
    function suppress(event) {
      if (!target(event)) return;
      event.preventDefault();
      event.stopImmediatePropagation();
    }
    window.addEventListener('pointerover', event => highlightElement(target(event)), options);
    for (const name of ['pointerdown', 'pointerup', 'mousedown', 'mouseup']) {
      window.addEventListener(name, suppress, options);
    }
    window.addEventListener('click', event => {
      const element = target(event);
      if (!element) return;
      suppress(event);
      stopPicking();
      clearElementSearch();
      selectElement(element);
    }, options);
  }

  function showTab(kind) {
    closeElementMenu?.();
    stopPicking();
    treeObserver?.disconnect();
    clearTimeout(treeRefresh);
    treeRefresh = 0;
    tab = kind;
    saveState(tabKey, tab);
    for (const [name, control] of Object.entries(buttons)) {
      control.setAttribute('aria-selected', name === kind);
      control.tabIndex = name === kind ? 0 : -1;
    }
    view.setAttribute('aria-labelledby', 'tab-' + kind);
    toolbar.replaceChildren();
    view.replaceChildren();
    if (kind === 'elements') showElements();
    if (kind === 'console') showConsole();
    if (kind === 'network') showNetwork();
  }

  function setWidth(value) {
    const max = Math.min(1000, innerWidth * .78);
    const min = Math.min(335, max);
    width = Math.round(Math.max(min, Math.min(max, value)));
    document.documentElement.style.setProperty(widthProperty, width + 'px');
    surface.classList.toggle('compact', width < 440);
    if (highlighted) highlightElement(highlighted);
  }

  function widthGrip() {
    const grip = node('div', null, 'grip');
    grip.tabIndex = 0;
    grip.setAttribute('role', 'separator');
    grip.setAttribute('aria-label', 'Ширина инспектора');
    grip.setAttribute('aria-orientation', 'vertical');
    grip.title = 'Потяните, чтобы изменить ширину панели';
    grip.addEventListener('pointerdown', event => {
      if (event.button !== 0) return;
      event.preventDefault();
      grip.setPointerCapture(event.pointerId);
      grip.classList.add('dragging');
    });
    grip.addEventListener('pointermove', event => {
      if (grip.hasPointerCapture(event.pointerId)) setWidth(innerWidth - event.clientX);
    });
    grip.addEventListener('lostpointercapture', () => {
      grip.classList.remove('dragging');
      saveState(widthKey, width);
    });
    grip.addEventListener('keydown', event => {
      if (!['ArrowLeft', 'ArrowRight'].includes(event.key)) return;
      event.preventDefault();
      setWidth(width + (event.key === 'ArrowLeft' ? 24 : -24));
      saveState(widthKey, width);
    });
    return grip;
  }

  function closePanel() {
    closeElementMenu?.();
    stopPicking();
    treeObserver?.disconnect();
    clearTimeout(treeRefresh);
    treeRefresh = 0;
    host.remove();
    host = null;
    document.documentElement.removeAttribute(openAttribute);
    document.documentElement.style.removeProperty(widthProperty);
    saveState(tabKey, '');
  }

  function openPanel(kind) {
    host = node('aside');
    host.id = 'dukhi-inspector';
    host.setAttribute('aria-label', 'Инспектор страницы AnimeSSS');
    const shadow = host.attachShadow({ mode: 'open' });
    shadow.adoptedStyleSheets = [sheet];
    const header = node('header');
    selectButton = button('Выбрать', startPicking, null, 'Выбрать элемент на странице (Ctrl+Shift+C)');
    selectButton.setAttribute('aria-pressed', 'false');
    const bar = node('div', null, 'tabs');
    bar.setAttribute('role', 'tablist');
    const keys = Object.keys(tabs);
    buttons = Object.fromEntries(keys.map(name => {
      const control = button(tabs[name], () => showTab(name));
      control.id = 'tab-' + name;
      control.setAttribute('role', 'tab');
      control.setAttribute('aria-controls', 'inspector-view');
      bar.append(control);
      return [name, control];
    }));
    bar.addEventListener('keydown', event => {
      if (!['ArrowLeft', 'ArrowRight'].includes(event.key)) return;
      event.preventDefault();
      const next = keys[(keys.indexOf(tab) + (event.key === 'ArrowRight' ? 1 : 2)) % keys.length];
      showTab(next);
      buttons[next].focus();
    });
    header.append(selectButton, bar, button('×', closePanel, 'icon close', 'Закрыть инспектор (F12)'));
    surface = node('div', null, 'surface');
    toolbar = node('div', null, 'tools');
    view = node('div', null, 'view');
    view.id = 'inspector-view';
    view.setAttribute('role', 'tabpanel');
    view.tabIndex = 0;
    footer = node('div', null, 'footer');
    highlight = node('div', null, 'highlight');
    highlight.hidden = true;
    surface.append(header, toolbar, view, footer);
    shadow.append(surface, widthGrip(), highlight);
    document.documentElement.append(host);
    setWidth(Number(readState(widthKey)) || innerWidth * .42);
    document.documentElement.setAttribute(openAttribute, '');
    showTab(kind);
  }

  const sheet = new CSSStyleSheet();
  sheet.replaceSync(dukhiInspectorStyles);

  window.addEventListener('resize', () => {
    if (host) setWidth(Number(readState(widthKey)) || innerWidth * .42);
  });
  window.addEventListener('keydown', event => {
    if (!event.isTrusted) return;
    if (host && event.ctrlKey && event.shiftKey && !event.altKey && !event.metaKey && event.code === 'KeyC') {
      event.preventDefault();
      event.stopImmediatePropagation();
      if (!event.repeat) startPicking();
      return;
    }
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
    const saved = readState(tabKey);
    if (!host && Object.hasOwn(tabs, saved)) openPanel(saved);
  }, { once: true });
})();
