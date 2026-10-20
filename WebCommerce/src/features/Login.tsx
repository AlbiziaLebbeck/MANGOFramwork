import { t } from '../i18n';
import { useState } from 'react';
import { LogIn } from 'lucide-react';
import { commerce, useCommerce } from '../store';
import { MockNotice } from '../components/Ui';
export default function LoginScreen() {
  const { config, busy, sessionSyncing } = useCommerce();
  const [subject, setSubject] = useState('demo-buyer');
  return (
    <div className="login-panel">
      <h1>{sessionSyncing ? t('กำลังเชื่อมต่อบัญชี MANGOs') : t('เข้าสู่ระบบ MANGOs')}</h1>
      <p>
        {sessionSyncing
          ? t('กำลังตรวจสอบบัญชีที่เข้าสู่ระบบในเมตาเวิร์ส')
          : t('ใช้บัญชีเดียวกับเมตาเวิร์ส เพื่อดูคำสั่งซื้อ ใบเสร็จ และสินค้าของคุณ')}
      </p>
      <button
        className="button primary"
        disabled={busy || sessionSyncing}
        onClick={() => void commerce.signIn()}
      >
        <LogIn size={21} />
        {sessionSyncing ? t('กำลังเชื่อมต่อ…') : t('เข้าสู่ระบบ MANGOs')}
      </button>
      {!sessionSyncing ? (
        <p className="muted">{t('หากเข้าสู่ระบบ MANGOs แล้ว ระบบจะเชื่อมต่อให้อัตโนมัติ')}</p>
      ) : null}
      {config?.mockSimulation && config.environment === 'Local' ? (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void commerce.signInLocal(subject);
          }}
          className="local-login"
        >
          <MockNotice />
          <label>
            {t('ชื่อบัญชีทดสอบ')}
            <input
              value={subject}
              onChange={(e) => setSubject(e.target.value)}
              required
              pattern={'[A-Za-z0-9_\\-]{1,80}'}
              maxLength={80}
            />
          </label>
          <button className="button secondary" disabled={busy || sessionSyncing}>
            {t('เข้าสู่ระบบทดสอบ Local')}
          </button>
        </form>
      ) : null}
    </div>
  );
}
