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
    return Array.isArray(value) ? '[Массив]' : '[Объект]';
  }

  function report(level, args) {
    try {
      const text = args.slice(0, 12).map(preview).join(' ').slice(0, 4800);
      emit(new Event(topic, { detail: stringify([level, text]) }));
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
    const source = event.filename ? `\n${event.filename}:${event.lineno}:${event.colno}` : '';
    report('error', [event.message + source]);
  });
  window.addEventListener('unhandledrejection', event => {
    report('error', ['Необработанное отклонение Promise:', event.reason]);
  });
})();
