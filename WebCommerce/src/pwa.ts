import { useSyncExternalStore } from 'react';
interface InstallPrompt extends Event { prompt():Promise<void>; userChoice:Promise<{outcome:string}> }
interface PwaState { enabled:boolean; offline:boolean; networkIssue:boolean; updateAvailable:boolean; installAvailable:boolean; applying:boolean; error:string|null }
export class PwaStore {
  state:PwaState={enabled:false,offline:typeof navigator!=='undefined'&&navigator.onLine===false,networkIssue:false,updateAvailable:false,installAvailable:false,applying:false,error:null};
  private listeners=new Set<()=>void>();private registration:ServiceWorkerRegistration|null=null;private installPrompt:InstallPrompt|null=null;
  private pageRelease='';private activeNewer=false;private started=false;private lastCheck=0;private freeze:(value:boolean)=>void=()=>{};
  subscribe=(listener:()=>void)=>{this.listeners.add(listener);return()=>{this.listeners.delete(listener)}};
  snapshot=()=>this.state;
  private patch(value:Partial<PwaState>){this.state={...this.state,...value};for(const listener of this.listeners)listener()}
  networkFailed(){this.patch({offline:true,networkIssue:typeof navigator==='undefined'||navigator.onLine!==false})}
  networkSucceeded(){this.patch({offline:false,networkIssue:false})}
  async start(enabled:boolean,freeze:(value:boolean)=>void){
    if(this.started)return;this.started=true;this.freeze=freeze;
    const online=()=>{if(navigator.onLine===false)this.patch({offline:true,networkIssue:false})};window.addEventListener('online',online);window.addEventListener('offline',online);online();
    if(!enabled||!window.isSecureContext||!('serviceWorker' in navigator))return;
    this.patch({enabled:true});this.pageRelease=document.querySelector('meta[name="mango-ttb-release"]')?.getAttribute('content')??'';
    window.addEventListener('beforeinstallprompt',event=>{event.preventDefault();this.installPrompt=event as InstallPrompt;this.patch({installAvailable:true})});
    window.addEventListener('appinstalled',()=>{this.installPrompt=null;this.patch({installAvailable:false})});
    navigator.serviceWorker.addEventListener('controllerchange',()=>{if(this.state.applying){location.reload();return}void this.inspectVersion()});
    try{
      const workerUrl=new URL('mango-ttb-sw.js',new URL('./',location.href));
      if(/^[a-f0-9]{64}$/.test(this.pageRelease))workerUrl.searchParams.set('__mangos_release',this.pageRelease);
      this.registration=await navigator.serviceWorker.register(workerUrl,{scope:'./',updateViaCache:'none'});
      const inspect=()=>{this.patch({updateAvailable:!!this.registration?.waiting||this.activeNewer});void this.inspectVersion()};
      this.registration.addEventListener('updatefound',()=>{const installing=this.registration?.installing;installing?.addEventListener('statechange',()=>{if(installing.state==='installed')setTimeout(inspect,0)})});
      inspect();this.lastCheck=Date.now();
      document.addEventListener('visibilitychange',()=>{if(document.visibilityState==='visible'&&Date.now()-this.lastCheck>300000){this.lastCheck=Date.now();void this.registration?.update().catch(()=>{})}});
      await this.registration.update().catch(()=>{});
    }catch{/* A cache/install failure must not stop online commerce or discard its existing shell. */}
  }
  private async inspectVersion(){
    const controller=navigator.serviceWorker.controller;if(!controller)return;
    const channel=new MessageChannel();const timeout=setTimeout(()=>{channel.port1.close();channel.port2.close()},2000);
    channel.port1.onmessage=event=>{clearTimeout(timeout);channel.port1.close();channel.port2.close();const release=event.data?.release;if(typeof release==='string'&&/^[a-f0-9]{64}$/.test(release)){
      this.activeNewer=!!this.pageRelease&&release!==this.pageRelease;this.patch({updateAvailable:!!this.registration?.waiting||this.activeNewer});
    }};
    controller.postMessage({type:'GET_VERSION'},[channel.port2]);
  }
  async applyUpdate(){
    if(this.state.applying||!this.state.updateAvailable||this.state.offline||window.MangoCommerceIsBusy?.())return false;
    const waiting=this.registration?.waiting;if(!waiting&&!this.activeNewer)return false;
    this.patch({applying:true,error:null});this.freeze(true);
    if(!waiting){location.reload();return true}
    waiting.postMessage({type:'ACTIVATE'});
    setTimeout(()=>{if(this.state.applying){this.patch({applying:false,error:'อัปเดตไม่สำเร็จ กรุณาลองอีกครั้ง'});this.freeze(false)}},15000);
    return true;
  }
  async install(){const prompt=this.installPrompt;if(!prompt||this.state.offline||this.state.applying)return;this.installPrompt=null;this.patch({installAvailable:false});await prompt.prompt();await prompt.userChoice}
}
export const pwa=new PwaStore();
export const usePwa=()=>useSyncExternalStore(pwa.subscribe,pwa.snapshot,pwa.snapshot);
