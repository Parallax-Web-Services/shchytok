import React,{useEffect,useRef,useState} from 'react';
import {call} from './bridge';

export default function SlipstreamWorkspace({active,workspace,connected,openConnect,openWorkstream,run,onWorkstreams}) {
  const [streams,setStreams]=useState([]),[loading,setLoading]=useState(false);
  const current=useRef(workspace?.key);current.current=workspace?.key;
  const refresh=()=>run(async()=>{if(!workspace)return;const key=workspace.key;setLoading(true);try{const result=await call('workstreams',{workspace:workspace.key});if(current.current!==key)return;setStreams(result.workstreams||[]);onWorkstreams(result.workstreams||[]);}finally{setLoading(false);}});
  useEffect(()=>{if(active&&workspace)refresh();},[active,workspace?.key]);
  return <div className="station-page slipstream-workspace"><header className="page-heading"><div><div className="eyebrow">{workspace?.name||'ENGINEERING'}</div><h1>SLIPSTREAM</h1><p>Your projects and workstreams use Station’s existing connection.</p></div><button className="station-button quiet" onClick={refresh} disabled={!workspace||loading}>{loading?'Refreshing…':'Refresh'}</button></header>
    {!connected||!workspace?<div className="empty-state"><h3>Connect your workspace</h3><p>Sign in in your default browser and connect this device to resume your work.</p><button className="station-button" onClick={openConnect}>Connect SLIPSTREAM</button></div>:<><p className="muted">Use the project tree for tasks and subtasks, or resume a workstream below.</p><div className="work-grid" aria-label="SLIPSTREAM workstreams">{streams.map(work=><button className="work-card" key={work.id} onClick={()=>openWorkstream(work.id)}><span className={`status status-${work.status||'unknown'}`}>{work.status||'Unknown status'}</span><h3>{work.name||work.long_name||`Workstream #${work.id}`}</h3><p>{work.project?.name||'Workstream'}</p><footer>Resume work →</footer></button>)}</div>{!loading&&!streams.length&&<div className="empty-state"><h3>No workstreams reported</h3><p>Existing workstreams will appear when SLIPSTREAM reports them for this workspace.</p></div>}</>}
  </div>;
}
