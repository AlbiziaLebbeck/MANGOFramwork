import { useState } from 'react';
import { ClipboardCheck, FileText, Search } from 'lucide-react';
import { useCommerce } from '../store';
import { Empty } from '../components/Ui';
import { admin, useAdmin } from './store';
import { caseLabel, exactMoney } from './types';
import { Back, Documents, Fact, Field, Refresh, RowEnd, SearchField, stamp, State, run } from './Ui';
import { ManualStart, ManualCreate, ManualInspector, GoodsInspector } from './Refunds';

export default function Finance() {
  const state = useAdmin();
  const { busy } = useCommerce();
  const [search, setSearch] = useState('');
  const detail = state.detail;
  const items = state.cases.filter((x) =>
    `${x.orderNumber ?? ''} ${x.instructionId ?? ''} ${caseLabel(x.kind)}`
      .toLocaleLowerCase()
      .includes(search.toLocaleLowerCase()),
  );
  return (
    <div className={`admin-columns ${detail ? 'has-detail' : ''}`}>
      <section className="admin-queue">
        <h1>เคสการเงิน</h1>
        {state.caseOrderId ? (
          <div className="admin-filter-context">
            <span>เคสของคำสั่งซื้อที่เลือก</span>
            <button
              className="text-button"
              disabled={busy}
              onClick={() => run(() => admin.selectTab('finance'))}
            >
              ดูเคสทั้งหมด
            </button>
          </div>
        ) : null}
        <div className="admin-counts">
          <State value="Open" label={`รอตรวจสอบ ${state.summary?.openCases ?? '—'}`} />
          <span>
            <FileText size={18} />
            รอไฟล์รายงาน {state.summary?.waitingFiles ?? 'ยังไม่เปิดบริการ'}
          </span>
        </div>
        <div className="admin-toolbar">
          <SearchField value={search} onChange={setSearch} />
          <select
            aria-label="สถานะเคส"
            value={state.caseState}
            disabled={busy}
            onChange={(e) => {
              admin.back();
              admin.patch({ caseState: e.target.value });
              run(() => admin.refresh());
            }}
          >
            <option value="Open">รอตรวจสอบ</option>
            <option value="Resolved">ดำเนินการแล้ว</option>
            <option value="">ทั้งหมด</option>
          </select>
          <Refresh />
        </div>
        <div className="admin-table finance-table">
          <div className="admin-table-head case-row">
            <span>คำสั่งซื้อ</span>
            <span>ประเภทเคส</span>
            <span>ยอดเงิน</span>
            <span>สถานะ</span>
          </div>
          {items.map((item) => (
            <button
              className={`admin-table-row case-row ${detail?.case.id === item.id ? 'selected' : ''}`}
              key={item.id}
              onClick={() => run(() => admin.chooseCase(item.id))}
            >
              <span>{item.orderNumber ?? 'ยังไม่จับคู่'}</span>
              <span>{caseLabel(item.kind)}</span>
              <span>{item.amountMinor === null ? '—' : exactMoney(item.amountMinor)}</span>
              <State value={item.state} label={item.state === 'Open' ? 'รอตรวจสอบ' : 'ดำเนินการแล้ว'} />
              <RowEnd />
            </button>
          ))}
          {state.loaded && !items.length ? <Empty>ไม่พบเคสตามเงื่อนไข</Empty> : null}
        </div>
        {state.caseCursor ? (
          <button className="text-button" disabled={busy} onClick={() => run(() => admin.refresh(true))}>
            ดูเคสเพิ่มเติม
          </button>
        ) : null}
      </section>
      <aside className="admin-inspector">
        {detail ? <CaseInspector key={detail.case.id} /> : <Empty>เลือกเคสเพื่อดูหลักฐานและดำเนินการ</Empty>}
      </aside>
    </div>
  );
}
function CaseInspector() {
  const { detail, review, goods } = useAdmin();
  const { busy } = useCommerce();
  const [reason, setReason] = useState('');
  if (!detail) return null;
  const row = detail.case,
    bank = detail.bank;
  const action = (value: string) =>
    run(async () => {
      await admin.caseAction(value, reason);
      setReason('');
    });
  return (
    <>
      <Back label="คิวเคสการเงิน" />
      <div className="admin-detail-heading">
        <h2>{caseLabel(row.kind)}</h2>
        <State value={row.state} label={row.state === 'Open' ? 'รอตรวจสอบ' : 'ดำเนินการแล้ว'} />
      </div>
      <p className="admin-order-number">คำสั่งซื้อ {row.orderNumber ?? 'ยังไม่จับคู่'}</p>
      <p className="muted">สร้างเคส {stamp(row.createdAt)}</p>
      <div className="admin-case-amount">
        <span>ยอดเงินที่เกี่ยวข้อง</span>
        <strong>{row.amountMinor === null ? '—' : exactMoney(row.amountMinor)}</strong>
      </div>
      {detail.manualRefund ? (
        <ManualInspector
          key={`${detail.manualRefund.id}:${detail.manualRefund.version}`}
          row={detail.manualRefund}
        />
      ) : null}
      {['VoidFulfillmentReview', 'ReportedVoidFulfillmentReview', 'ManualRefundFulfillmentReview'].includes(
        row.kind,
      ) ? (
        <button className="button secondary" disabled={busy} onClick={() => run(() => admin.inspectGoods())}>
          ตรวจสินค้าและสิทธิ์หลังคืนเงิน
        </button>
      ) : null}
      {goods ? <GoodsInspector key={`${goods.caseId}:${goods.caseVersion}`} /> : null}
      <section>
        <h3>หลักฐานรับเงิน</h3>
        {bank ? (
          <dl className="admin-evidence">
            <Fact label="อ้างอิงการรับเงิน">{bank.instructionId}</Fact>
            <Fact label="QR ID">{bank.qrId}</Fact>
            <Fact label="การจัดสรร">
              {bank.primary
                ? 'ยอดหลักของคำสั่งซื้อ'
                : bank.allocatedOrderId
                  ? 'ยอดจัดสรรเพิ่มเติม'
                  : 'ยังไม่จัดสรรยอดนี้'}
            </Fact>
            <Fact label="เวลาเงินเข้าต้นทาง">
              {bank.timeSource === 'ReportIsoPayment' && bank.originalCreditAt
                ? stamp(bank.originalCreditAt)
                : 'รอยืนยันจากรายงาน'}
            </Fact>
            <Fact label="สถานะ">{bank.voided ? 'ธนาคารยืนยันการยกเลิกแล้ว' : 'พบรายการรับเงิน'}</Fact>
          </dl>
        ) : (
          <p className="muted">ยังไม่มีรายการรับเงินที่ยืนยันและจับคู่ได้</p>
        )}
      </section>
      <section>
        <h3>ลำดับเหตุการณ์</h3>
        <ol className="admin-timeline">
          <li>
            <strong>สร้างเคสการเงิน</strong>
            <small>{stamp(row.createdAt)}</small>
          </li>
          {row.assignee ? (
            <li>
              <strong>ผู้รับผิดชอบ</strong>
              <small>{row.assignee}</small>
            </li>
          ) : null}
          {row.resolvedAt ? (
            <li>
              <strong>ดำเนินการแล้ว</strong>
              <small>{stamp(row.resolvedAt)}</small>
            </li>
          ) : null}
          {detail.notes
            .slice()
            .reverse()
            .map((note) => (
              <li key={note.id}>
                <p>{note.body}</p>
                <small>
                  {stamp(note.createdAt)} · {note.actor}
                </small>
              </li>
            ))}
        </ol>
      </section>
      <section>
        <Field label="เหตุผล / บันทึก">
          <textarea
            maxLength={2000}
            rows={3}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="ระบุเหตุผลหรือบันทึกเพิ่มเติม"
          />
        </Field>
        <div className="admin-character-count">{reason.length}/2000</div>
        <div className="admin-actions">
          <button
            className="button secondary"
            disabled={busy || !reason.trim()}
            onClick={() => action('note')}
          >
            <FileText size={19} />
            บันทึกหมายเหตุ
          </button>
          {row.state === 'Open' ? (
            <button className="text-button" disabled={busy || !reason.trim()} onClick={() => action('claim')}>
              <ClipboardCheck size={19} />
              รับผิดชอบเคส
            </button>
          ) : null}
        </div>
      </section>
      <div className="admin-actions">
        <button
          className="button primary"
          disabled={busy || !bank}
          onClick={() => bank && run(() => admin.inspectVoid(bank.id))}
        >
          <Search size={19} />
          ตรวจเงื่อนไขคืนเงิน
        </button>
        <button
          className="button secondary"
          disabled={busy || !row.orderId}
          onClick={() => run(() => admin.orderFromCase())}
        >
          <FileText size={19} />
          ดูคำสั่งซื้อ
        </button>
      </div>
      {review ? <VoidInspector key={review.bankTransactionId} /> : null}
      {bank && row.kind !== 'ManualRefund' ? <ManualStart bank={bank} sourceCaseId={row.id} /> : null}
      <ManualCreate />
      {row.state === 'Open' && ['LateOrClosedOrder', 'StockReview'].includes(row.kind) ? (
        <section className="admin-resolution">
          <h3>รับยอดชำระล่าช้า</h3>
          <p className="muted">
            ตรวจยืนยันความประสงค์ซื้อและยอดอ้างอิง ระบบจะตรวจสต็อกปัจจุบันอีกครั้งก่อนจัดสรรเงิน
          </p>
          <button
            className="button secondary"
            disabled={busy || !reason.trim()}
            onClick={() => action('accept-late-payment')}
          >
            ยืนยันรับยอดและดำเนินการซื้อ
          </button>
        </section>
      ) : null}
      {row.state === 'Open' && bank?.voided && !bank.primary ? (
        <button
          className="button secondary"
          disabled={busy || !reason.trim()}
          onClick={() => action('resolve-voided-credit')}
        >
          ปิดเคสยอดที่คืนเงินแล้ว
        </button>
      ) : null}
      {row.state === 'Open' &&
      bank &&
      !bank.primary &&
      BigInt(bank.manualAttestedMinor ?? '0') === BigInt(bank.amountMinor) ? (
        <button
          className="button secondary"
          disabled={busy || !reason.trim()}
          onClick={() => action('resolve-manual-refunded-credit')}
        >
          ปิดเคสยอดที่คืนภายนอกครบแล้ว
        </button>
      ) : null}
      {row.state === 'Open' && ['InvalidNotice', 'NotificationPayloadChanged'].includes(row.kind) ? (
        <button
          className="button secondary"
          disabled={busy || !reason.trim()}
          onClick={() => action('dismiss-metadata')}
        >
          ปิดเคสข้อมูลแจ้งเตือน
        </button>
      ) : null}
      {row.state === 'Resolved' && row.orderId ? (
        <button
          className="button secondary"
          disabled={busy || !reason.trim()}
          onClick={() => action('resume-paid-order')}
        >
          ตรวจและปลดพักคำสั่งซื้อ
        </button>
      ) : null}
    </>
  );
}
const voidMessages: Record<string, string> = {
  eligible: 'รายการอยู่ในช่วงเวลาที่ขอยกเลิกได้',
  void_unavailable: 'ยังไม่เปิดบริการคืนเงินผ่านธนาคาร',
  void_window_closed: 'พ้นช่วงเวลายกเลิกของธนาคาร ต้องใช้กระบวนการคืนเงินภายนอก',
  void_original_time_unproven: 'ยังไม่มีหลักฐานเวลารับเงินต้นทางที่เพียงพอ',
  void_cross_border_unavailable: 'API ไม่รองรับยกเลิกเงินข้ามพรมแดน',
  void_instruction_ineligible: 'Instruction นี้ไม่รองรับการยกเลิก',
  already_voided: 'ธนาคารยืนยันยกเลิกแล้ว',
  void_already_requested: 'มีคำขอยกเลิกรายการนี้แล้ว โปรดตรวจผลเดิม',
  void_payment_ineligible: 'สถานะหรือยอดชำระไม่เข้าเงื่อนไข',
};
export function VoidInspector() {
  const { review } = useAdmin();
  const { busy } = useCommerce();
  const [reason, setReason] = useState('');
  const [ack, setAck] = useState(false);
  const [confirm, setConfirm] = useState(false);
  if (!review) return null;
  return (
    <section className="admin-void-review">
      <h3>ตรวจเงื่อนไขคืนเงิน</h3>
      <dl>
        <Fact label="ยอดคืนเต็มจำนวน">{exactMoney(review.amountMinor)}</Fact>
        <Fact label="ผลต่อสินค้า">
          {review.affectsOrderPayment
            ? 'ยกเลิกยอดหลักและเพิกถอนสิทธิ์ของคำสั่งซื้อ'
            : 'คืนเฉพาะยอดนี้ · คงยอดหลักและสิทธิ์สินค้า'}
        </Fact>
        <Fact label="เงื่อนไข">
          {voidMessages[review.eligibility.code] ?? 'ต้องให้ฝ่ายการเงินตรวจสอบก่อน'}
        </Fact>
        {review.eligibility.windowEndsAt ? (
          <Fact label="สิ้นสุดช่วงเวลา">{stamp(review.eligibility.windowEndsAt)}</Fact>
        ) : null}
      </dl>
      {review.eligibility.eligible ? (
        <>
          <Field label="เหตุผลการคืนเงิน">
            <textarea
              maxLength={500}
              value={reason}
              rows={3}
              onChange={(e) => {
                setReason(e.target.value);
                setConfirm(false);
              }}
            />
          </Field>
          {review.deliveryReviewRequired ? (
            <label className="admin-check">
              <input
                type="checkbox"
                checked={ack}
                onChange={(e) => {
                  setAck(e.target.checked);
                  setConfirm(false);
                }}
              />
              ตรวจการส่งมอบ / ดาวน์โหลด / ใช้งาน และนโยบายคืนสินค้าที่อนุมัติแล้ว
            </label>
          ) : null}
          {review.deliveryReviewRequired && !review.fulfilledPolicyApproved ? (
            <p className="message warning">ยังไม่มีนโยบายคืนเงินหลังส่งมอบที่อนุมัติ</p>
          ) : null}
          <label className="admin-check">
            <input type="checkbox" checked={confirm} onChange={(e) => setConfirm(e.target.checked)} />
            ยืนยันคืนยอดนี้เต็มจำนวน {exactMoney(review.amountMinor)}
          </label>
          <button
            className="button primary"
            disabled={
              busy ||
              !reason.trim() ||
              !confirm ||
              (review.deliveryReviewRequired && (!ack || !review.fulfilledPolicyApproved))
            }
            onClick={() =>
              run(async () => {
                await admin.sendVoid(reason, ack);
                setConfirm(false);
              })
            }
          >
            ยืนยันคืนเงินเต็มจำนวน
          </button>
        </>
      ) : null}
      {review.operations.map((operation) => (
        <div className="admin-void-operation" key={operation.id}>
          <strong>{operation.transactionId}</strong>
          <State
            value={operation.state}
            label={
              operation.state === 'Confirmed'
                ? 'ธนาคารยืนยันยกเลิก'
                : operation.state === 'Unknown'
                  ? 'ยังไม่ทราบผล'
                  : operation.state === 'Requested'
                    ? 'รอส่งคำขอ'
                    : 'ธนาคารปฏิเสธ / ไม่ได้ส่ง'
            }
          />
          {operation.resultCode ? <small>รหัสผล {operation.resultCode}</small> : null}
          {operation.state === 'Unknown' ? (
            <button
              className="button secondary"
              disabled={busy || operation.enquiriesRemaining < 1}
              onClick={() => run(() => admin.enquireVoid(operation))}
            >
              ตรวจผลยกเลิก · เหลือ {operation.enquiriesRemaining} ครั้ง
            </button>
          ) : null}
        </div>
      ))}
      <Documents items={review.documents} />
      <button
        className="text-button"
        disabled={busy}
        onClick={() => run(() => admin.inspectVoid(review.bankTransactionId))}
      >
        อัปเดตผลจากหลังบ้าน
      </button>
    </section>
  );
}
