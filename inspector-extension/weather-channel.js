'use strict';

const hostName = 'com.dukhidukhi.weather';
const pages = new Map();
let publisher;

function sendRectangles() {
  const current = Array.from(pages.values()).at(-1);
  const rectangles = current || [];
  if (!current && !publisher) return;
  if (!publisher) {
    const native = chrome.runtime.connectNative(hostName);
    publisher = native;
    native.onDisconnect.addListener(() => {
      void chrome.runtime.lastError;
      if (publisher === native) publisher = null;
    });
  }
  publisher.postMessage(rectangles);
}

function sitePort(port) {
  port.onMessage.addListener(rectangles => {
    pages.delete(port);
    if (Array.isArray(rectangles)) pages.set(port, rectangles);
    sendRectangles();
  });
  port.onDisconnect.addListener(() => {
    if (pages.delete(port)) sendRectangles();
  });
}

function rendererPort(port) {
  const native = chrome.runtime.connectNative(hostName);
  native.onMessage.addListener(rectangles => port.postMessage(rectangles));
  native.onDisconnect.addListener(() => {
    void chrome.runtime.lastError;
    port.disconnect();
  });
  port.onDisconnect.addListener(() => native.disconnect());
  native.postMessage('watch');
}

chrome.runtime.onConnect.addListener(port => {
  if (port.sender?.id !== chrome.runtime.id || port.sender.frameId !== 0) return;
  const url = new URL(port.sender.url);
  if (port.name === 'weather-renderer' && url.href === 'http://tauri.localhost/weather-gutters.html') {
    rendererPort(port);
  } else if (port.name === 'weather-site' && url.protocol === 'https:' && !url.port &&
    ['animesss.tv', 'animesss.com', 'www.animesss.tv', 'www.animesss.com'].includes(url.hostname)) {
    sitePort(port);
  }
});
