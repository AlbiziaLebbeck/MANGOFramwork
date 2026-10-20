import { useState } from 'react';
import { LogIn } from 'lucide-react';
import { commerce, useCommerce } from '../store';
import { MockNotice } from '../components/Ui';
export default function LoginScreen(){const{config,busy}=useCommerce();const[subject,setSubject]=useState('demo-buyer');return <div className="login-panel"><h1>เข้าสู่ระบบเพื่อสั่งซื้อ</h1><p>ใช้บัญชี MANGOs เพื่อเก็บคำสั่งซื้อ ใบเสร็จ และสินค้าดิจิทัลไว้ในบัญชีของคุณ</p><button className="button primary" disabled={busy} onClick={()=>void commerce.signIn()}><LogIn size={21}/>เชื่อมต่อบัญชี MANGOs</button><p className="muted">หากยังไม่มีเซสชัน ให้เข้าสู่ระบบในเมตาเวิร์สก่อน</p>{config?.mockSimulation&&config.environment==='Local'?<form onSubmit={e=>{e.preventDefault();void commerce.signInLocal(subject)}} className="local-login"><MockNotice/><label>ชื่อบัญชีทดสอบ<input value={subject} onChange={e=>setSubject(e.target.value)} required pattern={'[A-Za-z0-9_\\-]{1,80}'} maxLength={80}/></label><button className="button secondary" disabled={busy}>เข้าสู่ระบบทดสอบ Local</button></form>:null}</div>}

