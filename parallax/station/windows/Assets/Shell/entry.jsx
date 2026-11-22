import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { Dialog, DialogBackdrop, DialogPanel, DialogTitle } from '@headlessui/react';
import { ChevronRightIcon, FolderIcon, BuildingOffice2Icon, Squares2X2Icon, CommandLineIcon, ComputerDesktopIcon, Cog6ToothIcon, PlusIcon, XMarkIcon, MagnifyingGlassIcon, ArrowPathIcon, ArrowLeftIcon, ArrowRightIcon, ArrowUpRightIcon, CheckIcon, PlayIcon, MapPinIcon, ArrowTopRightOnSquareIcon, CheckCircleIcon, ExclamationTriangleIcon } from '@heroicons/react/20/solid';
import { AppShell } from '@parallax/helm/AppShell';
import { WorkspaceLauncher } from '@parallax/core/react';
import '@parallax/core/styles.css';
import brands from '../Brands/catalog.json';
import ecosystem from '@parallax/core/catalog';
import { resolveProduct } from './app-identity.mjs';
import BrowserControls from './BrowserControls';
import SlipstreamWorkspace from './SlipstreamWorkspace';
import { call, subscribe } from './bridge';
import './station.css';

const additionalProducts = brands.map(brand => ({ key: brand.key, name: brand.name, role: brand.role, description: brand.role + '.', accent: brand.accent, icon: '/assets/' + brand.mark.split('/').pop(), href: brand.url, links: [{ id: 'service', label: 'Open ' + brand.name, href: brand.url }] }));
const products = [...ecosystem.products.map(product => ({ ...product, icon: '/assets/' + product.brand.asset })), ...additionalProducts];
const infrastructureKeys=new Set(['nebula','nucleus']);
const infrastructureProducts=additionalProducts.filter(product=>infrastructureKeys.has(product.key));
const appNames=value=>String(value||'').replace(/\bnucleus\b\.?|\b(?:continuum|slipstream|plexus|positronic|quark|pulsar|nebula|nexus|unravel)\b/gi,name=>name.replace(/\.$/,'').toUpperCase());
const tabTitle=tab=>tab.type==='browser'?appNames(tab.title):tab.title;
// DS9-inspired plan view: habitat ring, central core and three docking pylons.
function StationHomeIcon(props) {
  return <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.45" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" {...props}>
    <circle cx="12" cy="12" r="6.5" />
    <circle cx="12" cy="12" r="2" />
    {[0,120,240].map(angle => <g key={angle} transform={`rotate(${angle} 12 12)`}>
      <path d="M12 10V5.5M10.6 5.7 9.8 2.2 12 3.5 14.2 2.2 13.4 5.7" />
      <path d="m11.1 5.6.9-2.1.9 2.1" />
    </g>)}
  </svg>;
}
function AppIdentity({ tab }) {
  const product = tab.type === 'browser' ? resolveProduct(products, tab.url) : ['project', 'workstream', 'connect', 'slipstream'].includes(tab.type) ? products.find(product => product.key === 'slipstream') : null;
  const local = { home: ['STATION', StationHomeIcon], apps: ['Apps', Squares2X2Icon], terminal: ['Terminal', CommandLineIcon], machine: ['Infrastructure', ComputerDesktopIcon], settings: ['Settings', Cog6ToothIcon] };
  const [name, Component] = local[tab.type] || ['Browser', ArrowTopRightOnSquareIcon];
  return <span className="active-app-identity" aria-label="Active application">{product ? <img src={product.icon} alt="" /> : <Component aria-hidden="true" />}<strong>{product?.name || name}</strong></span>;
}
function TabIcon({tab}) {
  const [unavailable,setUnavailable]=useState(new Set());
  const product=tab.type==='browser'?resolveProduct(products,tab.url):['project','workstream','connect','slipstream'].includes(tab.type)?products.find(product=>product.key==='slipstream'):null;
  const favicon=tab.type==='browser'&&typeof tab.favicon==='string'&&tab.favicon.startsWith('data:image/png;base64,')?tab.favicon:null;
  const image=[favicon,product?.icon].find(source=>source&&!unavailable.has(source));
  if(image)return <img className="tab-favicon" src={image} alt="" onError={()=>setUnavailable(previous=>new Set([...previous,image]))} />;
  const Icon={home:StationHomeIcon,terminal:CommandLineIcon,apps:Squares2X2Icon,machine:ComputerDesktopIcon,settings:Cog6ToothIcon,browser:ArrowTopRightOnSquareIcon}[tab.type]||FolderIcon;
  return <Icon aria-hidden="true" />;
}
const noop = () => {};
const terminalInstances = new Map();
const terminalRestores = new Map();
const terminalSelections = new Map();
const terminalName = session => session?.displayName || (session?.shell === 'wsl' ? 'Ubuntu' : session?.shell === 'powershell' ? 'PowerShell' : 'Command Prompt');
window.station.readTerminal = id => {
  const buffer = terminalInstances.get(id)?.terminal.buffer.active;
  if (!buffer) return '';
  const lines = []; for (let i = 0; i < buffer.length; i++) lines.push(buffer.getLine(i)?.translateToString(true) || ''); return lines.join('\n');
};
window.station.terminalSize = id => { const terminal = terminalInstances.get(id)?.terminal; return terminal ? { columns: terminal.cols, rows: terminal.rows } : null; };
const nameOf = item => item?.name || item?.long_name || `Workstream #${item?.id}`;
const parentOf = task => task.parent_task_id ?? null;
const emptyAuth = { connected: false, organizations: [], workspace: null, origin: 'https://slipstream.codes' };
function Icon({ as: Component, ...props }) { return <Component aria-hidden="true" {...props} />; }
function Button({ children, icon: Component, quiet, className = '', ...props }) { return <button className={`station-button ${quiet ? 'quiet' : ''} ${className}`} {...props}>{Component && <Icon as={Component} />}{children}</button>; }
function Status({ value }) { return <span className={`status status-${value || 'unknown'}`}><i />{String(value || 'unknown').replaceAll('_', ' ')}</span>; }
function Empty({ title, children, action }) { return <div className="empty-state"><div className="empty-symbol"><FolderIcon /></div><h3>{title}</h3>{children && <p>{children}</p>}{action}</div>; }
function Heading({ eyebrow, title, children, actions }) { return <header className="page-heading"><div>{eyebrow && <div className="eyebrow">{eyebrow}</div>}<h1>{title}</h1>{children && <p>{children}</p>}</div>{actions && <div className="heading-actions">{actions}</div>}</header>; }
function App() {
  const [sidebarWidth, setSidebarWidth] = useState(() => Math.max(220,Math.min(560,Number(localStorage.getItem('station.sidebar.width')) || 272)));
  const [auth, setAuth] = useState(emptyAuth), [notice, setNotice] = useState(''), [booting, setBooting] = useState(true);
  const [tabs, setTabs] = useState([{ id: 'home', type: 'home', title: 'Home', workspace: 'global' }]), [active, setActive] = useState('home');
  const [projects, setProjects] = useState({}), [projectData, setProjectData] = useState({}), [expanded, setExpanded] = useState(() => { try { return new Set(JSON.parse(localStorage.getItem('station.expanded') || '[]')); } catch { return new Set(); } });
  const [projectLoads, setProjectLoads] = useState({});
  const projectRequests = useRef(new Map());
  const retryingConnection=useRef(false);const [connectionBusy,setConnectionBusy]=useState(false);
  const [dashboard, setDashboard] = useState(null), [workstreams, setWorkstreams] = useState([]), [visits, setVisits] = useState([]);
  const [query, setQuery] = useState(''), [busy, setBusy] = useState(false), [decision, setDecision] = useState(null);
  const [openingApps,setOpeningApps]=useState(new Set());const openingRequests=useRef(new Set());
  const workspace = auth.workspace, context = workspace?.key || 'local';
  const availableProducts = useMemo(() => additionalProducts.filter(product => !infrastructureKeys.has(product.key) && (product.key !== 'nexus' || auth.navigation?.showNexusApp === true)), [auth.navigation?.showNexusApp]);
  const navigationApps = useMemo(() => [...new Map(products.filter(product => !infrastructureKeys.has(product.key) && (product.key !== 'nexus' || auth.navigation?.showNexusApp === true)).map(product => [product.key,product])).values()], [auth.navigation?.showNexusApp]);
  const searchRef = useRef(null), currentRef = useRef(context); currentRef.current = context;
  const selected = tabs.find(tab => tab.id === active) || tabs[0];
  const run = async action => { try { return await action(); } catch (error) { setNotice(appNames(error.message)); if (error.authenticationRequired) { setAuth(emptyAuth); setActive('home'); call('browser.hide').catch(noop); } return null; } };
  const scoped = (method, params = {}, identity = workspace) => call(method, { ...params, workspace: identity?.key });
  const selectTab = id => { call('browser.hide').catch(noop); setActive(id); window.dispatchEvent(new Event('station:navigate')); };
  const openTab = tab => { setTabs(previous => previous.some(item => item.id === tab.id) ? previous : [...previous, tab]); selectTab(tab.id); };
  const openPage = (type, title) => openTab({ id: `${context}:${type}`, type, title, workspace: context });
  const closeTab = tab => {
    if (tab.type === 'home') return;
    if (tab.type === 'browser') call('browser.close', { id: tab.browserId }).catch(noop);
    if (tab.type === 'terminal') for (const session of terminalInstances.values()) if (session.workspace === tab.workspace) call('terminal.close', { id: session.id }).catch(noop);
    const index=tabs.findIndex(item=>item.id===tab.id);
    setTabs(previous => previous.filter(item => item.id !== tab.id)); if (active === tab.id) selectTab(tabs[index-1]?.id || tabs[index+1]?.id || 'home');
  };
  const openBrowser = async (url, title = 'Browser', options = {}) => run(async () => {
    const existing = tabs.find(tab => tab.type === 'browser' && tab.url === url && tab.workspace === context);
    if (existing) {
      selectTab(existing.id);
      if(existing.error || existing.authenticationRequired) await call('browser.navigate',{id:existing.browserId,url});
      return;
    }
    const result = await call('browser.open', { url, forceInside: options.forceInside === true }); if (result.external) return; openTab({ id: `browser:${result.id}`, browserId: result.id, type: 'browser', workspace: context, title, url, favicon:result.favicon, authenticationRequired: result.authenticationRequired });
  });
  const openApplication = async (url,title) => {
    if(openingRequests.current.has(url))return;
    try { const uri=new URL(url);if((uri.origin===auth.origin||uri.origin==='https://slipstream.codes')&&['/','/dashboard'].includes(uri.pathname)){openPage('slipstream','SLIPSTREAM');return;} } catch {}
    openingRequests.current.add(url);setOpeningApps(new Set(openingRequests.current));
    try{await openBrowser(url,title);}finally{openingRequests.current.delete(url);setOpeningApps(new Set(openingRequests.current));}
  };
  const detachTab = tab => run(async () => {
    const result = await call('window.detach', { tab });
    if (result.moved) { setTabs(previous => previous.filter(item => item.id !== tab.id)); selectTab('home'); }
  });
  window.station.captureTab = async tab => {
    const sessions = [];
    if (tab.type === 'terminal') for (const value of terminalInstances.values()) if (value.workspace === tab.workspace) {
      await new Promise(resolve => value.terminal.write('',resolve));
      sessions.push({ id: value.id, workspace: value.workspace, shell: value.shell, displayName: value.displayName, machine: value.machine, snapshot: value.serializer.serialize() });
    }
    return { tab, sessions, selectedTerminal: terminalSelections.get(tab.workspace), project: projectData[`${tab.workspace}:${tab.projectId}`] };
  };
  window.station.receiveTab = payload => {
    for (const session of payload.sessions) terminalRestores.set(session.id,session);
    if (payload.selectedTerminal) terminalSelections.set(payload.tab.workspace,payload.selectedTerminal);
    if (payload.project) setProjectData(previous => ({ ...previous, [`${payload.tab.workspace}:${payload.tab.projectId}`]: payload.project }));
    openTab(payload.tab);
  };
  const dragTab = (event,tab) => {
    if (event.button !== 0 || tab.type === 'home' || event.target.closest('.tab-close,.tab-detach')) return;
    selectTab(tab.id);
    const start = { x:event.clientX, y:event.clientY }; const element = event.currentTarget;
    element.setPointerCapture(event.pointerId);
    const up = end => {
      element.removeEventListener('pointerup',up); element.removeEventListener('pointercancel',cancel);
      if (element.hasPointerCapture(end.pointerId)) element.releasePointerCapture(end.pointerId);
      if (Math.hypot(end.clientX-start.x,end.clientY-start.y)<12) return;
      const strip = element.parentElement.getBoundingClientRect();
      if (end.clientX<strip.left || end.clientX>strip.right || end.clientY<strip.top-16 || end.clientY>strip.bottom+16) detachTab(tab);
    };
    const cancel = () => { element.removeEventListener('pointerup',up); element.removeEventListener('pointercancel',cancel); };
    element.addEventListener('pointerup',up); element.addEventListener('pointercancel',cancel);
  };
  const visibleTabs = tabs.filter(tab => tab.workspace === 'global' || tab.workspace === context);
  window.station.cycleTabs = direction => {
    const index = visibleTabs.findIndex(tab => tab.id === active);
    const tab = visibleTabs[(index + direction + visibleTabs.length) % visibleTabs.length];
    if (tab) selectTab(tab.id);
  };
  window.station.selectNumberedTab = number => { const tab = visibleTabs[number === 9 ? visibleTabs.length - 1 : number - 1]; if (tab) selectTab(tab.id); };
  window.station.openBrowser = openBrowser;
  window.station.focusSearch = () => { selectTab('home'); requestAnimationFrame(() => searchRef.current?.focus()); };
  window.station.closeCurrent = () => { if (!document.activeElement?.closest('.xterm')) closeTab(selected); };
  useEffect(() => { window.station.bootstrapped = !booting; }, [booting]);
  useEffect(()=>{
    const tab=document.querySelector(`[data-tab-id="${CSS.escape(active)}"]`),strip=document.querySelector('.station-tabstrip');if(!tab||!strip)return;
    let frame;const reveal=()=>{cancelAnimationFrame(frame);frame=requestAnimationFrame(()=>{const item=tab.getBoundingClientRect(),bounds=strip.getBoundingClientRect();if(item.left<bounds.left+6)strip.scrollLeft+=item.left-bounds.left-6;else if(item.right>bounds.right-6)strip.scrollLeft+=item.right-bounds.right+6;});};
    const observer=new ResizeObserver(reveal);observer.observe(strip);observer.observe(tab);window.addEventListener('resize',reveal);reveal();
    return()=>{cancelAnimationFrame(frame);observer.disconnect();window.removeEventListener('resize',reveal);};
  },[active,selected.title]);
  useEffect(() => { localStorage.setItem('station.sidebar.width',String(sidebarWidth)); },[sidebarWidth]);
  useEffect(() => { localStorage.setItem('station.expanded',JSON.stringify([...expanded])); }, [expanded]);
  const retryConnection = async () => {
    if(retryingConnection.current)return;retryingConnection.current=true;setConnectionBusy(true);
    try{await run(async()=>{const result=await call('bootstrap');currentRef.current=result.workspace?.key||'local';setAuth(result);setNotice('');});}
    finally{retryingConnection.current=false;setConnectionBusy(false);}
  };
  const refreshHome = async identity => {
    if (!identity) return;
    const [deck, lanes, history] = await Promise.all([scoped('dashboard', {}, identity), scoped('workstreams', {}, identity), scoped('visits', {}, identity)]);
    if (currentRef.current !== identity.key) return;
    setDashboard(deck); setWorkstreams(lanes.workstreams || []); setVisits(history || []);
  };
  const fetchProject = (identity, id) => {
    const key = `${identity.key}:${id}`;
    if (projectRequests.current.has(key)) return projectRequests.current.get(key);
    setProjectLoads(previous => ({ ...previous, [key]: { loading: true, error: '' } }));
    const request = call('project.tree', { workspace: identity.key, id }).then(data => {
      setProjectData(previous => ({ ...previous, [key]: data }));
      setProjectLoads(previous => ({ ...previous, [key]: { loading: false, error: '' } })); return data;
    }).catch(error => {
      setProjectLoads(previous => ({ ...previous, [key]: { loading: false, error: error.message } })); throw error;
    }).finally(() => projectRequests.current.delete(key));
    projectRequests.current.set(key,request); return request;
  };
  const loadProjects = async identity => {
    const data = await call('projects', { workspace: identity.key });
    const entries = data.projects || []; setProjects(previous => ({ ...previous, [identity.key]: entries }));
    await Promise.all(entries.filter(project => expanded.has(`project:${identity.key}:${project.id}`)).map(project => run(() => fetchProject(identity,project.id))));
    return entries;
  };
  const activate = async identity => {
    if (currentRef.current !== identity.key) {
      const result = await call('workspace.select', { key: identity.key });
      currentRef.current = result.key; setAuth(previous => ({ ...previous, workspace: result }));
      setDashboard(null); setWorkstreams([]); setVisits([]); selectTab('home');
    }
  };
  const loadProject = async (identity, id) => {
    await activate(identity);
    return await fetchProject(identity,id);
  };
  const openProject = async (identity, id, task = null) => run(async () => {
    const data = await loadProject(identity, id);
    openTab({ id: `${identity.key}:project:${id}`, type: 'project', title: data.project.name || `Project #${id}`, workspace: identity.key, projectId: id, task });
    setTabs(previous => previous.map(tab => tab.id === `${identity.key}:project:${id}` ? { ...tab, task } : tab));
  });
  const openWorkstream = id => openTab({ id: `${context}:workstream:${id}`, type: 'workstream', title: nameOf(workstreams.find(work => work.id === id) || { id }), workspace: context, resourceId: id });
  const toggle = key => setExpanded(previous => { const next = new Set(previous); if (next.has(key)) next.delete(key); else next.add(key); return next; });
  useEffect(() => {
    run(async () => { const result = await call('bootstrap'); setAuth(result); currentRef.current = result.workspace?.key || 'local'; }).finally(() => setBooting(false));
    window.chrome.webview.postMessage({ type: 'ready' });
    const listener = event => {
      if (event.ctrlKey && event.key.toLowerCase() === 'k') { event.preventDefault(); selectTab('home'); requestAnimationFrame(() => searchRef.current?.focus()); }
      if (event.ctrlKey && (event.key === 'Tab' || event.key === 'PageUp' || event.key === 'PageDown')) { event.preventDefault(); window.station.cycleTabs(event.shiftKey || event.key === 'PageUp' ? -1 : 1); }
      if (event.altKey && /^[1-9]$/.test(event.key)) { event.preventDefault(); window.station.selectNumberedTab(Number(event.key)); }
      if (event.ctrlKey && !event.shiftKey && event.key.toLowerCase() === 'w' && !event.target.closest('.xterm')) { event.preventDefault(); window.station.closeCurrent(); }
    };
    window.addEventListener('keydown', listener); return () => window.removeEventListener('keydown', listener);
  }, []);
  useEffect(() => {
    if (!workspace || !auth.connected) return;
    run(async () => { await Promise.all([refreshHome(workspace), ...auth.organizations.filter(identity => identity.key === workspace.key || expanded.has(`org:${identity.key}`)).map(loadProjects)]); });
    setExpanded(previous => new Set([...previous, `org:${workspace.key}`]));
  }, [workspace?.key, auth.organizations]);
  useEffect(() => subscribe(event => {
    if (event.type === 'notice') setNotice(appNames(event.message));
    if (event.type === 'navigation.changed') call('navigation.get').then(navigation => setAuth(previous => ({ ...previous, navigation }))).catch(noop);
    if (event.type === 'browser.state') setTabs(previous => previous.map(tab => tab.browserId === event.id ? { ...tab, ...Object.fromEntries(Object.entries(event).filter(([key]) => !['id', 'type', 'workspace'].includes(key))) } : tab));
    if (event.type === 'browser.opened') { const tab = { ...event, id: `browser:${event.id}`, browserId: event.id, type: 'browser' }; openTab(tab); }
    if (event.type === 'browser.closed') { setTabs(previous => previous.filter(tab => tab.browserId !== event.id)); selectTab('home'); }
  }), []);
  const TaskNodes = ({ tasks, parent, project, identity, trail = [] }) => tasks.filter(task => parentOf(task) === parent).map(task => {
    if (trail.includes(task.id)) return null;
    const children = tasks.filter(child => parentOf(child) === task.id), key = `${identity.key}:task:${task.id}`;
    return <div className="tree-branch" key={task.id}><div className={`tree-row task-row ${selected.task === task.id && selected.projectId === project.id ? 'current' : ''}`}>
      <button className="tree-expander" aria-label={`${expanded.has(key) ? 'Collapse' : 'Expand'} ${task.title}`} disabled={!children.length} onClick={() => toggle(key)}>{children.length > 0 && <ChevronRightIcon className={expanded.has(key) ? 'rotated' : ''} />}</button>
      <button className="tree-label" onClick={() => openProject(identity, project.id, task.id)}><span className={`task-dot task-${task.status}`} /><span>{task.title}</span></button>
    </div>{expanded.has(key) && <div className="tree-children"><TaskNodes tasks={tasks} parent={task.id} project={project} identity={identity} trail={[...trail, task.id]} /></div>}</div>;
  });
  const onAppSwitcherOpenChange=useCallback(visible=>{call('browser.overlay',{visible}).catch(noop);},[]);
  const AppLink=props=><a {...props} onClick={event=>{event.preventDefault();props.onClick?.(event);openApplication(props.href,resolveProduct(products,props.href)?.name||'Parallax app');}} />;
  const sidebar = <div className="station-sidebar">
    <div className="station-brand-launcher"><WorkspaceLauncher currentProduct="parallax" currentName="PARALLAX STATION" additionalProducts={availableProducts} assetBase="/assets" shortcut={false} onOpenChange={onAppSwitcherOpenChange} linkComponent={AppLink} trigger={<><span className="station-brand"><img src="/assets/parallax-mark.svg" alt="" /><span><img src="/assets/parallax-wordmark.png" alt="Parallax" /><small>STATION</small></span></span><Squares2X2Icon className="station-switcher-icon" aria-hidden="true" /></>} /></div>
    <nav className="primary-nav" aria-label="Station"><button className={selected.type === 'home' ? 'current' : ''} onClick={() => selectTab('home')}><StationHomeIcon />Home<span className="shortcut">Ctrl K</span></button><button className={selected.type === 'apps' ? 'current' : ''} onClick={() => openPage('apps', 'Apps')}><Squares2X2Icon />Apps</button></nav>
    <div className="sidebar-section"><span>PROJECTS</span><button aria-label="Refresh projects" onClick={() => run(() => Promise.all(auth.organizations.map(loadProjects)))}><ArrowPathIcon /></button></div>
    <div className="project-tree" aria-label="Organizations and projects">
      {auth.organizations.map(identity => <div className="organization" key={identity.key}>
        <div className={`tree-row organization-row ${workspace?.key === identity.key ? 'active-org' : ''}`}><button className="tree-expander" aria-label={`Expand ${identity.name}`} onClick={() => run(async () => { toggle(`org:${identity.key}`); if (!projects[identity.key]) await loadProjects(identity); })}><ChevronRightIcon className={expanded.has(`org:${identity.key}`) ? 'rotated' : ''} /></button><button className="tree-label" onClick={() => run(async () => { await activate(identity); setExpanded(previous => new Set([...previous, `org:${identity.key}`])); await loadProjects(identity); })}><BuildingOffice2Icon /><span>{identity.name}</span></button></div>
        {expanded.has(`org:${identity.key}`) && <div className="tree-children">{projects[identity.key] ? projects[identity.key].map(project => {
          const key = `${identity.key}:${project.id}`, data = projectData[key], load = projectLoads[key];
          return <div key={key}><div className={`tree-row ${selected.projectId === project.id && selected.workspace === identity.key && !selected.task ? 'current' : ''}`}><button className="tree-expander" aria-label={`Expand ${project.name}`} onClick={() => run(async () => { const opening = !expanded.has(`project:${key}`); toggle(`project:${key}`); if (opening && (!data || load?.error)) await fetchProject(identity,project.id); })}><ChevronRightIcon className={expanded.has(`project:${key}`) ? 'rotated' : ''} /></button><button className="tree-label" onClick={() => openProject(identity, project.id)}><FolderIcon /><span>{project.name || 'Untitled project'}</span></button></div>{expanded.has(`project:${key}`) && <div className="tree-children">{data ? data.tasks.length ? <TaskNodes tasks={data.tasks} parent={null} project={project} identity={identity} /> : <p className="tree-empty">No tasks yet</p> : <div className="tree-load-state">{load?.error ? <><p>Couldn’t load tasks</p><small>{load.error}</small><button title={load.error} onClick={() => run(() => fetchProject(identity,project.id))}>Retry</button></> : <p className="tree-empty">Loading tasks…</p>}</div>}</div>}</div>;
        }) : <p className="tree-empty">Loading projects…</p>}{projects[identity.key]?.length === 0 && <p className="tree-empty">No projects yet</p>}</div>}
      </div>)}
      {!auth.organizations.length && <div className="sidebar-empty"><FolderIcon /><p>{booting ? 'Loading organizations…' : 'Your organizations and projects will appear here.'}</p><button onClick={() => openPage('connect', 'Connect SLIPSTREAM')}>Connect SLIPSTREAM <ArrowUpRightIcon /></button></div>}
    </div>
    <section className="sidebar-apps" aria-label="Applications"><div className="sidebar-section"><span>APPS</span></div><nav aria-label="Parallax applications">{navigationApps.map(product => {
      const current = selected.type === 'slipstream' ? product.key === 'slipstream' : selected.type === 'browser' && resolveProduct(products,selected.url)?.key === product.key;
      return <button key={product.key} className={current ? 'current' : ''} aria-label={`Open ${product.name}`} aria-busy={openingApps.has(product.href)} disabled={openingApps.has(product.href)} aria-current={current ? 'page' : undefined} onClick={() => openApplication(product.href,product.name)}><img src={product.icon} alt="" /><span>{openingApps.has(product.href) ? `Opening ${product.name}…` : product.name}</span></button>;
    })}</nav></section>
    <div className="sidebar-tools"><button onClick={() => openPage('terminal', 'Terminal')}><CommandLineIcon />Terminal<span>Local</span></button><button onClick={() => openPage('machine', 'Infrastructure')}><ComputerDesktopIcon />Infrastructure</button><button onClick={() => openPage('settings', 'Settings')}><Cog6ToothIcon />Settings</button></div>
    <div role="separator" aria-label="Resize sidebar" aria-orientation="vertical" aria-valuemin={220} aria-valuemax={Math.min(560,window.innerWidth-480)} aria-valuenow={sidebarWidth} tabIndex={0} className="sidebar-resize-handle" onDoubleClick={() => setSidebarWidth(272)} onKeyDown={event => { if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') { event.preventDefault(); setSidebarWidth(width => Math.max(220,Math.min(560,window.innerWidth-480,width+(event.key === 'ArrowRight' ? 16 : -16)))); } }} onPointerDown={event => { event.preventDefault(); event.currentTarget.setPointerCapture(event.pointerId); event.currentTarget.dataset.dragging='true'; }} onPointerMove={event => { if(event.currentTarget.dataset.dragging==='true') setSidebarWidth(Math.max(220,Math.min(560,window.innerWidth-480,event.clientX))); }} onPointerUp={event => { delete event.currentTarget.dataset.dragging; event.currentTarget.releasePointerCapture(event.pointerId); }} onPointerCancel={event => { delete event.currentTarget.dataset.dragging; }} />
    <div className="sidebar-account"><span className="account-avatar">{auth.user?.initials || 'P'}</span><span><strong>{auth.user?.name || 'Your workspace'}</strong><small>{auth.connectionIssue ? 'Offline · saved connection' : auth.connected ? workspace?.name || 'Choose an organization' : 'Connect to get started'}</small></span><i className={auth.connected ? 'online' : ''} /></div>
  </div>;
  const navigateCommand = event => { if (event.key !== 'Enter') return; const input = query.trim(); if (input.startsWith('https://')) { openBrowser(input,'Browser',{forceInside:true}); } else if (input === '/terminal') openPage('terminal', 'Terminal'); else if ((input === '/machine' || input === '/infrastructure')) openPage('machine', 'Infrastructure'); else if (input === '/connect') openPage('connect', 'Connect SLIPSTREAM'); else if (input.startsWith('/workstream ') && /^\d+$/.test(input.slice(12))) openWorkstream(Number(input.slice(12))); };
  const home = <div className={`station-page home-page${auth.connected ? ' returning-home' : ''}`}><div className="home-intro"><img className="home-orbital" src="/assets/parallax-mark.svg" alt="" /><span className="eyebrow">{workspace?.name || 'PARALLAX STATION'}</span><h1>What needs to move forward?</h1><p>One workspace for your people, projects, and continuous work.</p></div><div className="command-input"><MagnifyingGlassIcon /><input ref={searchRef} value={query} onChange={event => setQuery(event.target.value)} onKeyDown={navigateCommand} placeholder="Find work, open a project, or enter a command…" aria-label="Workspace search and commands" /><kbd>↵</kbd></div><div className="command-hints"><button onClick={() => openPage('terminal', 'Terminal')}><CommandLineIcon />Open terminal</button><button onClick={() => openPage('apps', 'Apps')}><Squares2X2Icon />Explore apps</button><span>CONTINUUM conversation is not connected yet</span></div>
    {auth.connectionIssue && <section className="surface connection-status" role="status"><h2>SLIPSTREAM is unavailable</h2><p>{auth.connectionIssue.message}</p><p>Your saved connection is retained. Local tools remain available.</p><Button icon={ArrowPathIcon} disabled={connectionBusy} onClick={retryConnection}>{connectionBusy?'Retrying…':'Retry connection'}</Button></section>}
    {(!workspace || !auth.connected) ? auth.connectionIssue ? null : <div className="welcome-panel"><div><span className="eyebrow">PICK UP WHERE YOU LEFT OFF</span><h2>Your work belongs here.</h2><p>Connect SLIPSTREAM to bring your organizations, projects, tasks, and ongoing work into Station.</p></div><Button icon={ArrowUpRightIcon} onClick={() => openPage('connect', 'Connect SLIPSTREAM')}>Connect SLIPSTREAM</Button></div> : <>
      <div className="section-heading"><h2>Resume work</h2><Button quiet icon={ArrowPathIcon} onClick={() => run(() => refreshHome(workspace))}>Refresh</Button></div>
      <div className="work-grid">{[...workstreams].filter(work => nameOf(work).toLowerCase().includes(query.toLowerCase())).sort((a,b) => Number(visits.find(v => v.id === b.id)?.pinned || false) - Number(visits.find(v => v.id === a.id)?.pinned || false) || new Date(visits.find(v => v.id === b.id)?.visitedAt || 0) - new Date(visits.find(v => v.id === a.id)?.visitedAt || 0)).slice(0,6).map(work => <button className="work-card" key={work.id} onClick={() => openWorkstream(work.id)}><div><span className="resource-icon"><CommandLineIcon /></span><Status value={work.status} /></div><h3>{nameOf(work)}</h3><p>{work.project?.name || 'Workstream'}<span>#{work.id}</span></p><footer>{work.running_tasks_count || work.running_cycles_count ? <><i className="running-dot" />Execution in progress</> : 'Open workstream'}<ArrowUpRightIcon /></footer></button>)}</div>{!workstreams.length && <Empty title="No workstreams reported">Existing workstreams will appear here when SLIPSTREAM reports them.</Empty>}
      <div className="home-columns"><section><div className="section-heading"><h2>Needs your judgment</h2><span>{dashboard?.attention?.length || 0}</span></div>{dashboard?.attention?.length ? dashboard.attention.map(item => <button className="activity-item" key={item.key} onClick={() => openBrowser(new URL(item.href, auth.origin).href, item.title)}><span className="activity-icon"><ExclamationTriangleIcon /></span><span><strong>{item.title}</strong><small>{item.kind} {item.project && `· ${item.project}`}</small></span><ChevronRightIcon /></button>) : <div className="quiet-empty"><CheckCircleIcon /><p>No decisions reported by SLIPSTREAM.</p></div>}</section><section><div className="section-heading"><h2>Active work</h2></div>{workstreams.filter(work => work.running_tasks_count || work.running_cycles_count).map(work => <button className="activity-item" key={work.id} onClick={() => openWorkstream(work.id)}><span className="activity-icon"><PlayIcon /></span><span><strong>{nameOf(work)}</strong><small>Execution runs on its configured infrastructure</small></span><ChevronRightIcon /></button>)}{!workstreams.some(work => work.running_tasks_count || work.running_cycles_count) && <div className="quiet-empty"><CommandLineIcon /><p>No active execution reported.</p></div>}</section></div>
    </>}
  </div>;
  const commitDecision = async () => { setBusy(true); await run(async () => { await scoped('approval.commit', { challenge: decision.challenge }); setDecision(null); window.dispatchEvent(new CustomEvent('station:refresh-workstream')); }); setBusy(false); };
  const topbar = <><div className="context-path"><AppIdentity tab={selected} /><ChevronRightIcon /><span className="context-workspace">{workspace?.name || 'Local workspace'}</span></div><div className="topbar-end"><span className="local-indicator"><i />{auth.machine || 'This device'}</span><Button quiet icon={PlusIcon} onClick={() => openPage('connect', 'Connect SLIPSTREAM')}>Connect</Button></div></>;
  return <div className="station-host" style={{'--station-sidebar-width': `${sidebarWidth}px`}}><AppShell sidebar={sidebar} topbar={topbar}>
    <div className="station-tabstrip" role="tablist" aria-label="Workspace tabs" onKeyDown={event => {
      if (!event.target.closest('[role=tab]')) return;
      const index = visibleTabs.findIndex(tab => tab.id === active);
      const next = event.key === 'ArrowRight' ? (index+1)%visibleTabs.length : event.key === 'ArrowLeft' ? (index-1+visibleTabs.length)%visibleTabs.length : event.key === 'Home' ? 0 : event.key === 'End' ? visibleTabs.length-1 : -1;
      if (next>=0) { event.preventDefault(); selectTab(visibleTabs[next].id); requestAnimationFrame(() => document.querySelectorAll('.station-tabstrip [role=tab]')[next]?.focus()); }
    }}>{tabs.filter(tab => tab.workspace === 'global' || tab.workspace === context).map(tab => <div key={tab.id} data-tab-id={tab.id} className={`station-tab ${tab.id === active ? 'selected' : ''}`} onPointerDown={event => { if (event.button === 1) event.preventDefault(); else dragTab(event,tab); }} onAuxClick={event => { if (event.button === 1) { event.preventDefault(); closeTab(tab); } }}><button role="tab" tabIndex={tab.id === active ? 0 : -1} aria-selected={tab.id === active} onClick={() => selectTab(tab.id)}><TabIcon tab={tab} /><span>{tabTitle(tab)}</span></button>{tab.type !== 'home' && <button className="tab-detach" title="Move to new window (or drag out)" aria-label={`Move ${tabTitle(tab)} to new window`} onClick={() => detachTab(tab)}><ArrowTopRightOnSquareIcon /></button>}{tab.type !== 'home' && <button aria-label={`Close ${tabTitle(tab)}`} className="tab-close" onClick={() => closeTab(tab)}><XMarkIcon /></button>}</div>)}</div>
    {notice && <div className="station-notice" role="status"><ExclamationTriangleIcon /><span>{notice}</span><button aria-label="Dismiss notice" onClick={() => setNotice('')}><XMarkIcon /></button></div>}
    <div className="station-content">{tabs.map(tab => <section role="tabpanel" aria-label={tab.title} key={tab.id} className={`tab-pane ${tab.id === active && (tab.workspace === context || tab.workspace === 'global') ? 'active' : ''}`}>
      {tab.type === 'home' && home}
      {tab.type === 'apps' && <WorkspaceLauncher currentProduct="parallax" presentation="inline" additionalProducts={availableProducts} assetBase="/assets" shortcut={false} linkComponent={AppLink} />}
      {tab.type === 'slipstream' && <SlipstreamWorkspace active={tab.id === active} workspace={workspace} connected={auth.connected} openConnect={() => openPage('connect','Connect SLIPSTREAM')} openWorkstream={openWorkstream} run={run} onWorkstreams={setWorkstreams} />}
      {tab.type === 'connect' && <Connect origin={auth.origin} run={run} onConnected={data => { setAuth(data); currentRef.current = data.workspace?.key || 'local'; setProjects({}); setProjectData({}); selectTab('home'); }} />}
      {tab.type === 'project' && <Project data={projectData[`${tab.workspace}:${tab.projectId}`]} selectedTask={tab.task} active={tab.id === active && tab.workspace === context} identity={workspace} run={run} refresh={() => loadProject(workspace, tab.projectId)} onSelectTask={task => setTabs(previous => previous.map(item => item.id === tab.id ? { ...item, task } : item))} openWorkstream={openWorkstream} />}
      {tab.type === 'workstream' && <Workstream id={tab.resourceId} active={tab.id === active && tab.workspace === context} workspace={workspace} run={run} onVisits={setVisits} openBrowser={openBrowser} openTerminal={() => openPage('terminal', 'Terminal')} onDecision={setDecision} />}
      {tab.type === 'terminal' && <Terminals active={tab.id === active && tab.workspace === context} context={tab.workspace} run={run} />}
      {tab.type === 'machine' && <Infrastructure active={tab.id === active} workspace={workspace} run={run} openBrowser={openBrowser} openTerminal={()=>openPage('terminal','Terminal')} />}
      {tab.type === 'settings' && <div className="station-page"><Heading eyebrow="STATION" title="Settings">Your account, device, and workspace connection.</Heading><section className="surface settings-section"><h2>{auth.user?.name || 'Not connected'}</h2><p>{workspace?.name || 'Connect an authorized organization to resume its work.'}</p><label className="optional-app-setting"><input type="checkbox" aria-label="Show NEXUS for SaaS administration" checked={auth.navigation?.showNexusApp === true} disabled={!auth.connected} onChange={event => { const showNexusApp = event.target.checked; run(async () => { const navigation = await call('navigation.save', { companyGroups: [], hiddenTeams: [], ...auth.navigation, showNexusApp }); setAuth(previous => ({ ...previous, navigation })); }); }} /><span><strong>Show NEXUS</strong><small>For managing identity and customers in your own SaaS. Hidden by default.</small></span></label><Button onClick={() => openPage('connect', 'Connect SLIPSTREAM')}>Connect SLIPSTREAM</Button>{auth.connected && <Button quiet onClick={() => run(async () => { const result = await call('auth.logout'); setAuth(result); setProjects({}); setProjectData({}); setDashboard(null); setWorkstreams([]); selectTab('home'); })}>Disconnect this device</Button>}</section><BrowserControls run={run} openBrowser={openBrowser} /></div>}
      {tab.type === 'browser' && <BrowserSurface tab={tab} active={tab.id === active && tab.workspace === context} run={run} />}
    </section>)}</div>
    <footer className="station-statusbar"><span><i className={auth.connected ? 'online' : ''} />{auth.connectionIssue ? 'Offline' : auth.connected ? 'Connected workspace' : 'Local workspace'}</span><span>Cloud work continues independently · Local terminals run on this device</span></footer>
    <Dialog open={!!decision} onClose={() => { if (!busy) setDecision(null); }} className="station-dialog"><DialogBackdrop className="dialog-backdrop" /><div className="dialog-viewport"><DialogPanel className="dialog-panel"><span className="eyebrow">HUMAN DECISION</span><DialogTitle>{decision?.action === 'approve' ? 'Approve' : 'Reject'} task</DialogTitle><h3>{decision?.task?.title}</h3><p>{decision?.task?.description}</p><div className="decision-facts"><span>Action</span><strong>{decision?.task?.action || 'Configured executor'}</strong><span>Capabilities</span><strong>{decision?.task?.required_capabilities?.join(', ') || 'As configured by SLIPSTREAM'}</strong></div><>{decision?.task?.action_params && <pre className="decision-parameters">{JSON.stringify(decision.task.action_params,null,2)}</pre>}</><p className="fine-print">Approval can start a cloud action immediately or queue work. Station will not request an additional elevated grant.</p><div className="dialog-actions"><Button quiet disabled={busy} onClick={() => setDecision(null)}>Cancel</Button><Button disabled={busy} onClick={commitDecision}>{busy ? 'Submitting…' : `Confirm ${decision?.action}`}</Button></div></DialogPanel></div></Dialog>
  </AppShell></div>;
}

function Connect({ origin, run, onConnected }) {
  const [address, setAddress] = useState(origin), [code, setCode] = useState(''), [busy, setBusy] = useState(false);
  return <div className="station-page"><Heading eyebrow="YOUR WORKSPACE" title="Connect SLIPSTREAM">Use your existing NEXUS sign-in in your default browser.</Heading><div className="connect-layout"><section className="surface connect-card"><div className="step-number">1</div><h2>Sign in with your browser</h2><p>Station opens device sign-in in your default browser. Your password and browser session stay there.</p><label className="field">Service address<input value={address} onChange={event => setAddress(event.target.value)} type="url" /></label><Button icon={ArrowUpRightIcon} onClick={() => run(() => call('auth.open', { origin: address }))}>Open sign-in in browser</Button></section><section className="surface connect-card"><div className="step-number">2</div><h2>Connect this device</h2><p>Paste the one-time code within 60 seconds. Station stores the resulting device credential in Windows Credential Manager.</p><form onSubmit={async event => { event.preventDefault(); setBusy(true); await run(async () => { const result = await call('auth.exchange', { origin: address, code }); setCode(''); onConnected(result); }); setBusy(false); }}><label className="field">One-time code<input autoComplete="off" value={code} onChange={event => setCode(event.target.value)} placeholder="Paste your device code" /></label><Button disabled={busy || !code.trim()}>{busy ? 'Connecting…' : 'Connect this device'}</Button></form></section></div></div>;
}
function Project({ data, selectedTask, active, identity, run, refresh, onSelectTask, openWorkstream }) {
  const [form, setForm] = useState(null), [title, setTitle] = useState(''), [description, setDescription] = useState(''), [busy, setBusy] = useState(false);
  if (!data) return <Empty title="Select a project">Expand your organization in the sidebar.</Empty>;
  const canCreate = data.capabilities?.task_creation === true;
  const tasks = data.tasks || [], task = tasks.find(item => item.id === selectedTask), children = tasks.filter(item => parentOf(item) === (task?.id || null));
  const create = parent => { setTitle(''); setDescription(''); setForm({ parent }); };
  return <div className="station-page"><Heading eyebrow={identity?.name || 'PROJECT'} title={task?.title || data.project.name} actions={<><Button quiet icon={ArrowPathIcon} disabled={!active} onClick={() => run(refresh)}>Refresh</Button><Button icon={PlusIcon} disabled={!active || !canCreate} onClick={() => create(task?.id || null)}>Add {task ? 'subtask' : 'task'}</Button></>}>{task ? <><button className="inline-link" onClick={() => onSelectTask(null)}>{data.project.name}</button> / Task #{task.id}</> : data.project.description || 'Plan, inspect, and continue project work.'}</Heading>
    {!canCreate && <p className="fine-print legacy-task-note">Tasks are available through this project's workstreams. Creating project tasks and subtasks requires the updated SLIPSTREAM service.</p>}{task && <section className="surface task-detail"><Status value={task.status} /><p>{task.description || 'No additional details.'}</p>{parentOf(task) && <button className="inline-link" onClick={() => onSelectTask(parentOf(task))}>View parent task</button>}</section>}
    <div className="section-heading"><h2>{task ? 'Subtasks' : 'Tasks'}</h2><span>{children.length}</span></div><div className="task-list">{children.map(item => <button className="task-list-row" key={item.id} onClick={() => onSelectTask(item.id)}><span className={`task-dot task-${item.status}`} /><span><strong>{item.title}</strong><small>{tasks.filter(child => parentOf(child) === item.id).length} subtasks</small></span><Status value={item.status} /><ChevronRightIcon /></button>)}</div>{!children.length && <Empty title={task ? 'No subtasks yet' : 'No tasks yet'} action={<Button quiet icon={PlusIcon} disabled={!canCreate} onClick={() => create(task?.id || null)}>Add {task ? 'subtask' : 'task'}</Button>}>Break work into clear, reviewable steps.</Empty>}
    {form && <section className="surface task-form"><div className="section-heading"><h2>{form.parent ? 'New subtask' : 'New task'}</h2><button aria-label="Close task form" onClick={() => setForm(null)}><XMarkIcon /></button></div><form onSubmit={async event => { event.preventDefault(); setBusy(true); await run(async () => { await call('project.task', { workspace: identity.key, id: data.project.id, title, description, parent: form.parent }); setForm(null); await refresh(); }); setBusy(false); }}><label className="field">Title<input maxLength={255} value={title} onChange={event => setTitle(event.target.value)} required /></label><label className="field">Details<textarea maxLength={20000} value={description} onChange={event => setDescription(event.target.value)} rows={4} /></label><p className="fine-print">Created as proposed work. Execution requires separate human approval.</p><Button disabled={busy || !active}>{busy ? 'Creating…' : 'Create ' + (form.parent ? 'subtask' : 'task')}</Button></form></section>}
    {!task && <><div className="section-heading"><h2>Workstreams</h2></div><div className="work-grid">{(data.workstreams || []).map(work => <button key={work.id} className="work-card" onClick={() => openWorkstream(work.id)}><Status value={work.status} /><h3>{nameOf(work)}</h3><footer>Open execution context<ArrowUpRightIcon /></footer></button>)}</div>{!data.workstreams?.length && <p className="muted">No workstreams reported for this project.</p>}</>}
  </div>;
}
function Workstream({ id, active, workspace, run, onVisits, openBrowser, openTerminal, onDecision }) {
  const [data, setData] = useState(null), [visit, setVisit] = useState(null), [prompt, setPrompt] = useState(''), [busy, setBusy] = useState(false);
  const [draftState,setDraftState]=useState('Loading draft…');
  const draftLoaded=useRef(false),draftWrites=useRef(Promise.resolve()),draftRevision=useRef(0);
  useEffect(()=>{
    if(!active || draftLoaded.current)return;
    let cancelled=false;
    call('workstream.draft.read',{id,workspace:workspace?.key}).then(result=>{
      if(cancelled)return;setPrompt(result.text);draftLoaded.current=true;setDraftState(result.text?'Draft saved on this device':'');
    },()=>{if(!cancelled)setDraftState('Draft could not be restored. Reopen this workstream to retry.');});
    return()=>{cancelled=true;};
  },[active,id,workspace?.key]);
  const saveDraft=text=>{
    setPrompt(text);setDraftState('Saving draft…');const revision=++draftRevision.current;
    const pending=call('workstream.draft.save',{id,workspace:workspace?.key,text});
    draftWrites.current=pending;
    pending.then(()=>{if(revision===draftRevision.current)setDraftState(text?'Draft saved on this device':'');},()=>{if(revision===draftRevision.current)setDraftState('Draft was not saved. Keep this tab open and try editing again.');});
    return pending;
  };
  const refresh = async () => { if (!active || !workspace) return; const result = await call('workstream', { id, workspace: workspace.key }); setData(result.payload); setVisit(result.visits.find(v => v.id === id)); onVisits(result.visits); };
  useEffect(() => { if (active) run(refresh); }, [active, id]);
  useEffect(() => { const update = () => { if (active) run(refresh); }; window.addEventListener('station:refresh-workstream', update); return () => window.removeEventListener('station:refresh-workstream', update); }, [active]);
  if (!data) return <Empty title="Loading workstream">Fetching its authoritative state from SLIPSTREAM.</Empty>;
  const work = data.workstream, scope = { id, workspace: workspace?.key };
  return <div className="station-page"><Heading eyebrow={`${workspace?.name || 'WORKSPACE'} / WORKSTREAM #${id}`} title={nameOf(work)} actions={<><Button quiet icon={ArrowPathIcon} onClick={() => run(refresh)}>Refresh</Button><Button quiet icon={MapPinIcon} onClick={() => run(async () => { await call('workstream.pin', { ...scope, pinned: !visit?.pinned }); await refresh(); })}>{visit?.pinned ? 'Unpin' : 'Pin'}</Button></>}><Status value={work.status} /></Heading>
    <div className="resource-bar"><span>{work.project?.name || 'No linked project'}</span><Button quiet icon={CommandLineIcon} onClick={openTerminal}>Local terminal</Button><Button quiet icon={ArrowUpRightIcon} onClick={() => openBrowser(`${workspace.origin}/workstream/${id}`, nameOf(work))}>Open on web</Button></div>
    <div className="workstream-columns"><div><section className="surface conversation"><div className="section-heading"><h2>Agent conversation</h2><span>SLIPSTREAM</span></div><div className="messages">{(work.conversations || []).flatMap(conversation => conversation.prompts || []).map(message => <React.Fragment key={message.id}><div className="message human"><span>You</span><p>{message.body}</p></div>{(message.responses || []).map(reply => <div className="message agent" key={reply.id}><span>Agent</span><p>{reply.body}</p></div>)}</React.Fragment>)}{!work.conversations?.length && <p className="muted">No conversation reported yet.</p>}</div><form className="direction-form" onSubmit={async event => { event.preventDefault(); setBusy(true); await run(async () => { await draftWrites.current; await call('workstream.direction', { ...scope, prompt }); await saveDraft(''); await refresh(); }); setBusy(false); }}><textarea aria-label="Direction for SLIPSTREAM agent" placeholder="Give this workstream a direction…" maxLength={20000} value={prompt} disabled={!draftLoaded.current || busy} onChange={event => {saveDraft(event.target.value).catch(noop);}} /><p className="draft-status" role="status">{draftState}</p><div><span>Execution continues on its configured infrastructure.</span><Button disabled={busy || !prompt.trim() || !active}>{busy ? 'Sending…' : 'Send direction'}</Button></div></form></section>
    <section className="surface events-panel"><div className="section-heading"><h2>Activity since review</h2><Button quiet onClick={() => run(async () => { await call('workstream.review', scope); await refresh(); })}>Mark reviewed</Button></div>{(data.workerEvents || []).filter(event => event.id > (visit?.seenEvent || 0)).map(event => <div className="event-row" key={event.id}><i /><span><strong>{event.message || event.type}</strong><small>{event.created_at}</small></span></div>)}{!(data.workerEvents || []).some(event => event.id > (visit?.seenEvent || 0)) && <p className="muted">No new events in SLIPSTREAM's recent feed.</p>}</section></div>
    <div><section className="surface execution-panel"><h2>Execution</h2>{(work.cycles || []).map(cycle => <div className="execution-item" key={cycle.id}><span>{cycle.type?.replaceAll('_', ' ')}</span><Status value={cycle.status} /><small>{cycle.execution_target || 'Configured target'}</small></div>)}{!work.cycles?.length && <p className="muted">No execution reported.</p>}</section><section className="surface decisions-panel"><h2>Tasks and decisions</h2>{(work.tasks || []).map(task => <div className="decision-task" key={task.id}><h3>{task.title}</h3><Status value={task.status} /><p>{task.description}</p>{task.status === 'proposed' && <div className="decision-buttons">{['approve', 'reject'].map(action => <Button quiet key={action} onClick={() => run(async () => onDecision(await call('approval.prepare', { ...scope, task: task.id, action })))}>{action === 'approve' ? 'Review approval' : 'Reject'}</Button>)}</div>}</div>)}</section>{work.project?.repos?.length > 0 && <section className="surface"><h2>Repositories</h2>{work.project.repos.map(repo => <Button quiet key={repo.id} onClick={() => openBrowser(repo.url, repo.name || 'Repository')}>{repo.name || repo.url}<ArrowUpRightIcon /></Button>)}</section>}</div></div>
  </div>;
}
function Terminals({ active, context, run }) {
  const restored = () => [...terminalRestores.values()].filter(session => session.workspace === context);
  const restoredSelection = () => restored().find(session => session.id === terminalSelections.get(context)) || restored()[0];
  const [sessions, setSessions] = useState(restored), [selected, setSelected] = useState(() => restoredSelection()?.id || null), [shell, setShell] = useState(() => restoredSelection()?.shell || 'wsl');
  const creating = useRef(false);
  const windowsProfile = useRef(shell === 'wsl' ? 'cmd' : shell);
  terminalSelections.set(context,selected);
  const add = async (profile = shell) => {
    if (!active || creating.current) return; creating.current = true;
    await run(async () => {
      const session = await call('terminal.create', { shell: profile });
      setSessions(previous => [...previous, session]); setSelected(session.id); setShell(session.shell);
    });
    creating.current = false;
  };
  const chooseShell = async profile => {
    if (!active) return;
    if (profile !== 'wsl') windowsProfile.current = profile;
    const existing = [...sessions].reverse().find(session => session.shell === profile);
    if (existing) { setSelected(existing.id); setShell(profile); }
    else await add(profile);
  };
  useEffect(() => { if (active && !sessions.length) add(); }, [active]);
  const close = async session => {
    await run(async () => {
      await call('terminal.close', { id: session.id });
      const remaining = sessions.filter(item => item.id !== session.id); setSessions(remaining);
      if (selected === session.id) { setSelected(remaining[0]?.id || null); if (remaining[0]) setShell(remaining[0].shell); }
    });
  };
  useEffect(() => {
    if (!active) return;
    const shortcut = event => {
      if (!event.ctrlKey || !event.shiftKey) return;
      if (event.key.toLowerCase() === 't') { event.preventDefault(); add(); }
      if (event.key.toLowerCase() === 'w') { event.preventDefault(); const session = sessions.find(item => item.id === selected); if (session) close(session); }
    };
    window.addEventListener('keydown', shortcut); return () => window.removeEventListener('keydown', shortcut);
  }, [active, shell, sessions, selected]);
  return <div className={`terminal-page fluent-terminal ${shell === 'wsl' ? 'ubuntu-profile' : 'windows-profile'}`} data-profile={shell}>
    <div className="terminal-toolbar fluent-toolbar">
      <div className="terminal-session-tabs">{sessions.map((session, index) => <div className={selected === session.id ? 'selected' : ''} key={session.id} onPointerDown={event => { if (event.button === 1) event.preventDefault(); }} onAuxClick={event => { if (event.button === 1) { event.preventDefault(); close(session); } }}>
        <button onClick={() => { setSelected(session.id); setShell(session.shell); if (session.shell !== 'wsl') windowsProfile.current = session.shell; }}>{session.shell === 'wsl' ? <img src="/assets/ubuntu.svg" alt="" /> : <CommandLineIcon />}{terminalName(session)} {index + 1}</button>
        <button aria-label={`Close terminal ${index + 1}`} onClick={() => close(session)}><XMarkIcon /></button>
      </div>)}</div>
      <div className="terminal-profile-controls">
        <Button quiet icon={PlusIcon} disabled={!active} onClick={() => add()} title="New terminal (Ctrl+Shift+T)">New terminal</Button>
        <select aria-label="Terminal shell profile" value={shell} onChange={event => chooseShell(event.target.value)}><option value="wsl">Ubuntu (WSL)</option><option value="powershell">PowerShell</option><option value="cmd">Command Prompt</option></select>
        <div className="wsl-profile-toggle"><span>WSL</span><button type="button" role="switch" aria-label="Use WSL" aria-checked={shell === 'wsl'} title="Switch between WSL and Windows sessions without restarting them" onClick={() => chooseShell(shell === 'wsl' ? windowsProfile.current : 'wsl')}><span /></button></div>
      </div>
    </div>
    <div className="terminal-commandbar"><div className="terminal-scope"><span className="local-badge">LOCAL</span><span>{sessions[0]?.machine || 'This device'}</span><span className="terminal-profile-caption">{shell === 'wsl' ? 'Ubuntu · WSL' : `Windows · ${terminalName(sessions.find(session => session.id === selected))}`}</span></div>{selected && <div className="terminal-actions">
      <button onClick={() => terminalInstances.get(selected)?.terminal.clear()}>Clear</button>
      <button onClick={() => run(() => call('clipboard.copy', { text: terminalInstances.get(selected)?.terminal.getSelection() || '' }))}>Copy</button>
      <button onClick={() => run(async () => { const result = await call('clipboard.paste'); terminalInstances.get(selected)?.terminal.paste(result.text); })}>Paste</button>
      <button onClick={() => run(async () => { await call('terminal.restart', { id: selected }); terminalInstances.get(selected)?.terminal.reset(); })}>Restart</button>
    </div>}</div>
    <div className="terminal-sessions">{sessions.map(session => <TerminalView key={session.id} session={session} active={active && selected === session.id} />)}{!sessions.length && <Empty title="Open a local terminal" action={<Button icon={PlusIcon} onClick={() => add()}>New terminal</Button>}>WSL is the default execution target on this device.</Empty>}</div>
  </div>;
}
function TerminalView({ session, active }) {
  const host = useRef(null), instance = useRef(null);
  useEffect(() => {
    const windowsTheme = { background: '#0c0c0c', foreground: '#cccccc', cursor: '#ffffff', selectionBackground: '#ffffff40', black: '#0c0c0c', red: '#c50f1f', green: '#13a10e', yellow: '#c19c00', blue: '#0037da', magenta: '#881798', cyan: '#3a96dd', white: '#cccccc', brightBlack: '#767676', brightRed: '#e74856', brightGreen: '#16c60c', brightYellow: '#f9f1a5', brightBlue: '#3b78ff', brightMagenta: '#b4009e', brightCyan: '#61d6d6', brightWhite: '#f2f2f2' };
    const ubuntuTheme = { background: '#300a24', foreground: '#eeeeec', cursor: '#ffffff', selectionBackground: '#ffffff40', black: '#2e3436', red: '#cc0000', green: '#4e9a06', yellow: '#c4a000', blue: '#3465a4', magenta: '#75507b', cyan: '#06989a', white: '#d3d7cf', brightBlack: '#555753', brightRed: '#ef2929', brightGreen: '#8ae234', brightYellow: '#fce94f', brightBlue: '#729fcf', brightMagenta: '#ad7fa8', brightCyan: '#34e2e2', brightWhite: '#eeeeec' };
    const terminal = new window.Terminal({ fontFamily: 'Cascadia Mono, Consolas, monospace', fontSize: 14, lineHeight: 1.15, cursorBlink: true, scrollback: 5000, theme: session.shell === 'wsl' ? ubuntuTheme : windowsTheme });
    const fit = new window.FitAddon.FitAddon(); terminal.loadAddon(fit); terminal.open(host.current);
    const serializer = new window.SerializeAddon.SerializeAddon(); terminal.loadAddon(serializer);
    const value = { ...session, terminal, fit, serializer, dispose: () => terminal.dispose() }; instance.current = value; terminalInstances.set(session.id, value);
    const off = subscribe(event => { if (event.type !== 'terminal.output' || event.id !== session.id) return; terminal.write(event.data, () => call('terminal.ack', { id: session.id }).catch(noop)); });
    terminal.onData(data => call('terminal.input', { id: session.id, data }).catch(noop));
    terminal.onResize(({ cols, rows }) => call('terminal.resize', { id: session.id, columns: cols, rows }).catch(noop));
    terminal.attachCustomKeyEventHandler(event => {
      if (event.ctrlKey && event.shiftKey && ['c', 'v', 't', 'w'].includes(event.key.toLowerCase())) {
        if (event.type === 'keydown' && event.key.toLowerCase() === 'c') call('clipboard.copy', { text: terminal.getSelection() }).catch(noop);
        if (event.type === 'keydown' && event.key.toLowerCase() === 'v') call('clipboard.paste').then(result => terminal.paste(result.text)).catch(noop);
        return false;
      }
      return !(event.ctrlKey && event.key.toLowerCase() === 'c' && terminal.hasSelection());
    });
    fit.fit();
    const ready = () => call('terminal.ready', { id: session.id, columns: terminal.cols, rows: terminal.rows }).catch(noop);
    if (session.snapshot) { terminal.write(session.snapshot,ready); terminalRestores.delete(session.id); } else ready();
    const observer = new ResizeObserver(() => { if (host.current?.clientHeight && host.current?.clientWidth) fit.fit(); }); observer.observe(host.current);
    return () => { observer.disconnect(); off(); terminal.dispose(); terminalInstances.delete(session.id); };
  }, []);
  useEffect(() => { if (active) requestAnimationFrame(() => { instance.current?.fit.fit(); instance.current?.terminal.focus(); }); }, [active]);
  return <div className={`terminal-view ${active ? 'active' : ''}`} ref={host} data-session={session.id} />;
}
function Infrastructure({ active, workspace, run, openBrowser, openTerminal }) {
  const [data,setData]=useState(null);
  useEffect(()=>{if(!active)return;const refresh=()=>run(async()=>setData(await call('machine')));refresh();const timer=setInterval(refresh,2000);return()=>clearInterval(timer);},[active]);
  const used=data?(data.totalMemory-data.availableMemory)/1073741824:0,total=data?data.totalMemory/1073741824:0;
  return <div className="station-page infrastructure-page"><Heading eyebrow={workspace?.name||'PARALLAX STATION'} title="Infrastructure">The hardware, networks, and execution environments that support your workspace.</Heading>
    <section className="infrastructure-local"><div className="section-heading"><div><span className="eyebrow">LOCAL HARDWARE</span><h2>{data?.name||'This computer'}</h2></div><Button quiet icon={CommandLineIcon} onClick={openTerminal}>Open terminal</Button></div>
      {data?<><p className="muted">{data.operatingSystem} · {data.processors} logical processors · Local device</p><div className="machine-grid"><section className="surface metric"><span>CPU utilization</span><strong>{data.cpuPercent==null?'Measuring…':`${data.cpuPercent.toFixed(0)}%`}</strong><progress max="100" value={data.cpuPercent||0}/></section><section className="surface metric"><span>Memory</span><strong>{used.toFixed(1)} <small>/ {total.toFixed(1)} GiB</small></strong><progress max="100" value={100*used/total}/></section></div></>:<p className="muted">Reading this device…</p>}
    </section>
    <section className="infrastructure-services"><div className="section-heading"><h2>Infrastructure services</h2></div><p className="muted">Manage the remote resources authorized for your account through these services.</p><div className="infrastructure-grid">{infrastructureProducts.map(product=><section className="surface infrastructure-card" key={product.key} data-infrastructure={product.key}><header><img src={product.icon} alt=""/><div><h3>{product.name}</h3><span>{product.key==='nebula'?'Networks, clusters, and cloud infrastructure':'Bootstrap, private fabric, and execution runtimes'}</span></div></header><p>{product.key==='nebula'?'Manage networks, connected machines, clusters, and cloud environments.':'Manage machine identities, private fabric membership, and runtime resources.'}</p><Button quiet icon={ArrowUpRightIcon} onClick={()=>openBrowser(product.href,product.name)}>{product.key==='nebula'?'Manage networks and clusters':'Manage fabric and runtimes'}</Button></section>)}</div><p className="fine-print">Remote inventory is managed in the infrastructure services; Station does not yet aggregate it here.</p></section>
  </div>;
}
function BrowserSurface({ tab, active, run }) {
  const region = useRef(null), [address, setAddress] = useState(tab.url);
  useEffect(() => setAddress(tab.url), [tab.url]);
  useEffect(() => {
    if (!active || tab.authenticationRequired) return;
    const update = () => { if (!region.current) return; const box = region.current.getBoundingClientRect(); call('browser.bounds', { id: tab.browserId, x: box.x, y: box.y, width: box.width, height: box.height }).catch(noop); };
    const observer = new ResizeObserver(update); observer.observe(region.current); window.addEventListener('resize', update); requestAnimationFrame(update);
    return () => { observer.disconnect(); window.removeEventListener('resize', update); call('browser.hide').catch(noop); };
  }, [active, tab.browserId, tab.authenticationRequired]);
  return <div className="browser-page"><div className="browser-toolbar"><button aria-label="Back" disabled={!tab.canGoBack} onClick={() => run(() => call('browser.back', { id: tab.browserId }))}><ArrowLeftIcon /></button><button aria-label="Forward" disabled={!tab.canGoForward} onClick={() => run(() => call('browser.forward', { id: tab.browserId }))}><ArrowRightIcon /></button><button aria-label="Reload" onClick={() => run(() => call('browser.reload', { id: tab.browserId }))}><ArrowPathIcon /></button><button aria-label="Open current page in default browser" title="Open in default browser" onClick={() => run(() => call('browser.external', { id: tab.browserId }))}><ArrowTopRightOnSquareIcon /></button><form onSubmit={event => { event.preventDefault(); run(() => call('browser.navigate', { id: tab.browserId, url: address })); }}><input aria-label="Browser address" value={address} onChange={event => setAddress(event.target.value)} /></form></div>{tab.error && <div className="station-notice">This page could not load: {tab.error}</div>}{tab.authenticationRequired ? <div className="browser-auth-pending"><Empty title="Continue in your browser" action={<><Button icon={ArrowTopRightOnSquareIcon} onClick={() => run(() => call('browser.external', { id: tab.browserId }))}>Open in browser</Button><Button quiet icon={ArrowPathIcon} onClick={() => run(() => call('browser.navigate', { id: tab.browserId, url: tab.url }))}>Try this tab again</Button></>}>Sign-in opened in your default browser. This tab can reopen when an app session is available.</Empty></div> : <div className="browser-region" ref={region} />}</div>;
}
createRoot(document.getElementById('root')).render(<App />);
