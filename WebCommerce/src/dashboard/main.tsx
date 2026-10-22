import { useCallback, useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  Bell,
  Check,
  CheckCircle2,
  ChevronRight,
  ClipboardList,
  Copy,
  Download,
  FileJson,
  FileText,
  Home,
  LogIn,
  LogOut,
  Menu,
  RefreshCw,
  Search,
  ShieldCheck,
  Terminal,
  X,
} from 'lucide-react';
import { DashboardError, date, good, label, money, request } from './api';
import type {
  DashboardSession,
  Notice,
  NoticePage,
  OrderDetail,
  Overview,
  TestCase,
  TestPage,
} from './types';
import './styles.css';

type Tab = 'overview' | 'payments' | 'notifications' | 'tests';
const navigation = [
  { id: 'overview', name: 'ภาพรวม', icon: Home },
  { id: 'payments', name: 'รายการชำระเงิน', icon: ClipboardList },
  { id: 'notifications', name: 'Notification', icon: Bell },
  { id: 'tests', name: 'รายการ TEST', icon: FileText },
] as const;
const titles: Record<Tab, string> = {
  overview: 'ภาพรวมการรับชำระเงิน',
  payments: 'รายการชำระเงิน',
  notifications: 'Notification จาก TTB',
  tests: 'ประวัติการทดสอบธนาคาร',
};
const status = (value: string) => (
  <span
    className={`status ${good(value) ? 'success' : ['Review', 'Rejected', 'VoidUnknown', 'Unknown'].includes(value) ? 'danger' : 'pending'}`}
  >
    <span />
    {label(value)}
  </span>
);
const empty = (text: string) => (
  <div className="empty">
    <FileText size={28} />
    <p>{text}</p>
  </div>
);

