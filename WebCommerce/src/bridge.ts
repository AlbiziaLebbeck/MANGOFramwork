export interface UnityHost { SendMessage(object:string,method:string,payload:string):void }
export interface HostEvent { version:1; type:'host.attached'|'host.ready'|'host.stopped'|'product.open'|'preview.ready'|'preview.error'|'session.changed'|'session.end'|'game.ready'|'game.challenge'|'game.result'; modelKey?:string; productId?:string; requestId?:string; serverId?:string; challenge?:string; entitlementId?:string; ready?:boolean; result?:'equipped'|'denied'|'unavailable'|'removed' }
type Listener=(event:HostEvent)=>void;
export function parseHostEvent(raw:unknown):HostEvent|null{
  if(typeof raw==='string'){if(raw.length>4096)return null;try{raw=JSON.parse(raw)}catch{return null}}
  if(!raw||typeof raw!=='object')return null;const source=raw as Record<string,unknown>;const e:Record<string,unknown>={version:source.version,type:source.type};
  // Unity JsonUtility writes optional null strings as empty strings. Normalize only these known presentation fields.
  for(const key of ['modelKey','productId','requestId','serverId','challenge','entitlementId','ready','result'])if(source[key]!==undefined&&source[key]!==null&&source[key]!=='')e[key]=source[key];
  if(e.version!==1||!['host.attached','host.ready','host.stopped','product.open','preview.ready','preview.error','session.changed','session.end','game.ready','game.challenge','game.result'].includes(String(e.type)))return null;
  if(e.modelKey!==undefined&&(typeof e.modelKey!=='string'||!/^[A-Za-z0-9_-]{1,100}$/.test(e.modelKey)))return null;
  if(e.productId!==undefined&&(typeof e.productId!=='string'||! /^(?:[a-f0-9]{8}-){1}[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(e.productId)))return null;
  if(e.requestId!==undefined&&(typeof e.requestId!=='string'||e.requestId.length>80))return null;
  if(e.type==='product.open'&&!e.modelKey&&!e.productId)return null;
  if(e.type==='game.ready'&&typeof e.ready!=='boolean')return null;
  if(e.type==='game.challenge'&&(
    typeof e.serverId!=='string'||!/^[A-Za-z0-9_-]{1,64}$/.test(e.serverId)||typeof e.challenge!=='string'||!/^[a-f0-9]{64}$/.test(e.challenge)||
    typeof e.entitlementId!=='string'||!/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(e.entitlementId)||typeof e.requestId!=='string'||!/^[a-f0-9]{32}$/.test(e.requestId)))return null;
  if(e.type==='game.result'&&!['equipped','denied','unavailable','removed'].includes(String(e.result)))return null;
  return e as unknown as HostEvent;
}
export class CommerceBridge {
  private host:UnityHost|null=null;private listeners=new Set<Listener>();private nativeReady=false;
  get attached(){return this.host!==null}
  subscribe=(listener:Listener)=>{this.listeners.add(listener);return()=>{this.listeners.delete(listener)}};
  attachUnity=(host:UnityHost)=>{if(!host||typeof host.SendMessage!=='function')throw new Error('Invalid Unity host');this.host=host;this.receive({version:1,type:'host.attached'});if(this.nativeReady)this.receive({version:1,type:'host.ready'})};
  receive=(raw:unknown)=>{const event=parseHostEvent(raw);if(event){if(event.type==='host.ready')this.nativeReady=true;if(event.type==='host.stopped')this.nativeReady=false;if(event.type==='host.ready'||event.type==='host.stopped'){const canvas=document.querySelector<HTMLCanvasElement>('#unity-canvas');if(canvas)canvas.dataset.commerceReady=String(this.nativeReady)}if(event.type==='game.ready'||event.type==='host.stopped'||event.type==='session.end'){const canvas=document.querySelector<HTMLCanvasElement>('#unity-canvas');if(canvas)canvas.dataset.gameReady=String(event.type==='game.ready'&&event.ready===true)}for(const listener of this.listeners)listener(event)}};
  send(type:string,body:Record<string,unknown>={}){if(this.nativeReady)this.host?.SendMessage('MangoTtbCommerceBridge','OnBrowserMessage',JSON.stringify({version:1,type,...body}))}
  requestLogin(){this.send('session.login')}
}
export const bridge=new CommerceBridge();
declare global{interface Window{MangoTtb:CommerceBridge;MangoCommerceIsBusy?:()=>boolean}}

