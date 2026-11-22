import React, { useEffect } from 'react';
import { createRoot } from 'react-dom/client';
import { WorkspaceLauncher } from '@parallax/core/react';
import '@parallax/core/styles.css';
import brands from '../Brands/catalog.json';
const additionalProducts = brands.map(brand => ({ key: brand.key, name: brand.name, role: brand.role, description: brand.role + '.', accent: brand.accent, icon: '/assets/' + brand.mark.split('/').pop(), href: brand.url, links: [{ id: 'service', label: 'Open ' + brand.name, href: brand.url }] }));
const send = message => window.chrome.webview.postMessage(message);
function StationLink({ href, onClick, children, ...props }) {
  return <a {...props} href={href} onClick={event => {
    event.preventDefault(); onClick?.(event); send({ type: 'navigate', href });
  }}>{children}</a>;
}
function StationApps() {
  useEffect(() => { send({ type: 'ready' }); }, []);
  return <WorkspaceLauncher currentProduct="parallax" presentation="inline" additionalProducts={additionalProducts} assetBase="/assets" linkComponent={StationLink} shortcut={false} />;
}
window.addEventListener('error', event => send({ type: 'error', message: event.message }));
createRoot(document.getElementById('root')).render(<StationApps />);
