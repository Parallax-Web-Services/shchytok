// Resolve against registered origins and route boundaries, never page titles.
export function resolveProduct(products, address) {
  let url;
  try { url = new URL(address); } catch { return null; }
  let match = null, best = -1;
  for (const product of products) {
    for (const address of [product.href, ...(product.links || []).map(link => link.href)]) {
      if (!address) continue;
      const candidate = new URL(address);
      if (candidate.origin !== url.origin) continue;
      const path = candidate.pathname.replace(/\/$/, '');
      const score = url.pathname === path || url.pathname.startsWith(path + '/') ? path.length : 0;
      // Origin fallback handles login and other routes, while specific app paths win.
      if (score > best) { best = score; match = product; }
    }
  }
  return match;
}
