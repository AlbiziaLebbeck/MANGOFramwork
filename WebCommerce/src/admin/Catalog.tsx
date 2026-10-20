import { useState } from 'react';
import { Box, Plus, Save, Upload } from 'lucide-react';
import { useCommerce } from '../store';
import { Empty } from '../components/Ui';
import type { Preview } from '../types';
import { admin, useAdmin } from './store';
import { exactMoney, minorInput, type CatalogEntry, type ProductForm, type AdminVariant } from './types';
import { Back, Fact, Field, Refresh, RowEnd, SearchField, State, run } from './Ui';
const physical = (kind: string) => kind.toLowerCase() === 'physical';
export default function Catalog() {
  const state = useAdmin();
  const { busy } = useCommerce();
  const [search, setSearch] = useState(''),
    [publication, setPublication] = useState('');
  const entry = state.products.find((x) => x.product.id === state.productId);
  const selected = !!entry || state.creating;
  const rows = state.products.filter(
    (x) =>
      (publication === '' || x.product.published === (publication === 'published')) &&
      `${x.product.name} ${x.variants.map((v) => v.sku).join(' ')}`
        .toLowerCase()
        .includes(search.toLowerCase()),
  );
  return (
    <div className={`admin-columns ${selected ? 'has-detail' : ''}`}>
      <section className="admin-queue">
        <h1>สินค้า</h1>
        <div className="admin-toolbar">
          <SearchField value={search} onChange={setSearch} />
          <select
            aria-label="การเผยแพร่สินค้า"
            value={publication}
            onChange={(e) => setPublication(e.target.value)}
          >
            <option value="">ทั้งหมด</option>
            <option value="published">เผยแพร่</option>
            <option value="draft">ฉบับร่าง</option>
          </select>
          <button
            className="button primary"
            disabled={busy}
            onClick={() => admin.patch({ creating: true, productId: null })}
          >
            <Plus size={21} />
            เพิ่มสินค้า
          </button>
          <Refresh />
        </div>
        <div className="admin-table">
          <div className="admin-table-head catalog-row">
            <span>สินค้า</span>
            <span>ประเภท</span>
            <span>ราคา</span>
            <span>คงเหลือ</span>
            <span>เผยแพร่</span>
          </div>
          {rows.map((row) => (
            <button
              key={row.product.id}
              className={`admin-table-row catalog-row ${row.product.id === state.productId ? 'selected' : ''}`}
              onClick={() => admin.patch({ productId: row.product.id, creating: false })}
            >
              <span>{row.product.name}</span>
              <span>{physical(row.product.kind) ? 'สินค้าจริง' : 'สินค้าดิจิทัล'}</span>
              <span>{row.variants[0] ? exactMoney(row.variants[0].priceMinor) : '—'}</span>
              <span>
                {row.variants.some((v) => !v.trackStock)
                  ? 'ไม่จำกัด'
                  : row.variants.reduce((sum, v) => sum + v.onHand - v.reserved, 0)}
              </span>
              <State
                value={row.product.published ? 'Ready' : 'Draft'}
                label={row.product.published ? 'เผยแพร่' : 'ฉบับร่าง'}
              />
              <RowEnd />
            </button>
          ))}
          {state.loaded && !rows.length ? <Empty>ไม่พบสินค้าตามเงื่อนไข</Empty> : null}
        </div>
        {state.productCursor ? (
          <button className="text-button" disabled={busy} onClick={() => run(() => admin.refresh(true))}>
            ดูสินค้าเพิ่มเติม
          </button>
        ) : null}
      </section>
      <aside className="admin-inspector">
        {selected ? (
          <CatalogInspector
            key={`${entry?.product.id ?? 'new'}:${entry?.product.version ?? 0}`}
            entry={entry}
          />
        ) : (
          <Empty>เลือกสินค้าเพื่อแก้ไข หรือเพิ่มสินค้าใหม่</Empty>
        )}
      </aside>
    </div>
  );
}
function CatalogInspector({ entry }: { entry?: CatalogEntry }) {
  const { busy } = useCommerce();
  const product = entry?.product;
  let preview: Preview = { modelKey: '', scale: 1, initialRotation: 0, minZoom: 0.5, maxZoom: 3 };
  try {
    if (product) preview = JSON.parse(product.previewJson);
  } catch {}
  const [form, setForm] = useState<ProductForm>({
    slug: product?.slug ?? '',
    name: product?.name ?? '',
    description: product?.description ?? '',
    kind: product?.kind.toLowerCase() ?? 'physical',
    thumbnailUrl: product?.thumbnailUrl ?? '',
    taxCode: product?.taxCode ?? 'Unspecified',
    preview,
  });
  const update = <K extends keyof ProductForm>(key: K, value: ProductForm[K]) =>
    setForm({ ...form, [key]: value });
  const previewField = (key: keyof Preview, value: string | number) =>
    setForm({ ...form, preview: { ...form.preview, [key]: value } });
  return (
    <>
      <Back />
      <div className="admin-detail-heading">
        <h2>{product ? 'แก้ไขสินค้า' : 'เพิ่มสินค้า'}</h2>
        <State
          value={product?.published ? 'Ready' : 'Draft'}
          label={product?.published ? 'เผยแพร่' : 'ฉบับร่าง'}
        />
      </div>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          run(() => admin.saveProduct(form));
        }}
      >
        <Field label="ชื่อสินค้า">
          <input
            required
            maxLength={200}
            value={form.name}
            onChange={(e) => update('name', e.target.value)}
          />
        </Field>
        {!product ? (
          <Field label="รหัส URL สินค้า">
            <input
              required
              pattern={'[a-z0-9][a-z0-9\\-]{1,79}'}
              maxLength={80}
              value={form.slug}
              onChange={(e) => update('slug', e.target.value)}
            />
          </Field>
        ) : null}
        <Field label="ประเภทสินค้า">
          <select value={form.kind} disabled={!!product} onChange={(e) => update('kind', e.target.value)}>
            <option value="physical">สินค้าจริง</option>
            <option value="digital">สินค้าดิจิทัล</option>
          </select>
        </Field>
        <Field label="รายละเอียด">
          <textarea
            rows={3}
            maxLength={10000}
            value={form.description}
            onChange={(e) => update('description', e.target.value)}
          />
        </Field>
        <Field label="Model key">
          <input
            required
            maxLength={100}
            pattern={'[a-zA-Z0-9_\\-]+'}
            value={form.preview.modelKey}
            onChange={(e) => previewField('modelKey', e.target.value)}
          />
        </Field>
        <details className="admin-config">
          <summary>ภาพสินค้าและการแสดงผล 3D</summary>
          <Field label="ที่อยู่ภาพสินค้า">
            <input
              maxLength={2000}
              value={form.thumbnailUrl}
              onChange={(e) => update('thumbnailUrl', e.target.value)}
            />
          </Field>
          <div className="admin-pair">
            {(
              [
                { key: 'scale', label: 'ขนาดโมเดล', min: 0.01, max: 100, step: 0.01 },
                { key: 'initialRotation', label: 'มุมเริ่มต้น', min: -360, max: 360, step: 1 },
                { key: 'minZoom', label: 'ระยะซูมใกล้สุด', min: 0.01, max: 100, step: 0.1 },
                { key: 'maxZoom', label: 'ระยะซูมไกลสุด', min: 0.01, max: 100, step: 0.1 },
              ] as const
            ).map((field) => (
              <Field label={field.label} key={field.key}>
                <input
                  type="number"
                  required
                  min={field.min}
                  max={field.max}
                  step="any"
                  value={form.preview[field.key]}
                  onChange={(e) => previewField(field.key, e.target.valueAsNumber)}
                />
              </Field>
            ))}
          </div>
        </details>
        <div className="admin-actions">
          <button className="button primary" disabled={busy}>
            <Save size={18} />
            บันทึกการแก้ไข
          </button>
          {product ? (
            <button
              type="button"
              className="button secondary"
              disabled={busy}
              onClick={() => run(() => admin.publication())}
            >
              {product.published ? 'พักเผยแพร่สินค้า' : 'เผยแพร่สินค้า'}
            </button>
          ) : null}
        </div>
      </form>
      {entry ? (
        <Variants entry={entry} />
      ) : (
        <p className="muted">บันทึกสินค้าฉบับร่างก่อนเพิ่ม SKU ราคา และการส่งมอบ</p>
      )}
    </>
  );
}
function Variants({ entry }: { entry: CatalogEntry }) {
  const [id, setId] = useState(entry.variants[0]?.id ?? '');
  const variant = entry.variants.find((x) => x.id === id);
  return (
    <section>
      <h3>รุ่นสินค้า / SKU</h3>
      <Field label="เลือกรุ่นสินค้า">
        <select value={id} onChange={(e) => setId(e.target.value)}>
          {entry.variants.map((v) => (
            <option key={v.id} value={v.id}>
              {v.sku} · {v.name}
            </option>
          ))}
          <option value="">เพิ่มรุ่นสินค้า</option>
        </select>
      </Field>
      <VariantForm key={id || 'new'} entry={entry} variant={variant} />
    </section>
  );
}
function VariantForm({ entry, variant }: { entry: CatalogEntry; variant?: AdminVariant }) {
  const { busy } = useCommerce();
  const { assets } = useAdmin();
  const isPhysical = physical(entry.product.kind);
  const [sku, setSku] = useState(variant?.sku ?? ''),
    [name, setName] = useState(variant?.name ?? ''),
    [price, setPrice] = useState(variant ? (variant.priceMinor / 100).toFixed(2) : ''),
    [active, setActive] = useState(variant?.active ?? true),
    [track, setTrack] = useState(variant?.trackStock ?? isPhysical),
    [kind, setKind] = useState(variant?.digitalKind ?? 'inventoryItem'),
    [asset, setAsset] = useState(variant?.digitalAssetKey ?? ''),
    [options, setOptions] = useState(variant?.optionsJson ?? '{}'),
    [error, setError] = useState('');
  const save = () => {
    let amount: number, values: Record<string, string>;
    try {
      amount = minorInput(price);
      values = JSON.parse(options);
      if (!values || Array.isArray(values) || Object.values(values).some((v) => typeof v !== 'string'))
        throw new Error();
    } catch {
      setError('ตรวจราคาเป็นบาททศนิยมไม่เกิน 2 ตำแหน่ง และตัวเลือกเป็นข้อมูลข้อความ');
      return;
    }
    setError('');
    run(() =>
      admin.saveVariant(variant?.id ?? null, {
        expectedProductVersion: entry.product.version,
        sku,
        name,
        priceMinor: amount,
        active,
        trackStock: track,
        options: values,
        digitalKind: kind,
        digitalAssetKey: isPhysical || kind.toLowerCase() !== 'download' ? null : asset || null,
      }),
    );
  };
  return (
    <>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          save();
        }}
      >
        <Field label="SKU">
          <input required maxLength={100} value={sku} onChange={(e) => setSku(e.target.value)} />
        </Field>
        <Field label="ชื่อรุ่น">
          <input required maxLength={200} value={name} onChange={(e) => setName(e.target.value)} />
        </Field>
        <div className="admin-pair">
          <Field label="ราคา (บาท)">
            <input
              required
              inputMode="decimal"
              pattern="[0-9]+(\.[0-9]{1,2})?"
              value={price}
              onChange={(e) => setPrice(e.target.value)}
            />
          </Field>
          <Field label="จำนวนพร้อมขาย">
            <input
              readOnly
              value={variant?.trackStock ? String(variant.onHand - variant.reserved) : 'ไม่จำกัด'}
            />
          </Field>
        </div>
        <label className="admin-check">
          <input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} />
          เปิดขายรุ่นนี้
        </label>
        {!isPhysical ? (
          <>
            <label className="admin-check">
              <input type="checkbox" checked={track} onChange={(e) => setTrack(e.target.checked)} />
              จำกัดจำนวนสินค้าดิจิทัล
            </label>
            <Field label="การส่งมอบดิจิทัล">
              <select value={kind} onChange={(e) => setKind(e.target.value)}>
                <option value="inventoryItem">ใช้สินค้าในเมตาเวิร์ส</option>
                <option value="download">ดาวน์โหลดไฟล์ส่วนตัว</option>
              </select>
            </Field>
            {kind.toLowerCase() === 'download' ? (
              <>
                <Field label="ไฟล์สำหรับผู้ซื้อ">
                  <select value={asset} onChange={(e) => setAsset(e.target.value)}>
                    <option value="">เลือกไฟล์ที่จัดเก็บแล้ว</option>
                    {assets
                      .filter((x) => x.ready)
                      .map((x) => (
                        <option key={x.key} value={x.key}>
                          {x.fileName}
                        </option>
                      ))}
                  </select>
                </Field>
                <Field label="เพิ่มไฟล์ส่วนตัว">
                  <input
                    type="file"
                    disabled={busy}
                    onChange={(e) => {
                      const file = e.target.files?.[0];
                      if (file)
                        run(async () => {
                          const saved = await admin.uploadAsset(file);
                          setAsset(saved.key);
                        });
                      e.target.value = '';
                    }}
                  />
                </Field>
              </>
            ) : null}
          </>
        ) : null}
        <details className="admin-config">
          <summary>ตัวเลือกของรุ่นสินค้า</summary>
          <Field label="ตัวเลือก JSON">
            <textarea
              rows={3}
              value={options}
              maxLength={10000}
              onChange={(e) => setOptions(e.target.value)}
            />
          </Field>
        </details>
        {error ? (
          <p className="message warning" role="alert">
            {error}
          </p>
        ) : null}
        <button className="button secondary" disabled={busy}>
          <Save size={18} />
          บันทึกรุ่นสินค้า
        </button>
      </form>
      {variant && variant.trackStock ? <Stock variant={variant} /> : null}
    </>
  );
}
function Stock({ variant }: { variant: AdminVariant }) {
  const { busy } = useCommerce();
  const [delta, setDelta] = useState(''),
    [reason, setReason] = useState('');
  return (
    <details className="admin-config">
      <summary>
        <Box size={18} />
        ปรับสต็อก
      </summary>
      <dl>
        <Fact label="คงเหลือทั้งหมด">{variant.onHand}</Fact>
        <Fact label="อยู่ระหว่างจอง">{variant.reserved}</Fact>
        <Fact label="พร้อมขาย">{variant.onHand - variant.reserved}</Fact>
      </dl>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          const value = Number(delta);
          if (!Number.isSafeInteger(value) || value === 0 || Math.abs(value) > 2147483647) return;
          run(async () => {
            await admin.stock(variant.id, value, reason);
            setDelta('');
            setReason('');
          });
        }}
      >
        <Field label="เพิ่ม / ลดจำนวน">
          <input
            type="number"
            step="1"
            required
            min={-2147483647}
            max={2147483647}
            value={delta}
            onChange={(e) => setDelta(e.target.value)}
          />
        </Field>
        <Field label="เหตุผลการปรับสต็อก">
          <textarea
            required
            maxLength={500}
            rows={2}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />
        </Field>
        <button
          className="button secondary"
          disabled={busy || !delta || Number(delta) === 0 || !reason.trim()}
        >
          ยืนยันปรับสต็อก
        </button>
      </form>
    </details>
  );
}
