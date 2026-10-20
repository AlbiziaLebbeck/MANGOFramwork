import { t } from '../i18n';
import { useState } from 'react';
import { Copy, Send } from 'lucide-react';
import { useCommerce } from '../store';
import { Empty } from '../components/Ui';
import { admin, useAdmin } from './store';
import { Back, Fact, Field, Refresh, RowEnd, State, run } from './Ui';
import { exactMoney } from './types';
const labels: Record<string, string> = {
  Archived: 'รอตรวจรูปแบบ',
  Quarantined: 'ต้องตรวจสอบ',
  RevisionReview: 'ไฟล์เปลี่ยน',
  Staged: 'ผ่านการตรวจรูปแบบ',
  Complete: 'ประมวลผลแถวครบ',
  Replay: 'ไฟล์เดิมซ้ำ',
};
const parse = (text: string) => {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
};
export default function Reports() {
  const state = useAdmin();
  const { busy } = useCommerce();
  const [kind, setKind] = useState('');
  const rows = state.reports.filter((x) => !kind || x.kind.toLowerCase() === kind);
  const detail = state.report;
  return (
    <div className={`admin-columns ${detail ? 'has-detail' : ''}`}>
      <section className="admin-queue">
        <h1>{t('รายงานธนาคาร')}</h1>
        {state.summary?.reportsState !== 'Configured' ? (
          <div className="admin-report-notice">
            <h2>{t('ยังไม่เปิดรับไฟล์ธนาคาร')}</h2>
            <p>{t('รอข้อมูล SFTP และการยืนยันรูปแบบไฟล์')}</p>
          </div>
        ) : (
          <p className="admin-report-notice">
            {t('รอไฟล์รายงาน')} {state.summary.waitingFiles ?? '—'} {t('· ประมวลผลตามหลักฐานที่รับแล้ว')}{' '}
          </p>
        )}
        <div className="admin-toolbar">
          <select aria-label={t('ประเภทรายงาน')} value={kind} onChange={(e) => setKind(e.target.value)}>
            <option value="">{t('ISO / LOST ทั้งหมด')}</option>
            <option value="iso">ISO</option>
            <option value="lost">LOST</option>
          </select>
          <Refresh />
        </div>
        <div className="admin-table">
          <div className="admin-table-head report-row">
            <span>{t('ชื่อไฟล์')}</span>
            <span>{t('วันที่รายงาน')}</span>
            <span>{t('สถานะ')}</span>
            <span>{t('รุ่น')}</span>
          </div>
          {rows.map((row) => (
            <button
              key={row.id}
              className={`admin-table-row report-row ${detail?.report.id === row.id ? 'selected' : ''}`}
              onClick={() => run(() => admin.chooseReport(row.id))}
            >
              <span>{row.fileName}</span>
              <span>{row.fileDate}</span>
              <State value={row.state} label={labels[row.state] ?? row.state} />
              <span>v{row.revision}</span>
              <RowEnd />
            </button>
          ))}
          {state.loaded && !rows.length ? <Empty>{t('ยังไม่มีไฟล์ธนาคารตามเงื่อนไข')}</Empty> : null}
        </div>
        {state.reportCursor ? (
          <button className="text-button" disabled={busy} onClick={() => run(() => admin.refresh(true))}>
            {t('ดูไฟล์เพิ่มเติม')}{' '}
          </button>
        ) : null}
      </section>
      <aside className="admin-inspector">
        {detail ? (
          <ReportInspector key={detail.report.id} />
        ) : (
          <Empty>{t('เลือกไฟล์เพื่อดูผลตรวจรูปแบบและการเทียบรายการ')}</Empty>
        )}
      </aside>
    </div>
  );
}
const outcomeLabels: Record<string, string> = {
  MatchedCredit: 'รับเงินตรงกับบัญชี',
  MatchedVoidDebit: 'รายการยกเลิกตรงกับบัญชี',
  MatchedVoidedOriginal: 'ยอดเดิมที่ถูกยกเลิก',
  ReportUnallocatedCredit: 'รับเงินยังไม่จัดสรร',
  ReportUnknownVoid: 'ยกเลิกที่ยังไม่พบยอดเดิม',
  ReportPaymentMismatch: 'ข้อมูลรับเงินไม่ตรง',
  ReportInstructionWidthMismatch: 'ความยาวอ้างอิงไม่ตรง',
  ReportCreditAfterVoid: 'พบรับเงินหลังยกเลิก',
  ReportPaymentTimeConflict: 'เวลารับเงินขัดแย้ง',
};
const displayAmount = (value: unknown) =>
  (typeof value === 'string' && /^-?\d+$/.test(value)) ||
  (typeof value === 'number' && Number.isSafeInteger(value))
    ? exactMoney(value as string | number)
    : t('ต้องตรวจหลักฐานยอดเต็ม');