function JsonDialog({ value, close }: { value: unknown; close: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [copied, setCopied] = useState(false);
  const text = JSON.stringify(value, null, 2);
  useEffect(() => {
    dialog.current?.showModal();
    return () => dialog.current?.close();
  }, []);
  const download = () => {
    const url = URL.createObjectURL(new Blob([text], { type: 'application/json' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = `ttb-uat-log-${new Date().toISOString().replace(/[:.]/g, '-')}.json`;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  };
  return (
    <dialog ref={dialog} className="json-dialog" onCancel={close} aria-labelledby="json-title">
      <header>
        <div>
          <h2 id="json-title">รายละเอียด JSON</h2>
          <p>ข้อมูลระบบสำหรับตรวจสอบและส่งต่อ</p>
        </div>
        <button className="icon-button" aria-label="ปิดรายละเอียด" onClick={close}>
          <X />
        </button>
      </header>
      <pre tabIndex={0}>{text}</pre>
      <footer>
        <button
          className="button"
          onClick={() =>
            void navigator.clipboard
              .writeText(text)
              .then(() => setCopied(true))
              .catch(() => setCopied(false))
          }
        >
          {copied ? <Check size={17} /> : <Copy size={17} />}
          {copied ? 'คัดลอกแล้ว' : 'คัดลอก JSON'}
        </button>
        <button className="button primary" onClick={download}>
          <Download size={17} />
          บันทึก JSON
        </button>
      </footer>
    </dialog>
  );
}

function OrderPanel({
  detail,
  loading,
  json,
}: {
  detail: OrderDetail | null;
  loading: boolean;
  json: (value: unknown) => void;
}) {
  if (!detail)
    return (
      <aside className="panel detail-panel">
        <h2>รายละเอียดการชำระเงิน</h2>
        {empty(loading ? 'กำลังอ่านรายละเอียด…' : 'เลือกรายการเพื่อดูเส้นทางการชำระเงิน')}
      </aside>
    );
  const callback = detail.evidence.find((e) => e.source === 'Callback');
  const ack = callback ? detail.callbacks.find((c) => c.observationId === callback.id) : undefined;
  const qr = detail.payments[0]?.qrId ?? detail.attempts[0]?.qrId;
  const delivered =
    detail.fulfillment.length > 0 && detail.fulfillment.every((f) => f.state.toLowerCase() === 'delivered');
  const deliveryTime = delivered
    ? detail.fulfillment
        .map((f) => f.deliveredAt)
        .filter((v): v is string => !!v)
        .sort()
        .at(-1)
    : null;
  const paid = detail.order.paymentState.toLowerCase() === 'paid';
  const steps = [
    { name: 'ได้รับ Notification', done: !!callback, time: callback?.createdAt },
    { name: 'ยืนยันการชำระ', done: paid, time: detail.order.confirmedAt },
    { name: 'ส่งสินค้าสำเร็จ', done: delivered, time: deliveryTime },
  ];
  return (
    <aside className={`panel detail-panel ${loading ? 'updating' : ''}`} aria-busy={loading}>
      <h2>รายละเอียดการชำระเงิน</h2>
      <span className="field-label">เลขที่คำสั่งซื้อ</span>
      <h3 className="order-number">{detail.order.number}</h3>
      <span className="field-label">ยอดชำระ</span>
      <strong className="amount">{money(detail.order.amountMinor)}</strong>
      <div className="process">
        <span className="field-label">สถานะการดำเนินการ</span>
        <ol>
          {steps.map((step) => (
            <li key={step.name} className={step.done ? 'done' : ''}>
              <span className="step-dot">{step.done ? <Check size={16} /> : <span />}</span>
              <span>{step.name}</span>
              <small>{step.time ? date(step.time, true) : step.done ? 'สำเร็จ' : 'รอข้อมูล'}</small>
            </li>
          ))}
        </ol>
      </div>
      <dl className="facts">
        <dt>ACK</dt>
        <dd>{ack?.ackCode ?? '—'}</dd>
        <dt>ต้นทาง</dt>
        <dd>{callback?.peerIp ?? '—'}</dd>
        <dt>QRId</dt>
        <dd className="identifier">{qr ?? '—'}</dd>
      </dl>
      <details className="extra-facts">
        <summary>ข้อมูลเพิ่มเติม</summary>
        <dl className="facts">
          <dt>สถานะออเดอร์</dt>
          <dd>{label(detail.order.state)}</dd>
          <dt>InstructionId</dt>
          <dd className="identifier">{detail.payments[0]?.instructionId ?? ack?.instructionId ?? '—'}</dd>
          <dt>ใบเสร็จ</dt>
          <dd>{detail.documents.map((d) => `${d.number} · ${label(d.state)}`).join(', ') || '—'}</dd>
        </dl>
      </details>
      <button className="button gold full" onClick={() => json(detail)}>
        <FileJson size={18} />
        ดูรายละเอียด JSON
      </button>
    </aside>
  );
}

function NoticeTable({
  rows,
  select,
  selected,
}: {
  rows: Notice[];
  select: (row: Notice) => void;
  selected?: string;
}) {
  return rows.length ? (
    <div className="table-scroll">
      <table className="notices-table">
        <thead>
          <tr>
            <th>เวลา (ไทย)</th>
            <th>คำสั่งซื้อ</th>
            <th>ผลตอบรับ</th>
            <th>สถานะ</th>
            <th>
              <span className="sr-only">รายละเอียด</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.id} className={selected === row.id ? 'selected' : ''}>
              <td>
                <button className="row-link" onClick={() => select(row)}>
                  {date(row.createdAt)}
                </button>
              </td>
              <td>{row.orderNumber ?? 'ยังไม่จับคู่ออเดอร์'}</td>
              <td>{row.ackCode ? `ACK ${row.ackCode}` : 'ดูรายละเอียด'}</td>
              <td>{status(row.outcome)}</td>
              <td>
                <button
                  className="icon-button"
                  aria-label={`ดู Notification ${row.requestUid}`}
                  onClick={() => select(row)}
                >
                  <ChevronRight size={18} />
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  ) : (
    empty('ยังไม่มี Notification ที่บันทึกในระบบ')
  );
}

function App() {
  const [session, setSession] = useState<DashboardSession | null>(null);
  const [authLoading, setAuthLoading] = useState(true);
  const [tab, setTab] = useState<Tab>('overview');
  const [menu, setMenu] = useState(false);
  const [overview, setOverview] = useState<Overview | null>(null);
  const [notices, setNotices] = useState<NoticePage | null>(null);
  const [tests, setTests] = useState<TestPage | null>(null);
  const [detail, setDetail] = useState<OrderDetail | null>(null);
  const [selectedOrder, setSelectedOrder] = useState<string | null>(null);
  const [selectedNotice, setSelectedNotice] = useState<Notice | null>(null);
  const [selectedTest, setSelectedTest] = useState<TestCase | null>(null);
  const [search, setSearch] = useState('');
  const [term, setTerm] = useState('');
  const [state, setState] = useState('all');
  const [cursor, setCursor] = useState<string | null>(null);
  const [pages, setPages] = useState<(string | null)[]>([]);
  const [auto, setAuto] = useState(true);
  const [refresh, setRefresh] = useState(0);
  const [loading, setLoading] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [error, setError] = useState('');
  const [json, setJson] = useState<unknown | null>(null);
  const [lastUpdated, setLastUpdated] = useState<string | null>(null);

  const handleError = useCallback((e: unknown) => {
    if (e instanceof DOMException && e.name === 'AbortError') return;
    if (e instanceof DashboardError && [401, 403].includes(e.status)) {
      setOverview(null);
      setNotices(null);
      setTests(null);
      setDetail(null);
      setSelectedNotice(null);
      setSelectedTest(null);
      setJson(null);
      setSession({ authenticated: e.status === 403, canRead: false });
      setError(
        e.status === 403
          ? 'บัญชีนี้ยังไม่มีสิทธิ์ดู Dashboard กรุณาติดต่อผู้ดูแลระบบ'
          : 'เซสชันหมดอายุ กรุณาเข้าสู่ระบบอีกครั้ง',
      );
    } else
      setError(
        e instanceof DashboardError && e.status === 429
          ? 'เรียกข้อมูลถี่เกินไป กรุณารอสักครู่แล้วรีเฟรช'
          : 'อ่านข้อมูลจากเซิร์ฟเวอร์ไม่สำเร็จ กรุณารีเฟรชอีกครั้ง',
      );
  }, []);

  const connect = useCallback(
    async (signal?: AbortSignal) => {
      setAuthLoading(true);
      setError('');
      try {
        let current = await request<DashboardSession>('/session', signal);
        if (!current.authenticated) current = await request<DashboardSession>('/session', signal, 'POST');
        setSession(current);
      } catch (e) {
        handleError(e);
      } finally {
        if (!signal?.aborted) setAuthLoading(false);
      }
    },
    [handleError],
  );
  useEffect(() => {
    const controller = new AbortController();
    void connect(controller.signal);
    return () => controller.abort();
  }, [connect]);
  useEffect(() => {
    const timer = setTimeout(() => {
      setTerm(search.trim());
      setCursor(null);
      setPages([]);
    }, 300);
    return () => clearTimeout(timer);
  }, [search]);
  useEffect(() => {
    if (!auto || !session?.canRead) return;
    const timer = setInterval(() => {
      if (document.visibilityState === 'visible') setRefresh((x) => x + 1);
    }, 30000);
    return () => clearInterval(timer);
  }, [auto, session?.canRead]);

  useEffect(() => {
    if (!session?.canRead) return;
    const controller = new AbortController();
    setLoading(true);
    setError('');
    const query = new URLSearchParams({ search: term, state });
    if (cursor) query.set('before', cursor);
    const noticeQuery = new URLSearchParams({ search: term });
    if (tab === 'notifications' && cursor) noticeQuery.set('before', cursor);
    const testQuery = new URLSearchParams();
    if (cursor) testQuery.set('before', cursor);
    if (selectedTest) testQuery.set('caseId', selectedTest.id);
    const read = async () => {
      const [summary, notifications, bankTests] = await Promise.all([
        request<Overview>(`/overview?${query}`, controller.signal),
        request<NoticePage>(`/notifications?${noticeQuery}`, controller.signal),
        tab === 'tests' ? request<TestPage>(`/tests?${testQuery}`, controller.signal) : Promise.resolve(null),
      ]);
      if (controller.signal.aborted) return;
      setOverview(summary);
      setNotices(notifications);
      if (bankTests) setTests(bankTests);
      setLastUpdated(summary.fetchedAt);
      if (tab === 'overview' || tab === 'payments')
        setSelectedOrder((current) =>
          summary.orders.some((o) => o.id === current) ? current : (summary.orders[0]?.id ?? null),
        );
    };
    void read()
      .catch((e) => {
        if (!controller.signal.aborted) handleError(e);
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [session?.canRead, tab, term, state, cursor, refresh, selectedTest?.id, handleError]);

  useEffect(() => {
    if (!selectedOrder || !session?.canRead) {
      setDetail(null);
      return;
    }
    const controller = new AbortController();
    setDetailLoading(true);
    setDetail(null);
    void request<OrderDetail>(`/orders/${selectedOrder}`, controller.signal)
      .then((value) => {
        if (!controller.signal.aborted) setDetail(value);
      })
      .catch((e) => {
        if (!controller.signal.aborted) handleError(e);
      })
      .finally(() => {
        if (!controller.signal.aborted) setDetailLoading(false);
      });
    return () => controller.abort();
  }, [selectedOrder, refresh, session?.canRead, handleError]);

  const navigate = (next: Tab) => {
    setTab(next);
    setMenu(false);
    setSearch('');
    setTerm('');
    setState('all');
    setCursor(null);
    setPages([]);
    setSelectedNotice(null);
    setSelectedTest(null);
  };
  const selectNotice = (row: Notice) => {
    setSelectedNotice(row);
    if (row.orderId) setSelectedOrder(row.orderId);
  };
  const signOut = async () => {
    try {
      await request('/logout', undefined, 'POST', session?.csrfToken);
      setSession({ authenticated: false });
      setOverview(null);
      setNotices(null);
      setDetail(null);
      setTests(null);
      setJson(null);
      setSelectedOrder(null);
      setSelectedTest(null);
      setSelectedNotice(null);
      setLastUpdated(null);
    } catch (e) {
      handleError(e);
    }
  };
  const nextPage = () => {
    const next = tab === 'notifications' ? notices?.nextCursor : overview?.nextCursor;
    if (next) {
      setPages((p) => [...p, cursor]);
      setCursor(next);
    }
  };

  if (!session?.canRead)
    return (
      <div className="auth-page">
        <div className="auth-brand">
          MANGOs <span>×</span> ttb
        </div>
        <section className="auth-panel">
          <ShieldCheck size={38} className="auth-icon" />
          <h1>Payment Dashboard</h1>
          <p>ตรวจสอบผลจากธนาคารและสถานะคำสั่งซื้อ</p>
          <div className="auth-environment">
            <span className="environment-dot" />
            สภาพแวดล้อมทดสอบ UAT
          </div>
          {error ? (
            <div role="alert" className="error">
              {error}
            </div>
          ) : null}
          {session?.authenticated ? (
            <>
              <p>บัญชี {session.displayName || 'MANGOs'} ยังไม่มีสิทธิ์ดูข้อมูลการชำระเงิน</p>
              <button className="button full" onClick={() => void signOut()}>
                <LogOut size={18} />
                ออกจาก Dashboard
              </button>
            </>
          ) : (
            <>
              <a
                className={`button primary full ${authLoading ? 'disabled' : ''}`}
                href={`https://mangosgo.com/login?redirect=${encodeURIComponent(window.location.origin + '/mangoxttb/dashboard/')}`}
                aria-disabled={authLoading}
                onClick={(e) => {
                  if (authLoading) e.preventDefault();
                }}
              >
                <LogIn size={18} />
                {authLoading ? 'กำลังตรวจสอบบัญชี…' : 'เข้าสู่ระบบด้วย MANGOs'}
              </a>
              <button className="button full" disabled={authLoading} onClick={() => void connect()}>
                <RefreshCw size={17} />
                เชื่อมต่อบัญชีที่เข้าสู่ระบบแล้ว
              </button>
            </>
          )}
          <small>เฉพาะบัญชีผู้ดูแลที่ได้รับสิทธิ์เท่านั้น</small>
        </section>
        <footer>บริษัท สร้างสรรค์ปัญญา จำกัด</footer>
      </div>
    );

  const selectedRow = selectedNotice;
  const testEvents = tests?.events.filter((e) => !selectedTest || e.caseId === selectedTest.id) ?? [];
  return (
    <div className="dashboard">
      <button className="mobile-menu icon-button" aria-label="เปิดเมนู" onClick={() => setMenu((v) => !v)}>
        <Menu />
      </button>
      {menu ? <button className="menu-scrim" aria-label="ปิดเมนู" onClick={() => setMenu(false)} /> : null}
      <aside className={`sidebar ${menu ? 'open' : ''}`}>
        <div className="brand">
          MANGOs <span>×</span> ttb
        </div>
        <p className="brand-subtitle">TTB Payment</p>
        <nav aria-label="เมนู Dashboard">
          {navigation.map((item) => (
            <button
              key={item.id}
              className={tab === item.id ? 'active' : ''}
              onClick={() => navigate(item.id)}
              aria-current={tab === item.id ? 'page' : undefined}
            >
              <item.icon size={23} />
              {item.name}
            </button>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <div className="environment">
            <span className="environment-dot" />
            <div>
              <strong>UAT</strong>
              <small>สภาพแวดล้อมทดสอบ</small>
            </div>
          </div>
          <span className="user-name">{session.displayName}</span>
          <button className="logout" onClick={() => void signOut()}>
            <LogOut size={22} />
            ออกจากระบบ
          </button>
        </div>
      </aside>
      <main className="main">
        <header className="page-header">
          <div>
            <h1>{titles[tab]}</h1>
            <p>
              {tab === 'tests'
                ? 'รายการ Ref1=TEST แยกจากคำสั่งซื้อสินค้า'
                : 'ตรวจสอบผลจากธนาคารและสถานะคำสั่งซื้อ'}
            </p>
          </div>
          <div className="header-actions">
            <button className="button" disabled={loading} onClick={() => setRefresh((x) => x + 1)}>
              <RefreshCw size={18} className={loading ? 'spin' : ''} />
              รีเฟรช
            </button>
            <label className="auto-refresh">
              Auto refresh (30 วินาที)
              <input type="checkbox" checked={auto} onChange={(e) => setAuto(e.target.checked)} />
              <span className="switch" />
            </label>
          </div>
        </header>
        {error ? (
          <div role="alert" className="error">
            {error}
          </div>
        ) : null}
        <section className="summary" aria-label="สรุปการรับชำระเงิน">
          <div>
            <FileText />
            <span>
              คำสั่งซื้อ<strong>{overview?.summary.orders ?? '—'}</strong>
            </span>
          </div>
          <div>
            <CheckCircle2 />
            <span>
              ชำระสำเร็จ<strong>{overview?.summary.paid ?? '—'}</strong>
            </span>
          </div>
          <div>
            <Bell />
            <span>
              รับ Notification<strong>{overview?.summary.notifications ?? '—'}</strong>
            </span>
          </div>
        </section>
        {tab === 'overview' || tab === 'payments' ? (
          <>
            <div className="work-area">
              <section className="panel orders-panel" aria-busy={loading}>
                <h2>{tab === 'overview' ? 'รายการล่าสุด' : 'คำสั่งซื้อทั้งหมด'}</h2>
                <div className="table-toolbar">
                  <label className="search">
                    <Search size={19} />
                    <input
                      aria-label="ค้นหารายการชำระเงิน"
                      placeholder="ค้นหาเลขออเดอร์ / QRId / InstructionId"
                      value={search}
                      onChange={(e) => setSearch(e.target.value)}
                      maxLength={100}
                    />
                  </label>
                  <div className="segments" aria-label="สถานะการชำระเงิน">
                    {[
                      { id: 'all', name: 'ทั้งหมด' },
                      { id: 'paid', name: 'ชำระแล้ว' },
                      { id: 'unpaid', name: 'รอชำระ' },
                    ].map((item) => (
                      <button
                        key={item.id}
                        aria-pressed={state === item.id}
                        className={state === item.id ? 'active' : ''}
                        onClick={() => {
                          setState(item.id);
                          setCursor(null);
                          setPages([]);
                        }}
                      >
                        {item.name}
                      </button>
                    ))}
                  </div>
                </div>
                {overview?.orders.length ? (
                  <div className="table-scroll">
                    <table className="orders-table">
                      <thead>
                        <tr>
                          <th>คำสั่งซื้อ</th>
                          <th>ยอดชำระ</th>
                          <th>สถานะ</th>
                          <th>สร้างเมื่อ (ไทย)</th>
                          <th>
                            <span className="sr-only">รายละเอียด</span>
                          </th>
                        </tr>
                      </thead>
                      <tbody>
                        {overview.orders.map((row) => (
                          <tr key={row.id} className={selectedOrder === row.id ? 'selected' : ''}>
                            <td>
                              <button className="row-link" onClick={() => setSelectedOrder(row.id)}>
                                {row.number}
                              </button>
                            </td>
                            <td>{money(row.amountMinor)}</td>
                            <td>{status(row.paymentState)}</td>
                            <td>{date(row.createdAt)}</td>
                            <td>
                              <button
                                className="icon-button"
                                aria-label={`ดูออเดอร์ ${row.number}`}
                                onClick={() => setSelectedOrder(row.id)}
                              >
                                <ChevronRight size={18} />
                              </button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ) : (
                  empty(loading ? 'กำลังอ่านข้อมูล…' : 'ไม่พบคำสั่งซื้อที่ตรงกับตัวกรอง')
                )}
                <Pagination
                  previous={pages.length > 0}
                  next={!!overview?.nextCursor}
                  disabled={loading}
                  back={() =>
                    setPages((p) => {
                      setCursor(p.at(-1) ?? null);
                      return p.slice(0, -1);
                    })
                  }
                  nextPage={nextPage}
                />
              </section>
              <OrderPanel detail={detail} loading={detailLoading} json={setJson} />
            </div>
            {tab === 'overview' ? (
              <section className="panel recent-notices">
                <div className="section-title">
                  <h2>Notification ล่าสุด</h2>
                  <button className="text-button" onClick={() => navigate('notifications')}>
                    ดูทั้งหมด
                    <ChevronRight size={16} />
                  </button>
                </div>
                <NoticeTable
                  rows={notices?.items.slice(0, 5) ?? []}
                  select={(row) => {
                    navigate('notifications');
                    selectNotice(row);
                  }}
                />
              </section>
            ) : null}
          </>
        ) : null}
        {tab === 'notifications' ? (
          <div className="work-area">
            <section className="panel orders-panel">
              <h2>ประวัติ Notification</h2>
              <label className="search standalone">
                <Search size={19} />
                <input
                  aria-label="ค้นหา Notification"
                  placeholder="เลขออเดอร์ / QRId / InstructionId / Request UID"
                  maxLength={100}
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                />
              </label>
              <NoticeTable rows={notices?.items ?? []} select={selectNotice} selected={selectedRow?.id} />
              <Pagination
                previous={pages.length > 0}
                next={!!notices?.nextCursor}
                disabled={loading}
                back={() =>
                  setPages((p) => {
                    setCursor(p.at(-1) ?? null);
                    return p.slice(0, -1);
                  })
                }
                nextPage={nextPage}
              />
            </section>
            <aside className="panel detail-panel">
              <h2>รายละเอียด Notification</h2>
              {selectedRow ? (
                <>
                  <p className="notification-result">{status(selectedRow.outcome)}</p>
                  <dl className="facts">
                    <dt>รับเมื่อ</dt>
                    <dd>{date(selectedRow.createdAt)}</dd>
                    <dt>ออเดอร์</dt>
                    <dd>{selectedRow.orderNumber ?? 'ยังไม่จับคู่'}</dd>
                    <dt>ACK</dt>
                    <dd>{selectedRow.ackCode ?? '—'}</dd>
                    <dt>จำนวนครั้งที่ส่ง</dt>
                    <dd>{selectedRow.deliveryCount ?? '—'}</dd>
                    <dt>ต้นทาง</dt>
                    <dd>{selectedRow.peerIp ?? '—'}</dd>
                    <dt>QRId</dt>
                    <dd className="identifier">{selectedRow.qrId ?? '—'}</dd>
                    <dt>InstructionId</dt>
                    <dd className="identifier">{selectedRow.instructionId ?? '—'}</dd>
                    <dt>Request UID</dt>
                    <dd className="identifier">{selectedRow.requestUid}</dd>
                  </dl>
                  <button className="button gold full" onClick={() => setJson(selectedRow)}>
                    <FileJson size={18} />
                    ดูรายละเอียด JSON
                  </button>
                  {selectedRow.orderId ? (
                    <button
                      className="button full"
                      onClick={() => {
                        setTab('payments');
                        setSelectedOrder(selectedRow.orderId);
                        setSearch(selectedRow.orderNumber ?? '');
                        setCursor(null);
                        setPages([]);
                      }}
                    >
                      ดูคำสั่งซื้อ
                      <ChevronRight size={17} />
                    </button>
                  ) : null}
                </>
              ) : (
                empty('เลือก Notification เพื่อดูผลตอบรับและต้นทาง')
              )}
            </aside>
          </div>
        ) : null}
        {tab === 'tests' ? (
          <>
            <div className="work-area">
              <section className="panel orders-panel">
                <h2>รายการ Ref1=TEST</h2>
                <p className="muted section-description">
                  {overview?.summary.tests ?? 0} รายการในระบบ · แสดง 50 รายการล่าสุด
                </p>
                {tests?.cases.length ? (
                  <div className="table-scroll">
                    <table>
                      <thead>
                        <tr>
                          <th>QRId / Ref2</th>
                          <th>ยอดชำระ</th>
                          <th>สถานะ</th>
                          <th>
                            <span className="sr-only">รายละเอียด</span>
                          </th>
                        </tr>
                      </thead>
                      <tbody>
                        {tests.cases.map((row) => (
                          <tr key={row.id} className={selectedTest?.id === row.id ? 'selected' : ''}>
                            <td>
                              <button className="row-link" onClick={() => setSelectedTest(row)}>
                                {row.qrId}
                                <small>{row.ref2}</small>
                              </button>
                            </td>
                            <td>{money(row.amountMinor)}</td>
                            <td>
                              {status(
                                row.state === 'AwaitingPayment' &&
                                  row.expiresAt &&
                                  new Date(row.expiresAt).getTime() < Date.now()
                                  ? 'Expired'
                                  : row.state,
                              )}
                            </td>
                            <td>
                              <button
                                className="icon-button"
                                aria-label={`ดู TEST ${row.qrId}`}
                                onClick={() => setSelectedTest(row)}
                              >
                                <ChevronRight size={18} />
                              </button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ) : (
                  empty('ยังไม่มีรายการทดสอบ')
                )}
              </section>
              <aside className="panel detail-panel">
                <h2>รายละเอียด TEST</h2>
                {selectedTest ? (
                  <>
                    <h3 className="test-reference">Ref1 = {selectedTest.ref1}</h3>
                    <strong className="amount">{money(selectedTest.amountMinor)}</strong>
                    <dl className="facts">
                      <dt>QRId</dt>
                      <dd className="identifier">{selectedTest.qrId}</dd>
                      <dt>Ref2</dt>
                      <dd className="identifier">{selectedTest.ref2}</dd>
                      <dt>สร้างเมื่อ</dt>
                      <dd>{date(selectedTest.createdAt)}</dd>
                      <dt>หมดอายุ</dt>
                      <dd>{date(selectedTest.expiresAt)}</dd>
                      <dt>ต้นกำเนิด</dt>
                      <dd>{selectedTest.origin}</dd>
                    </dl>
                    <button
                      className="button gold full"
                      onClick={() => setJson({ testCase: selectedTest, events: testEvents })}
                    >
                      <FileJson size={18} />
                      ดูรายละเอียด JSON
                    </button>
                    <button className="text-button full" onClick={() => setSelectedTest(null)}>
                      ดูเหตุการณ์ทุก TEST
                    </button>
                  </>
                ) : (
                  empty('เลือกรายการ TEST เพื่อกรองประวัติเหตุการณ์')
                )}
                <div className="operator-note">
                  <Terminal size={18} />
                  <p>สร้าง QR, Verify และ Void ผ่านคอนโซลทดสอบส่วนตัวที่เปิดด้วย SSH</p>
                  <button
                    className="text-button"
                    onClick={() =>
                      setJson({
                        command: '.\\Infrastructure\\TTB\\uat\\open-bank-test-console.ps1',
                        url: 'http://127.0.0.1:18765/internal/bank-test-console/',
                        note: 'รันใน PowerShell จาก Unity project แล้วเปิด URL บนเครื่องของคุณ',
                      })
                    }
                  >
                    ดูวิธีเปิดคอนโซล
                    <ChevronRight size={16} />
                  </button>
                </div>
              </aside>
            </div>
            <section className="panel recent-notices">
              <div className="section-title">
                <h2>เหตุการณ์ TEST</h2>
                <button className="button" onClick={() => setJson(testEvents)}>
                  <Download size={16} />
                  ส่งออก JSON
                </button>
              </div>
              {testEvents.length ? (
                <div className="table-scroll">
                  <table>
                    <thead>
                      <tr>
                        <th>เวลา (ไทย)</th>
                        <th>เหตุการณ์</th>
                        <th>QRId</th>
                        <th>HTTP / Bank code</th>
                        <th>ผลลัพธ์</th>
                        <th />
                      </tr>
                    </thead>
                    <tbody>
                      {testEvents.map((e) => (
                        <tr key={e.id}>
                          <td>{date(e.createdAt)}</td>
                          <td>{e.kind}</td>
                          <td className="identifier">{e.qrId}</td>
                          <td>
                            {e.httpStatus ?? '—'} / {e.bankCode ?? '—'}
                          </td>
                          <td>{status(e.outcome)}</td>
                          <td>
                            <button
                              className="icon-button"
                              aria-label={`ดูเหตุการณ์ ${e.id}`}
                              onClick={() => setJson(e)}
                            >
                              <FileJson size={18} />
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : (
                empty('ยังไม่มีเหตุการณ์ในรายการนี้')
              )}
              {(tests?.events.length ?? 0) === 50 ? (
                <button className="button" onClick={() => setCursor(tests?.events.at(-1)?.id ?? null)}>
                  ดูเหตุการณ์ที่เก่ากว่า
                </button>
              ) : null}
            </section>
          </>
        ) : null}
        <footer className="page-footer">
          <span>เวลาไทย (UTC+7)</span>
          <span role="status">
            {loading ? 'กำลังอัปเดตข้อมูล…' : `อัปเดตข้อมูลล่าสุด: ${date(lastUpdated)}`}
          </span>
        </footer>
      </main>
      {json !== null ? <JsonDialog value={json} close={() => setJson(null)} /> : null}
    </div>
  );
}

function Pagination({
  previous,
  next,
  disabled,
  back,
  nextPage,
}: {
  previous: boolean;
  next: boolean;
  disabled: boolean;
  back: () => void;
  nextPage: () => void;
}) {
  return previous || next ? (
    <div className="pagination">
      <button className="button" disabled={!previous || disabled} onClick={back}>
        ก่อนหน้า
      </button>
      <span>หน้าละไม่เกิน 50 รายการ</span>
      <button className="button" disabled={!next || disabled} onClick={nextPage}>
        ถัดไป
      </button>
    </div>
  ) : null;
}

createRoot(document.getElementById('root')!).render(<App />);
