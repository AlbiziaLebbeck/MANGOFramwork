import { useState } from 'react';
import { Download, Upload } from 'lucide-react';
import { useCommerce } from '../store';
import { admin, useAdmin } from './store';
import { exactMoney, deliveryLabels, type BankCredit, type ManualRefund } from './types';
import { Fact, Field, run, stamp } from './Ui';

const available = (bank: BankCredit) =>
  BigInt(bank.amountMinor) -
  BigInt(bank.manualAttestedMinor ?? '0') -
  BigInt(bank.manualReservedMinor ?? '0');
const baht = (minor: bigint) => `${minor / 100n}.${(minor % 100n).toString().padStart(2, '0')}`;
const exactMinor = (input: string) => {
  if (!/^\d{1,17}(\.\d{1,2})?$/.test(input)) return null;
  const [whole, fraction = ''] = input.split('.');
  const minor = BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
  return minor > 0n && minor <= 9223372036854775807n ? minor.toString() : null;
};
export function ManualStart({
  bank,
  sourceCaseId = null,
}: {
  bank: BankCredit;
  sourceCaseId?: string | null;
}) {
  const { busy } = useCommerce();
  return (
    <button
      className="text-button"
      disabled={busy || bank.voided || available(bank) <= 0n}
      onClick={() => admin.patch({ manualDraft: { bank, sourceCaseId } })}
    >
      เปิดเคสคืนเงินภายนอก
    </button>
  );
}
export function ManualCreate() {
  const { manualDraft } = useAdmin();
  return manualDraft ? <CreateForm key={manualDraft.bank.id} bank={manualDraft.bank} /> : null;
}
function CreateForm({ bank }: { bank: BankCredit }) {
  const { busy } = useCommerce();
  const [amount, setAmount] = useState(baht(available(bank))),
    [reason, setReason] = useState(''),
    [confirm, setConfirm] = useState(false);
  const minor = exactMinor(amount),
    valid = minor !== null && BigInt(minor) <= available(bank);
  return (
    <section className="admin-refund-form" aria-label="เปิดเคสคืนเงินภายนอก">
      <h3>เปิดเคสคืนเงินภายนอก</h3>
      <p className="muted">บันทึกกระบวนการคืนเงินที่ดำเนินการภายนอก ระบบนี้ไม่ส่งคำสั่งโอนเงิน</p>
      <dl>
        <Fact label="ยอดรับเงินเดิม">{exactMoney(bank.amountMinor)}</Fact>
        <Fact label="อนุมัติหลักฐานแล้ว">{exactMoney(bank.manualAttestedMinor ?? '0')}</Fact>
        <Fact label="กันยอดในเคสอื่น">{exactMoney(bank.manualReservedMinor ?? '0')}</Fact>
        <Fact label="เปิดเคสเพิ่มได้">{exactMoney(available(bank).toString())}</Fact>
      </dl>
      <Field label="ยอดคืนเงินภายนอก (บาท)">
        <input
          inputMode="decimal"
          value={amount}
          onChange={(e) => {
            setAmount(e.target.value);
            setConfirm(false);
          }}
        />
      </Field>
      <Field label="เหตุผลเปิดเคส">
        <textarea
          rows={3}
          maxLength={500}
          value={reason}
          onChange={(e) => {
            setReason(e.target.value);
            setConfirm(false);
          }}
        />
      </Field>
      <label className="admin-check">
        <input type="checkbox" checked={confirm} onChange={(e) => setConfirm(e.target.checked)} />
        ยืนยันกันยอดนี้ไว้เพื่อตรวจสอบการคืนเงินภายนอก
      </label>
      <div className="admin-actions">
        <button
          className="button primary"
          disabled={busy || !valid || !reason.trim() || !confirm}
          onClick={() => valid && run(() => admin.createManual(minor!, reason))}
        >
          สร้างเคสตรวจสอบ
        </button>
        <button className="text-button" disabled={busy} onClick={() => admin.patch({ manualDraft: null })}>
          ปิดแบบฟอร์ม
        </button>
      </div>
    </section>
  );
}
const manualLabels = {
  Review: 'รอหลักฐาน',
  EvidenceSubmitted: 'รอเจ้าหน้าที่อีกคนตรวจ',
  Attested: 'อนุมัติหลักฐานแล้ว',
  Cancelled: 'ยกเลิกก่อนแจ้งโอน',
};
export function ManualInspector({ row }: { row: ManualRefund }) {
  const { busy } = useCommerce();
  const [reason, setReason] = useState(''),
    [reference, setReference] = useState(row.externalReference ?? ''),
    [paidAt, setPaidAt] = useState('');
  const [files, setFiles] = useState<string[]>([]),
    [confirm, setConfirm] = useState(false);
  const terminal = row.state === 'Attested' || row.state === 'Cancelled';
  const reset = () => setConfirm(false);
  return (
    <section className="admin-refund-form" aria-label="หลักฐานคืนเงินภายนอก">
      <h3>คืนเงินภายนอก · {manualLabels[row.state]}</h3>
      <p className="muted">การรับรองเอกสารโดยเจ้าหน้าที่ ไม่ใช่ผลยืนยัน Void จาก TTB</p>
      <dl>
        <Fact label="ยอดที่ขอคืน">{exactMoney(row.amountMinor)}</Fact>
        <Fact label="ผู้เปิดเคส">{row.createdBy}</Fact>
        <Fact label="ผลต่อคำสั่งซื้อ">
          {row.affectsOrderPayment ? 'ตรวจสินค้าและสิทธิ์ของยอดหลัก' : 'คงยอดหลักและสิทธิ์ของผู้ซื้อ'}
        </Fact>
        {row.externalReference ? <Fact label="อ้างอิงที่แจ้งโอนภายนอก">{row.externalReference}</Fact> : null}
        {row.externalPaidAt ? <Fact label="เวลาที่แจ้งโอน">{stamp(row.externalPaidAt)}</Fact> : null}
        {row.approvedAt ? (
          <Fact label="ผู้อนุมัติ / เวลา">
            {row.approvedBy} · {stamp(row.approvedAt)}
          </Fact>
        ) : null}
      </dl>
      <p>{row.reason}</p>
      <h4>หลักฐานส่วนตัวของฝ่ายการเงิน</h4>
      {!row.files.length ? <p className="muted">ยังไม่มีไฟล์หลักฐาน</p> : null}
      {row.files.map((file) => (
        <div className="admin-proof-file" key={file.id}>
          {row.state === 'Review' ? (
            <label className="admin-check">
              <input
                type="checkbox"
                checked={files.includes(file.id)}
                disabled={busy || (!files.includes(file.id) && files.length >= 8)}
                onChange={(e) => {
                  setFiles(e.target.checked ? [...files, file.id] : files.filter((id) => id !== file.id));
                  reset();
                }}
              />
              {file.fileName}
            </label>
          ) : (
            <strong>{file.fileName}</strong>
          )}
          <small>
            {(file.byteLength / 1024).toFixed(1)} KB · {stamp(file.uploadedAt)}
          </small>
          <details>
            <summary>ตรวจรหัส SHA-256</summary>
            <code>{file.sha256}</code>
          </details>
          <button
            className="text-button"
            disabled={busy}
            onClick={() => run(() => admin.downloadEvidence(file))}
          >
            <Download size={17} />
            ดาวน์โหลดหลักฐาน
          </button>
        </div>
      ))}
      {row.state === 'Review' ? (
        <Field label="แนบหลักฐาน PDF / PNG / JPEG / TXT (ไม่เกิน 20 MB)">
          <span className="admin-upload">
            <Upload size={18} />
            <input
              type="file"
              disabled={busy}
              accept=".pdf,.png,.jpg,.jpeg,.txt"
              onChange={(e) => {
                const file = e.target.files?.[0];
                e.target.value = '';
                if (file) run(() => admin.uploadEvidence(file));
              }}
            />
          </span>
        </Field>
      ) : null}
      {!terminal ? (
        <>
          {row.state === 'Review' ? (
            <>
              <Field label="เลขอ้างอิงการโอนภายนอก">
                <input
                  maxLength={200}
                  value={reference}
                  onChange={(e) => {
                    setReference(e.target.value);
                    reset();
                  }}
                />
              </Field>
              <Field label="วันและเวลาโอนภายนอก (เวลาของอุปกรณ์)">
                <input
                  type="datetime-local"
                  step="1"
                  value={paidAt}
                  onChange={(e) => {
                    setPaidAt(e.target.value);
                    reset();
                  }}
                />
              </Field>
            </>
          ) : null}
          <Field label="เหตุผลตรวจหลักฐาน">
            <textarea
              rows={3}
              maxLength={500}
              value={reason}
              onChange={(e) => {
                setReason(e.target.value);
                reset();
              }}
            />
          </Field>
          <label className="admin-check">
            <input type="checkbox" checked={confirm} onChange={(e) => setConfirm(e.target.checked)} />
            ยืนยันตรวจข้อมูลและหลักฐานสำหรับยอด {exactMoney(row.amountMinor)}
          </label>
          {row.state === 'Review' ? (
            <div className="admin-actions">
              <button
                className="button primary"
                disabled={
                  busy ||
                  !confirm ||
                  !reason.trim() ||
                  !reference.trim() ||
                  !paidAt ||
                  !files.length ||
                  !Number.isFinite(new Date(paidAt).getTime())
                }
                onClick={() =>
                  run(() =>
                    admin.manualAction('submit-evidence', reason, {
                      externalReference: reference,
                      externalPaidAt: new Date(paidAt).toISOString(),
                      evidenceIds: files,
                    }),
                  )
                }
              >
                ส่งหลักฐานให้เจ้าหน้าที่อีกคนตรวจ
              </button>
              {!row.submittedAt ? (
                <button
                  className="text-button"
                  disabled={busy || !reason.trim() || !confirm}
                  onClick={() => run(() => admin.manualAction('cancel-before-submission', reason))}
                >
                  ยกเลิกเคสก่อนแจ้งโอน
                </button>
              ) : (
                <p className="muted">เคยแจ้งโอนแล้ว ยอดยังคงถูกกันไว้ระหว่างแก้ไขหลักฐาน</p>
              )}
            </div>
          ) : (
            <>
              {!row.policyApproved ? (
                <p className="message warning">ยังไม่มีนโยบายรับรองการคืนเงินภายนอกที่บริษัทอนุมัติ</p>
              ) : null}
              {!row.canApprove ? (
                <p className="message subtle">
                  ต้องให้เจ้าหน้าที่ฝ่ายการเงินอีกคนตรวจ ผู้เปิดหรือส่งหลักฐานอนุมัติตนเองไม่ได้
                </p>
              ) : null}
              <div className="admin-actions">
                <button
                  className="button primary"
                  disabled={busy || !reason.trim() || !confirm || !row.policyApproved || !row.canApprove}
                  onClick={() => run(() => admin.manualAction('approve-attestation', reason))}
                >
                  อนุมัติหลักฐานการคืนเงินภายนอก
                </button>
                <button
                  className="button secondary"
                  disabled={busy || !reason.trim() || !confirm || !row.canApprove}
                  onClick={() => run(() => admin.manualAction('reject-evidence', reason))}
                >
                  ส่งกลับแก้ไขหลักฐาน
                </button>
              </div>
            </>
          )}
        </>
      ) : null}
    </section>
  );
}
export function GoodsInspector() {
  const { goods, detail } = useAdmin();
  const { busy } = useCommerce();
  const [reason, setReason] = useState(''),
    [units, setUnits] = useState<string[]>([]),
    [quantities, setQuantities] = useState<Record<string, number>>({});
  const [consumed, setConsumed] = useState(false),
    [confirm, setConfirm] = useState(false);
  if (!goods) return null;
  const editable = detail?.case.state === 'Open',
    selected = goods.digitalUnits.filter(
      (u) => !u.alreadyDisposed && (goods.fullRefund || units.includes(`${u.orderLineId}:${u.unitIndex}`)),
    );
  const needsConsumed = selected.some((u) => u.downloadRequested || u.gameUsed);
  const validQuantities = goods.lines.every((l) => {
    const q = quantities[l.id] ?? 0;
    return (
      Number.isInteger(q) &&
      q >= 0 &&
      q <= l.maximumRestockQuantity &&
      (l.kind !== 'Digital' || q <= selected.filter((u) => u.orderLineId === l.id).length)
    );
  });
  return (
    <section className="admin-refund-form" aria-label="ตรวจสินค้าและสิทธิ์หลังคืนเงิน">
      <h3>ตรวจสินค้าและสิทธิ์หลังคืนเงิน</h3>
      <p className="muted">
        {goods.fullRefund
          ? 'คืนเงินครบแล้ว จะยกเลิกสิทธิ์ที่เหลือและการส่งมอบที่ยังไม่จัดส่ง'
          : 'คืนเงินบางส่วน เลือกเฉพาะหน่วยสินค้าที่ได้รับผลกระทบ'}
      </p>
      {!goods.policyApproved ? (
        <p className="message warning">ยังไม่มีนโยบายคืนสินค้าและเพิกถอนสิทธิ์ที่อนุมัติ</p>
      ) : null}
      {goods.lines.map((line) => (
        <div className="admin-product-line admin-return-line" key={line.id}>
          <div>
            <strong>{line.name}</strong>
            <p className="muted">
              ซื้อ {line.purchasedQuantity} · รับคืนเข้าสต็อกแล้ว {line.restockedQuantity}
            </p>
            <small>
              {line.deliveryState
                ? (deliveryLabels[line.deliveryState] ?? line.deliveryState)
                : 'ยังไม่ส่งมอบ'}
              {line.deliveryQuantity !== null ? ` · ${line.deliveryQuantity} หน่วย` : ''}
            </small>
          </div>
          {line.trackStock ? (
            <Field label={`รับคืนเข้าสต็อก: ${line.name}`}>
              <input
                type="number"
                min={0}
                max={line.maximumRestockQuantity}
                step={1}
                disabled={busy || !editable}
                value={quantities[line.id] ?? 0}
                onChange={(e) => {
                  setQuantities({ ...quantities, [line.id]: Number(e.target.value) });
                  setConfirm(false);
                }}
              />
            </Field>
          ) : (
            <small>ไม่ติดตามสต็อก</small>
          )}
        </div>
      ))}
      {goods.digitalUnits.length ? <h4>หน่วยสินค้าดิจิทัล</h4> : null}
      {goods.digitalUnits.map((unit) => (
        <label className="admin-check admin-unit-check" key={`${unit.orderLineId}:${unit.unitIndex}`}>
          <input
            type="checkbox"
            disabled={busy || !editable || unit.alreadyDisposed || goods.fullRefund}
            checked={
              unit.alreadyDisposed ||
              goods.fullRefund ||
              units.includes(`${unit.orderLineId}:${unit.unitIndex}`)
            }
            onChange={(e) => {
              const id = `${unit.orderLineId}:${unit.unitIndex}`;
              setUnits(e.target.checked ? [...units, id] : units.filter((x) => x !== id));
              setConfirm(false);
            }}
          />
          <span>
            {unit.name} · หน่วย {unit.unitIndex + 1}
            <small>
              {unit.alreadyDisposed ? 'จัดการแล้ว' : 'เลือกเพิกถอนสิทธิ์'}
              {unit.downloadRequested ? ' · เคยขอดาวน์โหลด' : ''}
              {unit.gameUsed ? ' · ใช้งานในเกมแล้ว' : ''}
            </small>
          </span>
        </label>
      ))}
      {editable ? (
        <>
          <Field label="เหตุผลจัดการสินค้า">
            <textarea
              rows={3}
              maxLength={500}
              value={reason}
              onChange={(e) => {
                setReason(e.target.value);
                setConfirm(false);
              }}
            />
          </Field>
          {needsConsumed ? (
            <label className="admin-check">
              <input
                type="checkbox"
                checked={consumed}
                onChange={(e) => {
                  setConsumed(e.target.checked);
                  setConfirm(false);
                }}
              />
              ตรวจสินค้าที่ดาวน์โหลดหรือใช้แล้วตามนโยบายบริษัท
            </label>
          ) : null}
          <p className="muted">
            จำนวนรับคืนเข้าสต็อกเริ่มที่ศูนย์ ยืนยันเฉพาะสินค้าที่รับคืนจริง ประวัติการส่งมอบเดิมยังคงอยู่
          </p>
          <label className="admin-check">
            <input type="checkbox" checked={confirm} onChange={(e) => setConfirm(e.target.checked)} />
            ยืนยันจำนวนรับคืนและสิทธิ์ที่เลือก
          </label>
          <button
            className="button primary"
            disabled={
              busy ||
              !reason.trim() ||
              !confirm ||
              !goods.policyApproved ||
              !validQuantities ||
              (needsConsumed && !consumed)
            }
            onClick={() =>
              run(() =>
                admin.goodsDecision({
                  expectedCaseVersion: goods.caseVersion,
                  reason,
                  cancelUndelivered: goods.fullRefund,
                  revokeDigitalUnits: goods.fullRefund
                    ? []
                    : selected.map((u) => ({ orderLineId: u.orderLineId, unitIndex: u.unitIndex })),
                  restock: goods.lines
                    .filter((l) => (quantities[l.id] ?? 0) > 0)
                    .map((l) => ({ orderLineId: l.id, quantity: quantities[l.id] })),
                  consumedGoodsReviewed: consumed,
                }),
              )
            }
          >
            ยืนยันการจัดการสินค้า
          </button>
        </>
      ) : null}
    </section>
  );
}