function ReportInspector() {
  const { report: detail, summary, reports } = useAdmin();
  const { busy } = useCommerce();
  const [reason, setReason] = useState(''),
    [copied, setCopied] = useState(false);
  if (!detail) return null;
  const report = detail.report;
  const totals = parse(report.totalsJson);
  const comparison = parse(report.comparisonJson);
  const canReprocess =
    summary?.reportsState === 'Configured' && ['Quarantined', 'RevisionReview'].includes(report.state);
  const parsed = ['Complete', 'Staged'].includes(report.state),
    review =
      detail.outcomes.some(
        (x) => !['MatchedCredit', 'MatchedVoidDebit', 'MatchedVoidedOriginal'].includes(x.outcome),
      ) ||
      comparison?.absentCount > 0 ||
      comparison?.timeAttributionComplete === false;
  return (
    <>
      <Back />
      <div className="admin-detail-heading">
        <h2>{report.fileName}</h2>
        <State value={report.state} label={labels[report.state] ?? report.state} />
      </div>
      <p className="muted">
        {reports.find((x) => x.id === report.id)?.fileDate} {t('· รุ่น v')}
        {report.revision}
      </p>
      <section>
        <h3>{t('รายละเอียดรายงาน')}</h3>
        {totals && Number.isInteger(totals.creditCount) && Number.isInteger(totals.debitCount) ? (
          <dl className="admin-report-totals">
            <div className="admin-report-total-heading">{t('ยอดรวมในไฟล์')}</div>
            <Fact label={t('รายการรับเงิน')}>{totals.creditCount}</Fact>
            <Fact label={t('รายการยกเลิก')}>{totals.debitCount}</Fact>
            <Fact label={t('ยอด Credit')}>{displayAmount(totals.creditMinor)}</Fact>
            <Fact label={t('ยอด Debit')}>{displayAmount(totals.debitMinor)}</Fact>
            <Fact label={t('ผลตรวจยอดรวม')}>
              <State
                value={parsed ? 'Complete' : 'Review'}
                label={parsed ? t('ผ่านยอดรวมในไฟล์') : t('ยังไม่ยืนยัน')}
              />
            </Fact>
          </dl>
        ) : (
          <p className="muted">{t('ยังไม่มียอดรวมที่ผ่านการตรวจรูปแบบ')}</p>
        )}
        {report.failureCode ? (
          <p className="message warning">
            {t('ต้องตรวจสอบ ·')} {report.failureCode}
          </p>
        ) : null}
        {review ? <p className="message warning">{t('ยังมีรายการที่ต้องตรวจสอบ')}</p> : null}
      </section>
      <section>
        <h3>{t('ผลเทียบรายการ')}</h3>
        {detail.outcomes.length ? (
          <dl>
            {detail.outcomes.map((row) => (
              <Fact key={row.outcome} label={outcomeLabels[row.outcome] ?? row.outcome}>
                {row.count}
              </Fact>
            ))}
          </dl>
        ) : (
          <p className="muted">{t('ยังไม่มีผลประมวลผลแถวรายงาน')}</p>
        )}
        {comparison?.timeAttributionComplete === false ? (
          <p className="muted">
            {t('ยังมีรายการที่ไม่ทราบเวลาเงินเข้าต้นทาง จึงยังยืนยันการเทียบรายวันไม่ได้')}
          </p>
        ) : comparison?.scope === 'LostRepairRowsOnly' ? (
          <p className="muted">{t('LOST ใช้ซ่อมรายการ ไม่ใช้ยืนยันความครบถ้วนรายวัน')}</p>
        ) : null}
        <p className="muted">{t('การผ่านรูปแบบไฟล์และยอดรวม ไม่ใช่การยืนยันว่าเทียบรายการรับเงินครบแล้ว')}</p>
      </section>
      <section>
        <h3>{t('การตรวจสอบซ้ำ')}</h3>
        <Field label={t('เหตุผลที่ตรวจสอบซ้ำ')}>
          <textarea
            rows={3}
            maxLength={500}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            disabled={!canReprocess}
          />
        </Field>
        <div className="admin-character-count">{reason.length}/500</div>
        <button
          className="button secondary"
          disabled={busy || !canReprocess || !reason.trim()}
          onClick={() => run(() => admin.reprocess(reason))}
        >
          <Send size={18} />
          {t('ส่งตรวจสอบไฟล์เดิม')}{' '}
        </button>
        {!canReprocess ? (
          <p className="muted">
            {summary?.reportsState !== 'Configured'
              ? t('ยังไม่พร้อมตรวจไฟล์ด้วยรูปแบบที่อนุมัติ')
              : t('ไฟล์ที่สร้างแถวหลักฐานแล้วใช้ซ้ำโดยไม่เปลี่ยนแถวเดิม')}
          </p>
        ) : null}
      </section>
      <section>
        <div className="admin-proof-reference">
          <span>
            {t('หลักฐานอ้างอิง')}{' '}
            <small>
              {report.encryptedSha256.slice(0, 8)}…{report.encryptedSha256.slice(-8)}
            </small>
          </span>
          <button
            className="text-button"
            aria-label={t('คัดลอกหลักฐานอ้างอิง')}
            onClick={() =>
              run(async () => {
                await navigator.clipboard.writeText(report.encryptedSha256);
                setCopied(true);
              })
            }
          >
            <Copy size={18} />
            {copied ? t('คัดลอกแล้ว') : ''}
          </button>
        </div>
        <details className="admin-config">
          <summary>{t('ดูหลักฐานตรวจสอบฉบับเต็ม')}</summary>
          <dl>
            <Fact label={t('เข้ารหัส SHA256')}>{report.encryptedSha256}</Fact>
            <Fact label={t('ถอดรหัส SHA256')}>{report.plaintextSha256 ?? t('ยังไม่ผ่านการถอดรหัส')}</Fact>
            {report.replayOfId ? <Fact label={t('ไฟล์เดิม')}>{report.replayOfId}</Fact> : null}
          </dl>
          <pre className="admin-private-json">{JSON.stringify({ totals, comparison }, null, 2)}</pre>
        </details>
      </section>
    </>
  );
}
