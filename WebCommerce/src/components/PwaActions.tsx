import { Download,RefreshCw,WifiOff } from 'lucide-react';
import { pwa,usePwa } from '../pwa';
import { useCommerce } from '../store';
export function PwaActions(){
  const state=usePwa();const commerce=useCommerce();
  const pending=commerce.busy||!!window.MangoCommerceIsBusy?.();
  return <>{state.offline?<span className="pwa-offline" role="status" title="ต้องเชื่อมต่อบริการเพื่อซื้อสินค้าและตรวจการชำระเงิน"><WifiOff size={18}/><span>{state.networkIssue?'เชื่อมต่อไม่ได้':'ออฟไลน์'}</span></span>:null}
    {state.updateAvailable?<button className="icon-button pwa-update" aria-label="อัปเดตแอป" disabled={pending||state.offline||state.applying} title={state.error??(pending?'อัปเดตได้หลังจบรายการนี้':'มีเวอร์ชันใหม่ อัปเดตเมื่อพร้อม')} onClick={()=>void pwa.applyUpdate()}><RefreshCw/><span className="pwa-update-dot"/></button>:state.installAvailable?<button className="icon-button" aria-label="ติดตั้งแอป" disabled={state.offline||state.applying} title="เพิ่ม MANGOs ไว้บนอุปกรณ์" onClick={()=>void pwa.install()}><Download/></button>:null}</>;
}
