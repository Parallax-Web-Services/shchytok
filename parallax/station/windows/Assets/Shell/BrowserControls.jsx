import React, { useEffect, useState } from 'react';
import { call } from './bridge';

export default function BrowserControls({ run, openBrowser }) {
  const [mode,setMode]=useState('station'),[address,setAddress]=useState(''),[message,setMessage]=useState('');
  useEffect(()=>{run(async()=>setMode((await call('browser.preferences')).linkMode));},[]);
  return <section className="surface browser-settings"><h2>Browser behavior</h2>
    <label className="field">Open web links from Station in<select aria-label="Open web links in" value={mode} onChange={event=>{const next=event.target.value;run(async()=>{await call('browser.preferences.save',{linkMode:next});setMode(next);});}}><option value="station">Station tabs</option><option value="external">My default browser</option></select></label>
    <p>Sign-in always opens in your default browser. App sessions connect separately; passwords and browser cookies are not copied.</p>
    <div className="browser-setting-actions"><button className="station-button quiet" onClick={()=>run(()=>call('browser.defaultSettings'))}>Choose default browser in Windows</button><button className="station-button quiet" onClick={()=>run(async()=>{await call('browser.registerLinks');setMessage('Browser-to-Station links are enabled for your Windows account.');})}>Enable browser-to-Station links</button></div>
    <p>To send a page back, enable Station links and add the bookmark below to your browser. It sends only the current page address.</p>
    <button className="station-button quiet" onClick={()=>run(async()=>{await call('clipboard.copy',{text:"javascript:location.href='parallax-station://open?url='+encodeURIComponent(location.href)"});setMessage('Bookmark address copied. Create a browser bookmark named Send to Station and paste this as its address.');})}>Copy Send to Station bookmark</button>
    <form className="browser-open-form" onSubmit={event=>{event.preventDefault();openBrowser(address,'Browser',{forceInside:true});}}><label className="field">Or paste a page address<input aria-label="Open address in Station" type="url" required value={address} onChange={event=>setAddress(event.target.value)} placeholder="https://…" /></label><button className="station-button" type="submit">Open in Station</button></form>
    {message&&<p role="status">{message}</p>}
  </section>;
}
