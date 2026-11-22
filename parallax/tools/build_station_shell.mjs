import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { cp, readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
const workspace = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const require = createRequire(resolve(workspace, 'slipstream/package.json'));
const esbuild = require('esbuild');
const target = resolve(workspace,'shchytok/parallax/station/windows/Assets/Shell');
const core = resolve(workspace,'core');
const bundle = await esbuild.build({ entryPoints:[resolve(target,'entry.jsx')],bundle:true,minify:true,outfile:resolve(target,'shell.js'),format:'iife',target:'chrome120',metafile:true,external:['/fonts/*'],
 nodePaths:[resolve(workspace,'slipstream/node_modules')],alias:{'@parallax/core/catalog':resolve(core,'resources/ecosystem.json'),'react':resolve(workspace,'slipstream/node_modules/react'),'react-dom':resolve(workspace,'slipstream/node_modules/react-dom'),'@headlessui/react':resolve(workspace,'slipstream/node_modules/@headlessui/react'),'@parallax/helm/AppShell':resolve(workspace,'helm/resources/js/Helm/shell/AppShell.jsx'),'@parallax/core/react':resolve(core,'src/react/WorkspaceLauncher.jsx'),'@parallax/core/styles.css':resolve(core,'src/react/workspace-launcher.css'),'@inertiajs/react':resolve(target,'navigation-adapter.js')},
 define:{'process.env.NODE_ENV':'"production"'},plugins:[{name:'packaged-core-assets',setup(build){build.onLoad({filter:/core\/src\/brand-assets\.js$/},()=>({contents:'export const brandAssets = {};',loader:'js'}));}}] });
const helmRequire = createRequire(resolve(workspace,'helm/package.json'));
const { compile } = await import(fileURLToPath(new URL('file://' + helmRequire.resolve('@tailwindcss/node'))));
const { Scanner } = helmRequire('@tailwindcss/oxide');
const cssFile = resolve(workspace,'helm/resources/css/app.css');
const compiled = await compile(await readFile(cssFile,'utf8'),{base:dirname(cssFile),onDependency:()=>{}});
const scanner = new Scanner({sources:[{base:workspace,pattern:'helm/resources/js/Helm/**/*.jsx',negated:false},{base:workspace,pattern:'shchytok/parallax/station/windows/Assets/Shell/entry.jsx',negated:false}]});
await writeFile(resolve(target,'helm.css'),compiled.build(scanner.scan()));
await mkdir(resolve(target,'ecosystem'),{recursive:true});
await cp(resolve(workspace,'marketing/public/assets/ecosystem-map.js'),resolve(target,'ecosystem/map.js'));
await cp(resolve(workspace,'marketing/public/assets/home-sections.css'),resolve(target,'ecosystem/site.css'));
await writeFile(resolve(target,'ecosystem/index.html'),`<!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'none'"><link rel="stylesheet" href="site.css"><style>html,body{margin:0;background:#11071d;color:#eee;font:13px system-ui}#eco-wrap{min-width:1100px}#eco-tip{box-sizing:border-box}.hidden{display:none}</style></head><body><div id="eco-wrap"><svg id="eco" viewBox="0 0 1500 1245" role="img" aria-label="Parallax ecosystem reference"></svg><div id="eco-tip" class="hidden"></div></div><script src="map.js"></script></body></html>`);
await mkdir(resolve(target,'assets'),{recursive:true}); await cp(resolve(core,'assets'),resolve(target,'assets'),{recursive:true});
const brands = JSON.parse(await readFile(resolve(target,'../Brands/catalog.json'),'utf8'));
for(const brand of brands) await cp(resolve(target,'..','..',brand.mark),resolve(target,'assets',brand.mark.split('/').pop()));
await cp(resolve(target,'../Brands/unravel-provenance.json'),resolve(target,'assets/unravel-provenance.json'));
await cp(resolve(target,'../Brands/parallax-wordmark.png'),resolve(target,'assets/parallax-wordmark.png'));
await cp(resolve(target,'../ubuntu.svg'),resolve(target,'assets/ubuntu.svg'));
await writeFile(resolve(target,'assets/ubuntu-provenance.json'),JSON.stringify({source:'https://assets.ubuntu.com/v1/ce518a18-CoF-2022_solid+O.svg',sha256:createHash('sha256').update(await readFile(resolve(target,'../ubuntu.svg'))).digest('hex'),modified:false},null,2));
for(const filename of ['xterm.js','xterm.css','addon-fit.js','addon-serialize.js']) await cp(resolve(target,'../Terminal',filename),resolve(target,filename));
await mkdir(resolve(target,'fonts'),{recursive:true});
// Preserve the established suite font; no network font dependency at runtime.
const { globSync } = helmRequire('tinyglobby');
const fonts = globSync(['marketing/public/**/Inter*Regular*.woff2','marketing/public/**/*inter*latin*.woff2'],{cwd:workspace});
if(fonts[0]) await cp(resolve(workspace,fonts[0]),resolve(target,'fonts/inter.woff2'));
const sources=['core/src/react/WorkspaceLauncher.jsx','core/src/react/workspace-launcher.css','core/resources/ecosystem.json','helm/resources/js/Helm/shell/AppShell.jsx','helm/resources/css/app.css'];
await writeFile(resolve(target,'provenance.json'),JSON.stringify({sources:await Promise.all(sources.map(async source=>({source,sha256:createHash('sha256').update(await readFile(resolve(workspace,source))).digest('hex')})))},null,2));
// Package notices for every dependency actually included in the bundle.
const noticeDirectory = resolve(target,'../../ThirdParty/Shell');
await mkdir(noticeDirectory,{recursive:true});
const packages = new Map();
for (const input of Object.keys(bundle.metafile.inputs)) {
  const full = resolve(workspace,input); const marker = full.lastIndexOf('/node_modules/');
  if (marker < 0) continue;
  const relative = full.slice(marker + '/node_modules/'.length).split('/');
  const packageName = relative[0].startsWith('@') ? relative.slice(0,2).join('/') : relative[0];
  const packageRoot = full.slice(0,marker) + '/node_modules/' + packageName;
  if (packages.has(packageName)) continue;
  const metadata = JSON.parse(await readFile(resolve(packageRoot,'package.json'),'utf8'));
  packages.set(packageName,{name:packageName,version:metadata.version,license:metadata.license});
  for (const license of ['LICENSE','LICENSE.md','LICENSE.txt','license']) {
    try { await cp(resolve(packageRoot,license),resolve(noticeDirectory,packageName.replaceAll('/','-') + '-LICENSE.txt')); break; } catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
}
await writeFile(resolve(noticeDirectory,'packages.json'),JSON.stringify([...packages.values()],null,2));
console.log('Built the React Station shell with the shared Helm frame, tokens, Core grid and packaged terminal.');
