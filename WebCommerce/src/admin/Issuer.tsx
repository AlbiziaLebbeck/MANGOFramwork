import { t } from '../i18n';
import { useState, type ChangeEvent } from 'react';
import { Save } from 'lucide-react';
import { commerce, useCommerce } from '../store';
import { Empty } from '../components/Ui';
import { admin, useAdmin } from './store';
import type { IssuerProfile } from './types';
import { Field, Refresh, run } from './Ui';

export default function Issuer() {
  const { issuer, loaded } = useAdmin();
  return (
    <section className="issuer-settings">
      <div className="issuer-heading">
        <h1>{t('ข้อมูลบริษัท')}</h1>
        <Refresh />
      </div>
      <p className="message warning">
        {t(
          'ข้อมูลทดสอบสำหรับใบเสร็จใหม่ ใบเสร็จที่ออกแล้วจะเก็บข้อมูลเดิมไว้ การบันทึกหน้านี้ไม่ได้รับรองข้อมูลบริษัทสำหรับ Production',
        )}
      </p>
      {issuer && loaded ? (
        <IssuerForm key={issuer.version} profile={issuer} />
      ) : (
        <Empty>{t('กำลังโหลดข้อมูลบริษัท…')}</Empty>
      )}
    </section>
  );
}

function IssuerForm({ profile }: { profile: IssuerProfile }) {
  const { busy } = useCommerce();
  const [fields, setFields] = useState(() => ({
    name: profile.name,
    taxId: profile.taxId,
    branch: profile.branch,
    address: profile.address,
    contact: profile.contact,
  }));
  const field = (key: keyof typeof fields) => ({
    value: fields[key],
    onChange: (e: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
      setFields((previous) => ({ ...previous, [key]: e.target.value })),
    required: true,
    disabled: busy || !profile.editable,
  });
  return (
    <form
      className="issuer-form"
      onSubmit={(event) => {
        event.preventDefault();
        run(async () => {
          await admin.saveIssuer({ ...fields, expectedVersion: profile.version });
          commerce.patch({ notice: 'บันทึกข้อมูลบริษัททดสอบแล้ว ใช้กับใบเสร็จใหม่' });
        });
      }}
    >
      <Field label={t('ชื่อบริษัท')}>
        <input {...field('name')} maxLength={200} />
      </Field>
      <Field label={t('เลขประจำตัวผู้เสียภาษี (ทดสอบ)')}>
        <input {...field('taxId')} inputMode="numeric" pattern="[0-9]{13}" minLength={13} maxLength={13} />
      </Field>
      <Field label={t('สาขา')}>
        <input {...field('branch')} maxLength={100} />
      </Field>
      <Field label={t('ที่อยู่บริษัท')}>
        <textarea {...field('address')} maxLength={1000} rows={4} />
      </Field>
      <Field label={t('ข้อมูลติดต่อ')}>
        <input {...field('contact')} maxLength={300} />
      </Field>
      {profile.editable ? (
        <button className="button primary" disabled={busy}>
          <Save size={20} />
          {t('บันทึกข้อมูลบริษัททดสอบ')}
        </button>
      ) : (
        <p>{t('ข้อมูล Production จัดการผ่านค่าตั้งระบบที่รับรองแล้ว')}</p>
      )}
    </form>
  );
}
