import { useEffect } from 'react';
import { useCommerce } from '../store';
import { Empty, MockNotice } from '../components/Ui';
import { admin, useAdmin } from './store';
import { canAdmin } from './types';
import { run } from './Ui';
import Finance from './Finance';
import Catalog from './Catalog';
import Orders from './Orders';
import Reports from './Reports';
import Issuer from './Issuer';
import './admin.css';
export default function Admin() {
  const { session } = useCommerce();
  const { tab } = useAdmin();
  const signature = `${session.user?.id}:${session.user?.roles.slice().sort().join(',')}`;
  useEffect(() => {
    run(() => admin.start());
  }, [signature]);
  if (!canAdmin(session)) return <Empty>บัญชีนี้ไม่มีสิทธิ์จัดการร้านค้า</Empty>;
  return (
    <main className="admin-panel">
      <MockNotice />
      {tab === 'issuer' ? (
        <Issuer />
      ) : tab === 'finance' ? (
        <Finance />
      ) : tab === 'catalog' ? (
        <Catalog />
      ) : tab === 'orders' ? (
        <Orders />
      ) : (
        <Reports />
      )}
    </main>
  );
}
