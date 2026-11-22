import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { cp, readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
const workspace = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const require = createRequire(resolve(workspace, 'slipstream/package.json'));
const esbuild = require('esbuild');
const core = resolve(workspace, 'core');
const target = resolve(workspace, 'shchytok/parallax/station/windows/Assets/Launcher');
await esbuild.build({
  entryPoints: [resolve(target,'entry.jsx')], bundle: true, minify: true,
  outfile: resolve(target,'launcher.js'), format: 'iife', target: 'chrome120',
  nodePaths: [resolve(workspace,'slipstream/node_modules')],
  alias: { '@parallax/core/react': resolve(core,'src/react/WorkspaceLauncher.jsx'), '@parallax/core/styles.css': resolve(core,'src/react/workspace-launcher.css') },
  define: { 'process.env.NODE_ENV': '"production"' },
  plugins: [{ name: 'core-local-brand-assets', setup(build) {
    build.onLoad({ filter: /core\/src\/brand-assets\.js$/ }, () => ({ contents: 'export const brandAssets = {};', loader: 'js' }));
  }}],
});
await mkdir(resolve(target,'assets'),{recursive:true});
await cp(resolve(core,'assets'),resolve(target,'assets'),{recursive:true});
await cp(resolve(core,'resources/ecosystem.json'),resolve(target,'ecosystem.json'));
const brands = JSON.parse(await readFile(resolve(target,'../Brands/catalog.json'),'utf8'));
for (const brand of brands) await cp(resolve(target,'..','..',brand.mark),resolve(target,'assets',brand.mark.split('/').pop()));
await cp(resolve(target,'../Brands/catalog.json'),resolve(target,'registered-apps.json'));
const sources = ['src/react/WorkspaceLauncher.jsx','src/react/workspace-launcher.css','src/index.js','resources/ecosystem.json'];
const provenance = await Promise.all(sources.map(async path => ({ source: `core/${path}`, sha256: createHash('sha256').update(await readFile(resolve(core,path))).digest('hex') })));
await writeFile(resolve(target,'provenance.json'), JSON.stringify({ package:'@parallax/core', sources: provenance },null,2));
console.log('Bundled the shared Core launcher, CSS, catalog, and approved assets.');
