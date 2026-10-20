import { t } from '../i18n';
import { useEffect, useState } from 'react';
import { ArrowLeft, Download, RefreshCw } from 'lucide-react';
import { commerce, useCommerce } from '../store';
import { money } from '../types';
import { IconButton, MockNotice } from '../components/Ui';

export default function PaymentScreen() {
  const { order, qr, verification, busy, config } = useCommerce();
  const [now, setNow] = useState(Date.now());
  const [scenario, setScenario] = useState('success');
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);
  useEffect(() => {
    let running = false;
    const timer = setInterval(() => {
      if (document.visibilityState !== 'visible' || running) return;
      running = true;
      void commerce
        .refreshOrder()
        .catch(() => {})
        .finally(() => {
          running = false;
        });
    }, 3000);
    return () => clearInterval(timer);
  }, [order?.id]);
  if (!order)
    return (
      <div className="empty-state">
        <p>{t('ไม่พบคำสั่งซื้อ')}</p>
      </div>
    );
  const expires = qr?.expiresAt ?? order.expiresAt;
  const remaining = Math.max(0, Math.ceil((Date.parse(expires) - now) / 1000));
  const time = `${Math.floor(remaining / 60)
    .toString()
    .padStart(2, '0')}:${(remaining % 60).toString().padStart(2, '0')}`;
  const availableAt = verification?.nextCheckAt ?? qr?.verificationAvailableAt ?? expires;
  const delay = Math.max(0, Math.ceil((Date.parse(availableAt) - now) / 1000));
  const checks = verification?.checksRemaining ?? qr?.checksRemaining ?? 0;
  const closed = order.state !== 'Open' || order.paymentState !== 'Unpaid';
  return (
    <div className="payment-panel">
      <div className="panel-title">
        <IconButton label={t('กลับไปสำรวจสินค้า')} onClick={() => commerce.close()}>
          <ArrowLeft />
        </IconButton>
        <h1>{t('ชำระด้วย QR')}</h1>
      </div>
      <MockNotice />
      <div className="payment-reference">
        <span>{t('หมายเลขคำสั่งซื้อ')}</span>
        <strong>{order.number}</strong>
        <span>{t('ยอดชำระทั้งหมด')}</span>
        <b>{money(order.totalMinor)}</b>
        {qr && qr.mode !== 'Mock' ? (
          <p>
            {t('ผู้รับเงิน')} {qr.beneficiaryName}
          </p>
        ) : null}
      </div>
      {!closed && remaining > 0 && qr ? (
        <>
          <div className="qr-frame">
            <img
              src={qr.imageDataUrl}
              alt={
                qr.mode === 'Mock'
                  ? t('QR จำลองสำหรับทดสอบ ไม่มีการรับเงินจริง')
                  : t('QR สำหรับชำระตามยอดคำสั่งซื้อ')
              }
              width={280}
              height={280}
            />
            {qr.mode === 'Mock' ? <p>{t('QR จำลองสำหรับทดสอบระบบ')}</p> : null}
          </div>
          <p className="payment-timer">
            {t('เหลือเวลา')} {time}
          </p>
          <p className="payment-wait">{t('กำลังรอการชำระเงิน')}</p>
          <a className="button secondary" href={qr.imageDataUrl} download={`${order.number}-qr.png`}>
            <Download size={21} />
            {t('บันทึก QR')}
          </a>
          <p className="muted text-center">{t('เปิดด้วยแอปธนาคารที่รองรับ')}</p>
        </>
      ) : (
        <div className="message warning">
          {closed
            ? t('รายการนี้ไม่ได้อยู่ในสถานะรอชำระ')
            : remaining === 0
              ? t('QR หมดอายุ กรุณาตรวจสอบสถานะก่อนสั่งซื้อใหม่')
              : t('กำลังเตรียม QR')}
        </div>
      )}
      {!qr && !closed && remaining > 0 ? (
        <button
          className="button secondary"
          disabled={busy}
          onClick={() => void commerce.run(() => commerce.openOrder(order.id))}
        >
          {t('ลองโหลด QR อีกครั้ง')}
        </button>
      ) : null}
      <button
        className="button secondary verify"
        disabled={busy || delay > 0 || checks <= 0 || closed}
        onClick={() => void commerce.verify()}
      >
        <RefreshCw size={20} />
        {delay > 0 ? t('ตรวจสอบการชำระเงินใน {0} วินาที', [delay]) : t('ตรวจสอบการชำระเงิน')}
      </button>
      <p className="muted text-center">
        {t('ตรวจสอบได้อีก')} {checks} {t('ครั้ง')}
      </p>
      <button
        className="text-button"
        disabled={busy}
        onClick={() => void commerce.run(() => commerce.refreshOrder())}
      >
        {t('อัปเดตสถานะคำสั่งซื้อ')}
      </button>
      <div className="payment-lines">
        {order.lines.map((line) => (
          <div key={line.id}>
            <span>
              {line.name} × {line.quantity}
            </span>
            <span>{money(line.totalMinor)}</span>
          </div>
        ))}
      </div>
      {config?.mockSimulation && config.environment === 'Local' ? (
        <details className="mock-tools">
          <summary>{t('เครื่องมือทดสอบ Local')}</summary>
          <label>
            {t('สถานการณ์')}
            <select value={scenario} onChange={(e) => setScenario(e.target.value)}>
              <option value="success">{t('รับเงินสำเร็จ')}</option>
              <option value="lost-callback">{t('Callback สูญหาย')}</option>
              <option value="duplicate">{t('Callback ซ้ำ')}</option>
              <option value="amount-mismatch">{t('ยอดไม่ตรง')}</option>
              <option value="late">{t('จ่ายหลังหมดอายุ')}</option>
              <option value="extra-credit">{t('มีเงินเข้าเพิ่ม')}</option>
            </select>
          </label>
          <button
            className="button secondary"
            disabled={busy}
            onClick={() => void commerce.simulate(scenario)}
          >
            {t('จำลองการชำระ (ไม่มีเงินจริง)')}
          </button>
        </details>
      ) : null}
      <div className="payment-bottom">
        <button className="text-button" onClick={() => commerce.close()}>
          {t('กลับไปสำรวจสินค้า')}
        </button>
        {order.canCancel ? (
          <button className="text-button danger" disabled={busy} onClick={() => void commerce.cancel()}>
            {t('ยกเลิกคำสั่งซื้อ')}
          </button>
        ) : null}
      </div>
    </div>
  );
}
