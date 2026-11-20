(() => {
  'use strict';

  const port = chrome.runtime.connect({ name: 'weather-renderer' });
  let weatherMask;
  let latest;

  function draw(rectangles) {
    const scene = document.querySelector('.wrapper-as');
    if (!scene) return;
    if (!weatherMask) {
      weatherMask = document.createElement('style');
      document.documentElement.append(weatherMask);
    }
    if (!rectangles.length) {
      weatherMask.textContent = '';
      return;
    }
    const holes = rectangles.map(([left, top, right, bottom]) =>
      '<rect x="' + left + '" y="' + top + '" width="' + (right - left) +
      '" height="' + (bottom - top) + '"/>').join('');
    // One white viewport with black holes also handles overlapping panels.
    const svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1 1" preserveAspectRatio="none">' +
      '<path fill="white" d="M0 0H1V1H0Z"/>' + holes + '</svg>';
    const url = 'data:image/svg+xml,' + encodeURIComponent(svg);
    // The native bell already masks body. The outer mask composes with it.
    weatherMask.textContent = 'html{mask:url("' + url + '") center / 100% 100% no-repeat luminance !important}';
  }

  document.addEventListener('DOMContentLoaded', () => {
    if (latest) draw(latest);
    latest = null;
  }, { once: true });
  port.onMessage.addListener(rectangles => {
    if (!Array.isArray(rectangles)) return;
    if (document.readyState === 'loading') latest = rectangles;
    else draw(rectangles);
  });
  port.onDisconnect.addListener(() => {
    void chrome.runtime.lastError;
    latest = null;
    weatherMask?.remove();
    weatherMask = null;
  });
})();
