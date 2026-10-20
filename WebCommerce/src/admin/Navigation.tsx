import { ArrowLeft } from 'lucide-react';
import { commerce, useCommerce } from '../store';
import { admin, useAdmin } from './store';
import { adminTabs, allowed } from './types';
import { run } from './Ui';
export function AdminNavigation() {
  const { session } = useCommerce();
  const { tab } = useAdmin();
  return (
    <>
      <nav aria-label="เมนูจัดการร้านค้า">
        {adminTabs
          .filter((t) => allowed(session, t.role))
          .map((item) => (
            <button
              key={item.id}
              aria-current={item.id === tab ? 'page' : undefined}
              onClick={() => run(() => admin.selectTab(item.id))}
            >
              {item.label}
            </button>
          ))}
      </nav>
      <button className="text-button admin-return" onClick={() => commerce.close()}>
        <ArrowLeft size={20} />
        กลับหน้าร้าน
      </button>
    </>
  );
}
