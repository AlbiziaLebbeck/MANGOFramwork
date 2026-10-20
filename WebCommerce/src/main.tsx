import { createRoot } from 'react-dom/client';
import App from './App';
import { bridge, type UnityHost } from './bridge';
import { commerce } from './store';
import { pwa } from './pwa';
import { hasOpenUnpaidOrder } from './types';
import './styles.css';
declare global {interface Window{unityInstance?:UnityHost}}

const root=document.getElementById('mango-ttb-root')??document.body.appendChild(Object.assign(document.createElement('div'),{id:'mango-ttb-root'}));
if(!root.dataset.mounted){
  root.dataset.mounted='true';window.MangoTtb=bridge;
  window.MangoCommerceIsBusy=()=>commerce.state.busy||!!commerce.pendingCheckout()||hasOpenUnpaidOrder(commerce.state.order);
  void pwa.start(window.MANGOS_TTB_CONFIG?.pwaEnabled===true,value=>commerce.patch({busy:value}));
  createRoot(root).render(<App/>);
  if(window.unityInstance)bridge.attachUnity(window.unityInstance);
  void commerce.bootstrap();
  const canvas=document.querySelector<HTMLCanvasElement>('#unity-canvas');
  if(canvas){const pointers=new Map<number,{x:number;y:number}>();let multiple=false;
    canvas.addEventListener('pointerdown',event=>{if(commerce.state.screen!=='gallery'||event.pointerType==='mouse'&&event.button!==0)return;pointers.set(event.pointerId,{x:event.clientX,y:event.clientY});if(pointers.size>1)multiple=true});
    canvas.addEventListener('pointercancel',event=>{pointers.delete(event.pointerId);multiple=pointers.size>0});
    canvas.addEventListener('pointerup',event=>{const start=pointers.get(event.pointerId);pointers.delete(event.pointerId);const tap=start&&!multiple&&Math.hypot(event.clientX-start.x,event.clientY-start.y)<8;if(!pointers.size)multiple=false;if(!tap||commerce.state.screen!=='gallery')return;const rect=canvas.getBoundingClientRect();if(rect.width>0&&rect.height>0)bridge.send('world.tap',{x:(event.clientX-rect.left)/rect.width,y:(event.clientY-rect.top)/rect.height})});
  }
}
