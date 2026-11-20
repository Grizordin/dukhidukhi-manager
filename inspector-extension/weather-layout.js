(() => {
  'use strict';

  const floating = [
    '.suite-floating-ui:not(.aw-modal-overlay, :has(> .suite-lab-fatigue-modal-box))',
    '.aw-modal-overlay > .aw-modal-box', '.suite-floating-ui > .suite-lab-fatigue-modal-box',
    '#suite-settings-btn', '#suite-settings-panel', '#suite-mobile-helper-layer',
    '.clicker-ui-root', '.clicker-btn-root', '.clicker-modal', '.clicker-ui-root #settingsPanel',
    '.cpt-toast', '#customNotifyRoot .custom-toast', '#dukhi-inspector',
  ].join(',');
  const tracked = new Set();
  const resize = new ResizeObserver(schedule);
  const port = chrome.runtime.connect({ name: 'weather-site' });
  let frame = 0;
  let lastSent;
  let gesture;
  let needsTracking = true;

  function bounds(element) {
    if (element.closest('[hidden], [aria-hidden="true"]')) return null;
    if (element.checkVisibility({ visibilityProperty: true, opacityProperty: true }) === false) return null;
    const box = element.getBoundingClientRect();
    const left = Math.max(0, box.left / innerWidth);
    const right = Math.min(1, box.right / innerWidth);
    const top = Math.max(0, box.top / innerHeight);
    const bottom = Math.min(1, box.bottom / innerHeight);
    const rect = [left, top, right, bottom].map(value => Math.round(value * 1e6) / 1e6);
    return rect[0] < rect[2] && rect[1] < rect[3] ? rect : null;
  }

  function publish(value) {
    const key = JSON.stringify(value);
    if (key === lastSent) return;
    try {
      port.postMessage(value);
      lastSent = key;
    } catch {
      stop();
    }
  }

  function schedule() {
    if (!gesture && !frame) frame = requestAnimationFrame(measure);
  }

  function measure() {
    frame = 0;
    if (document.hidden) {
      publish(null);
      return;
    }
    if (needsTracking) {
      track();
      needsTracking = false;
    }
    const rectangles = innerWidth && innerHeight ? Array.from(tracked, bounds).filter(Boolean) : [];
    publish(rectangles.slice(0, 128));
  }

  function track() {
    const current = new Set(document.querySelectorAll(floating));
    for (const element of tracked) {
      if (current.has(element)) continue;
      resize.unobserve(element);
      tracked.delete(element);
    }
    for (const element of current) {
      if (tracked.has(element)) continue;
      resize.observe(element);
      tracked.add(element);
    }
  }

  function containsControl(node) {
    return node instanceof Element && (node.matches(floating) || !!node.querySelector(floating));
  }

  const mutations = new MutationObserver(records => {
    if (records.some(record => record.type === 'attributes' ? tracked.has(record.target) || containsControl(record.target) :
      Array.from(record.addedNodes).some(containsControl) || Array.from(record.removedNodes).some(containsControl))) {
      needsTracking = true;
      schedule();
    }
  });

  function observeControls() {
    mutations.observe(document, { subtree: true, childList: true, attributes: true,
      attributeFilter: ['class', 'id', 'hidden', 'style', 'aria-hidden'] });
  }

  function beginGesture(event) {
    if (gesture || !event.isPrimary || event.button !== 0 ||
        !event.composedPath().some(node => tracked.has(node))) return;
    gesture = { id: event.pointerId, mouse: event.pointerType === 'mouse' };
    // Keep the current cutouts while the pointer is held so dragging never
    // rebuilds the mask in the separate weather WebView.
    mutations.disconnect();
    cancelAnimationFrame(frame);
    frame = 0;
  }

  function finishGesture() {
    if (!gesture) return;
    gesture = null;
    needsTracking = true;
    observeControls();
    schedule();
  }

  function hide() {
    finishGesture();
    publish(null);
  }

  const lifecycle = new AbortController();
  const { signal } = lifecycle;
  function stop() {
    lifecycle.abort();
    mutations.disconnect();
    resize.disconnect();
    cancelAnimationFrame(frame);
  }

  port.onDisconnect.addListener(stop);
  observeControls();
  const pointer = { capture: true, passive: true, signal };
  window.addEventListener('pointerdown', beginGesture, pointer);
  for (const type of ['pointerup', 'pointercancel', 'lostpointercapture']) {
    window.addEventListener(type, event => {
      if (gesture?.id === event.pointerId) finishGesture();
    }, pointer);
  }
  // Legacy mouse drags can finish on mouseup or release outside the WebView.
  window.addEventListener('mouseup', event => {
    if (gesture?.mouse && event.button === 0) finishGesture();
  }, pointer);
  window.addEventListener('pointermove', event => {
    if (gesture?.id === event.pointerId && gesture.mouse && !(event.buttons & 1)) finishGesture();
  }, pointer);
  window.addEventListener('blur', finishGesture, { signal });
  window.addEventListener('resize', schedule, { passive: true, signal });
  window.addEventListener('scroll', schedule, { capture: true, passive: true, signal });
  window.addEventListener('pagehide', hide, { signal });
  window.addEventListener('pageshow', schedule, { signal });
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) hide();
    else schedule();
  }, { signal });
  for (const event of ['animationstart', 'animationend', 'animationcancel', 'transitionrun', 'transitionend', 'transitioncancel']) {
    document.addEventListener(event, ({ target }) => {
      if (tracked.has(target)) schedule();
    }, { capture: true, signal });
  }
  schedule();
})();
