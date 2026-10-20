import { afterEach, expect, it, vi } from 'vitest';
import { CommerceApi, StaleSessionError } from '../src/api';
import { allowed, exactMoney, minorInput } from '../src/admin/types';
afterEach(() => vi.unstubAllGlobals());
it('continues an early history request after cookie authentication finishes instead of leaving the shopper on login', async () => {
  const { shop } = await stores();
  let completeSession!: (response: Response) => void;
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string) => {
      if (url.endsWith('/session'))
        return new Promise<Response>((resolve) => {
          completeSession = resolve;
        });
      const value =
        url.includes('/catalog/products') || url.includes('/orders?')
          ? { items: [], nextCursor: null }
          : url.endsWith('/cart')
            ? { version: 0, lines: [], subtotalMinor: 0 }
            : { environment: 'Local', paymentMode: 'Mock' };
      return Promise.resolve(new Response(JSON.stringify(value), { status: 200 }));
    }),
  );
  const loading = shop.bootstrap();
  shop.navigate('history');
  expect(shop.state.screen).toBe('login');
  completeSession(
    new Response(
      JSON.stringify({
        authenticated: true,
        user: { id: 'buyer', displayName: 'Buyer', roles: [] },
        csrfToken: 'cookie-csrf',
      }),
      { status: 200 },
    ),
  );
  await loading;
  expect(shop.state.session.authenticated).toBe(true);
  expect(shop.state.screen).toBe('history');
  await vi.waitFor(() => expect(shop.state.busy).toBe(false));
});
async function stores() {
  vi.stubGlobal('window', { MANGOS_TTB_CONFIG: { apiBase: '/ttb-commerce/v1' } });
  vi.stubGlobal('location', { origin: 'https://shop.test' });
  const { CommerceStore } = await import('../src/store');
  const { AdminStore } = await import('../src/admin/store');
  const shop = new CommerceStore(new CommerceApi('https://shop.test/ttb-commerce/v1'));
  return { shop, admin: new AdminStore(shop) };
}
it('limits visible operations by verified roles and formats bank money without floating point loss', () => {
  const finance = { authenticated: true, user: { id: 'f', displayName: 'finance', roles: ['finance'] } };
  expect(allowed(finance, 'admin')).toBe(false);
  expect(allowed(finance, 'operations')).toBe(true);
  expect(allowed(finance, 'finance')).toBe(true);
  expect(allowed({ ...finance, user: { ...finance.user, roles: ['fulfillment'] } }, 'finance')).toBe(false);
  expect(exactMoney('9007199254740993')).toBe('฿90,071,992,547,409.93');
  expect(exactMoney('-1')).toBe('-฿0.01');
  expect(minorInput('1499.01')).toBe(149901);
  for (const value of ['1e3', '12.999', '-1', 'NaN']) expect(() => minorInput(value)).toThrow();
});
it('erases private admin data and rejects an in-flight response when the same account loses a role', async () => {
  const { shop, admin } = await stores();
  shop.applySession({
    authenticated: true,
    user: { id: 'same', displayName: 'Operator', roles: ['admin'] },
    csrfToken: 'before',
  });
  admin.patch({ summary: { openCases: 99 } as never, productId: 'private-product' });
  let complete!: (value: Response) => void;
  vi.stubGlobal(
    'fetch',
    vi.fn(
      () =>
        new Promise<Response>((resolve) => {
          complete = resolve;
        }),
    ),
  );
  const pending = admin.chooseCase('private-case');
  shop.applySession({
    authenticated: true,
    user: { id: 'same', displayName: 'Operator', roles: [] },
    csrfToken: 'after',
  });
  expect(admin.state.summary).toBeNull();
  expect(admin.state.productId).toBeNull();
  expect(admin.state.cases).toEqual([]);
  complete(
    new Response(JSON.stringify({ case: { id: 'private-case' }, notes: [{ body: 'PRIVATE' }] }), {
      status: 200,
    }),
  );
  await expect(pending).rejects.toBeInstanceOf(StaleSessionError);
  expect(admin.state.detail).toBeNull();
});
it('keeps a stable privileged operation key across an uncertain retry and rotates it when input changes', async () => {
  const { admin } = await stores();
  const first = admin.operation('stock:v', { onHandDelta: 3, reason: 'Return' });
  expect(admin.operation('stock:v', { onHandDelta: 3, reason: 'Return' })).toBe(first);
  expect(admin.operation('stock:v', { onHandDelta: 4, reason: 'Return' })).not.toBe(first);
  expect(admin.operation('void:b', { reason: 'Cancel' })).not.toBe(first);
});
it('does not overwrite an explicit navigation when the login cart response arrives later', async () => {
  const { shop } = await stores();
  shop.patch({ screen: 'login', config: { environment: 'Local', mockSimulation: true } as never });
  let completeCart!: (response: Response) => void;
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string) =>
      url.endsWith('/session/local')
        ? Promise.resolve(
            new Response(
              JSON.stringify({
                authenticated: true,
                user: { id: 'operator', displayName: 'Operator', roles: ['admin'] },
                csrfToken: 'csrf',
              }),
              { status: 200 },
            ),
          )
        : new Promise<Response>((resolve) => {
            completeCart = resolve;
          }),
    ),
  );
  const signingIn = shop.signInLocal('demo-admin');
  await vi.waitFor(() => expect(shop.state.session.authenticated).toBe(true));
  shop.navigate('admin');
  completeCart(new Response(JSON.stringify({ version: 0, lines: [], subtotalMinor: 0 }), { status: 200 }));
  await signingIn;
  expect(shop.state.screen).toBe('admin');
  expect(shop.state.busy).toBe(false);
});
it('uploads private files with CSRF/no-store and discards an old-account response', async () => {
  const api = new CommerceApi('https://shop.test/ttb-commerce/v1');
  api.setSession('file-csrf');
  let complete!: (value: Response) => void;
  const fetch = vi.fn(
    (_url: RequestInfo | URL, _options?: RequestInit) =>
      new Promise<Response>((resolve) => {
        complete = resolve;
      }),
  );
  vi.stubGlobal('fetch', fetch);
  const file = new File(['Synthetic QA'], 'คู่มือ.txt');
  const pending = api.upload('/admin/digital-assets', file);
  expect(fetch).toHaveBeenCalledTimes(1);
  const options = fetch.mock.calls[0]![1] as RequestInit;
  expect(options.credentials).toBe('include');
  expect(options.cache).toBe('no-store');
  expect((options.headers as Record<string, string>)['X-CSRF-Token']).toBe('file-csrf');
  expect((options.headers as Record<string, string>)['X-File-Name']).toBe(encodeURIComponent(file.name));
  expect(options.body).toBe(file);
  api.reset();
  complete(new Response('{"key":"old-private-file"}', { status: 200 }));
  await expect(pending).rejects.toBeInstanceOf(StaleSessionError);
});
