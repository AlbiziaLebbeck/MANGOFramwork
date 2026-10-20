import type { ReactNode } from 'react';
import { ArrowLeft, CheckCircle2, Clock3, ChevronRight, Download, RefreshCw, Search } from 'lucide-react';
import { useCommerce, commerce } from '../store';
import type { CommerceDocument } from '../types';
import { admin } from './store';
export const run = (action: () => Promise<unknown>) =>
  void commerce.run(async () => {
    await action();
  });
export const stamp = (value: string) =>
  new Intl.DateTimeFormat('th-TH', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'Asia/Bangkok',
  }).format(new Date(value));
export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="admin-field">
      <span>{label}</span>
      {children}
    </label>
  );
}
export function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="admin-fact">
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
  );
}
export function State({ value, label }: { value: string; label?: string }) {
  return (
    <span
      className={`admin-state ${['Resolved', 'Complete', 'Confirmed', 'Paid', 'Ready'].includes(value) ? 'ok' : ''}`}
    >
      {['Resolved', 'Complete', 'Confirmed', 'Paid', 'Ready'].includes(value) ? (
        <CheckCircle2 size={18} />
      ) : (
        <Clock3 size={18} />
      )}{' '}
      {label ?? value}
    </span>
  );
}
export function Back({ label = 'กลับรายการ' }: { label?: string }) {
  return (
    <button className="text-button admin-back" onClick={() => admin.back()}>
      <ArrowLeft size={20} />
      {label}
    </button>
  );
}
export function RowEnd() {
  return <ChevronRight className="admin-row-end" size={19} />;
}
export function Refresh({ onClick }: { onClick?: () => void }) {
  const { busy } = useCommerce();
  return (
    <button
      type="button"
      className="admin-refresh"
      disabled={busy}
      aria-label="อัปเดตรายการ"
      title="อัปเดตรายการ"
      onClick={onClick ?? (() => run(() => admin.refresh()))}
    >
      <RefreshCw size={21} />
    </button>
  );
}
export function SearchField({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  return (
    <label className="admin-search">
      <Search size={21} />
      <input
        aria-label="ค้นหาในรายการที่โหลด"
        placeholder="ค้นหาในรายการที่โหลด"
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
    </label>
  );
}
export function Documents({ items }: { items: CommerceDocument[] }) {
  const { busy } = useCommerce();
  return (
    <div className="admin-documents">
      {items.map((item) => (
        <div key={item.id}>
          <div>
            <strong>{item.kind === 'Receipt' ? 'ใบเสร็จรับเงิน' : 'หลักฐานยกเลิกรายการ'}</strong>
            <small>{item.number}</small>
          </div>
          {item.state === 'Ready' ? (
            <button className="text-button" disabled={busy} onClick={() => run(() => admin.download(item))}>
              <Download size={18} />
              ดาวน์โหลด
            </button>
          ) : (
            <State value={item.state} label="กำลังจัดทำ" />
          )}
        </div>
      ))}
    </div>
  );
}
