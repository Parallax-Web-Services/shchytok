const pending = new Map();
const listeners = new Set();
let sequence = 0;
export function call(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = String(++sequence);
    const timeout = setTimeout(() => { pending.delete(id); reject(new Error('Station did not respond. Refresh before retrying a write.')); }, 60000);
    pending.set(id, { resolve, reject, timeout });
    window.chrome.webview.postMessage({ id, method, params });
  });
}
export function subscribe(listener) { listeners.add(listener); return () => listeners.delete(listener); }
window.chrome.webview.addEventListener('message', ({ data }) => {
  if (data.id && pending.has(data.id)) {
    const { resolve, reject, timeout } = pending.get(data.id); clearTimeout(timeout); pending.delete(data.id);
    if (data.ok) resolve(data.data); else { const error = new Error(data.error); error.accessDenied = data.accessDenied; error.authenticationRequired = data.authenticationRequired; reject(error); }
    return;
  }
  for (const listener of listeners) listener(data);
});
window.station = { call };
