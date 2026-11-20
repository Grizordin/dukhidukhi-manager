(() => {
  'use strict';

  // The native weather surface is drawn above the page. Its existing drawer
  // cutout can also reserve room for the floating controls.
  const bridge = window.chrome?.webview;
  if (typeof bridge?.postMessage !== 'function') return;
  const send = bridge.postMessage;
  const prefix = 'animesss:weather-layout/';
  const floating = [
    '.suite-floating-ui', '#suite-settings-btn', '#suite-settings-panel', '#suite-mobile-helper-layer',
    '.clicker-ui-root', '.clicker-btn-root', '.clicker-modal', '.clicker-ui-root #settingsPanel', '#dukhi-inspector',
  ].join(',');
  let previousLayout, lastSent, frame, watching;

  function rectOf(element) {
    if (element.closest('[hidden], [aria-hidden="true"]')) return null;
    if (element.checkVisibility?.({ visibilityProperty: true, opacityProperty: true }) === false) return null;
    const box = element.getBoundingClientRect();
    return box.width > 0 && box.height > 0 ? box : null;
  }

  function adjustedMessage(message, layout) {
    const bounds = Array.isArray(layout) ? layout : layout.area;
    if (!Array.isArray(bounds) || bounds.length !== 4 || !bounds.every(Number.isFinite) || !innerWidth || !innerHeight)
      return message;
    const [mainLeft, mainRight, pageWidth] = bounds;
    const existing = Array.isArray(layout.drawer) && layout.drawer.length === 4 && layout.drawer.every(Number.isFinite)
      ? layout.drawer : null;
    let mask = existing;
    for (const element of document.querySelectorAll(floating)) {
      const box = rectOf(element);
      if (!box) continue;
      const left = Math.max(0, box.left / innerWidth);
      const right = Math.min(pageWidth, box.right / innerWidth);
      const top = Math.max(0, box.top / innerHeight);
      const bottom = Math.min(1, box.bottom / innerHeight);
      if (left >= right || top >= bottom || (left >= mainLeft && right <= mainRight)) continue;
      mask = mask ? [Math.min(mask[0], left), Math.min(mask[1], top), Math.max(mask[2], right), Math.max(mask[3], bottom)]
        : [left, top, right, bottom];
    }
    if (!mask || mask === existing) return message;
    return prefix + JSON.stringify({ ...(Array.isArray(layout) ? {} : layout), area: bounds, drawer: mask });
  }

  function update() {
    frame = 0;
    if (!previousLayout || document.hidden) return;
    const next = adjustedMessage(previousLayout.message, previousLayout.layout);
    if (next === lastSent) return;
    Reflect.apply(send, bridge, [next]);
    lastSent = next;
  }

  function schedule() {
    if (!frame && previousLayout) frame = requestAnimationFrame(update);
  }

  function capture(message, ...rest) {
    if (typeof message === 'string' && message.startsWith(prefix)) {
      let layout;
      try { layout = JSON.parse(message.slice(prefix.length)); } catch { /* Pass through unknown formats. */ }
      previousLayout = layout && typeof layout === 'object' ? { message, layout } : null;
      if (previousLayout && !document.hidden) {
        message = adjustedMessage(message, layout);
        if (!watching) watchFloatingControls();
      }
      lastSent = message;
    }
    return Reflect.apply(send, this, [message, ...rest]);
  }

  try {
    bridge.postMessage = capture;
    if (bridge.postMessage !== capture) return;
  } catch { return; }

  function watchFloatingControls() {
    watching = true;
    const resize = new ResizeObserver(schedule);
    const tracked = new Set();
    function track() {
      const current = new Set(document.querySelectorAll(floating));
      for (const element of tracked) {
        if (!current.has(element)) {
          resize.unobserve(element);
          tracked.delete(element);
        }
      }
      for (const element of current) {
        if (tracked.has(element)) continue;
        resize.observe(element);
        tracked.add(element);
      }
      schedule();
    }

    function isFloating(node) {
      return node instanceof Element && (node.matches(floating) || !!node.querySelector(floating));
    }
    new MutationObserver(records => {
      if (records.some(record => record.type === 'attributes' ? tracked.has(record.target) || isFloating(record.target) :
        Array.from(record.addedNodes).some(isFloating) || Array.from(record.removedNodes).some(isFloating))) track();
    }).observe(document, { subtree: true, childList: true, attributes: true, attributeFilter: ['class', 'id', 'hidden', 'style', 'aria-hidden'] });
    track();
    window.addEventListener('resize', schedule, { passive: true });
    window.addEventListener('scroll', schedule, { passive: true, capture: true });
    document.addEventListener('visibilitychange', schedule);
    document.addEventListener('transitionend', schedule, true);
  }
})();
