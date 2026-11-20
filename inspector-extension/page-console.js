(() => {
  'use strict';

  const emit = document.dispatchEvent.bind(document);
  const Event = window.CustomEvent;
  const apply = Reflect.apply;
  const stringify = JSON.stringify;
  const topic = 'dukhi:inspector-console';

  function preview(value) {
    if (value === null) return 'null';
    if (typeof value === 'string') return value.slice(0, 3200);
    if (typeof value === 'function') return '[Функция]';
    if (typeof value !== 'object') return String(value);
    if (Error.isError?.(value)) {
      const message = Object.getOwnPropertyDescriptor(value, 'message')?.value;
      return typeof message === 'string' ? message.slice(0, 3200) : '[Ошибка]';
    }
    return Array.isArray(value) ? 'Array(' + value.length + ')' : 'Object';
  }

  function snapshot(value) {
    const keys = Object.keys(value);
    const entries = keys.slice(0, 16).map(key => {
      const field = Object.getOwnPropertyDescriptor(value, key);
      const display = field && Object.hasOwn(field, 'value') ? preview(field.value).slice(0, 280) : '‹getter›';
      return [key.slice(0, 100), display];
    });
    if (keys.length > 16) entries.push(['…', '+' + (keys.length - 16)]);
    return { label: preview(value), entries };
  }

  function report(level, args, source = '') {
    try {
      const visible = args.slice(0, 12);
      const text = visible.map(preview).join(' ').slice(0, 4800);
      const objects = visible.filter(value => value && typeof value === 'object' && !Error.isError?.(value))
        .slice(0, 2).map(value => {
          try { return snapshot(value); } catch { return { label: 'Object', entries: [] }; }
        }).filter(object => object.entries.length);
      emit(new Event(topic, { detail: stringify([level, text, source, objects]) }));
    } catch {
      // Console output must still reach the browser if a value cannot be previewed.
    }
  }

  for (const level of ['log', 'info', 'debug', 'warn', 'error']) {
    const original = console[level];
    console[level] = function (...args) {
      const result = apply(original, this, args);
      report(level, args);
      return result;
    };
  }

  window.addEventListener('error', event => {
    if (typeof event.message !== 'string' || !event.message) return;
    const source = event.filename ? event.filename + ':' + event.lineno + ':' + event.colno : '';
    report('error', [event.message], source);
  });
  window.addEventListener('unhandledrejection', event => {
    report('error', ['Необработанное отклонение Promise:', event.reason]);
  });
})();
