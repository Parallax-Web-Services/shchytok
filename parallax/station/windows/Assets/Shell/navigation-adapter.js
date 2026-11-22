// Helm's frame listens for Inertia navigation to close its mobile drawer.
// Station supplies that event from its local tab/resource router instead.
export const router = { on(event, callback) {
  if (event !== 'navigate') throw new Error('Unsupported Helm frame event');
  window.addEventListener('station:navigate', callback);
  return () => window.removeEventListener('station:navigate', callback);
} };
