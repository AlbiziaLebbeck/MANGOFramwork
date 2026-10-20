import { afterEach, expect, it, vi } from 'vitest';
import { CommerceApi } from '../src/api';

afterEach(() => vi.unstubAllGlobals());
const account = (id = 'alice') => ({
  authenticated: true,
  user: { id, displayName: id, roles: [] },
  csrfToken: 'test-csrf',
});
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
async function store() {
  vi.stubGlobal('window', { MANGOS_TTB_CONFIG: { apiBase: '/ttb-commerce/v1' } });
  vi.stubGlobal('location', { origin: 'https://shop.test' });
  const { CommerceStore } = await import('../src/store');
  return new CommerceStore(new CommerceApi('https://shop.test/ttb-commerce/v1'));
}
function publicResponse(url: string) {
  return json(
    url.endsWith('/cart')
      ? { version: 0, lines: [], subtotalMinor: 0 }
      : url.includes('/catalog') || url.includes('/orders?')
        ? { items: [], nextCursor: null }
        : { environment: 'Uat', paymentMode: 'Bank', paymentsReady: false },
  );
}

it('automatically exchanges an existing MANGOs cookie and continues to history without a second login', async () => {
  const shop = await store();
  const fetch = vi.fn((url: string, options: RequestInit) =>
    Promise.resolve(
      url.endsWith('/session')
        ? json(options.method === 'POST' ? account() : { authenticated: false })
        : publicResponse(url),
    ),
  );
  vi.stubGlobal('fetch', fetch);
  const loading = shop.bootstrap();
  shop.navigate('history');
  await loading;
  expect(shop.state.session.user?.id).toBe('alice');
  expect(shop.state.screen).toBe('history');
  const exchange = fetch.mock.calls.filter(
    ([url, options]) => url.endsWith('/session') && options.method === 'POST',
  );
  expect(exchange).toHaveLength(1);
  expect(exchange[0][1]).toMatchObject({ credentials: 'include', cache: 'no-store' });
  expect(exchange[0][1].body).toBeUndefined();
});

it('reuses a verified commerce session instead of rotating it on focus', async () => {
  const shop = await store();
  const fetch = vi.fn((url: string, _options: RequestInit) =>
    Promise.resolve(url.endsWith('/session') ? json(account()) : publicResponse(url)),
  );
  vi.stubGlobal('fetch', fetch);
  await shop.refreshSession();
  await shop.refreshSession();
  expect(fetch.mock.calls.filter(([url]) => url.endsWith('/session'))).toHaveLength(2);
  expect(
    fetch.mock.calls.every(([, options]) => !options || (options as RequestInit).method !== 'POST'),
  ).toBe(true);
});

it('keeps anonymous visitors anonymous without an error, redirect or Local identity', async () => {
  const shop = await store();
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string, options: RequestInit) =>
      Promise.resolve(
        url.endsWith('/session')
          ? options.method === 'POST'
            ? json({ code: 'login_required' }, 401)
            : json({ authenticated: false })
          : publicResponse(url),
      ),
    ),
  );
  await shop.bootstrap();
  expect(shop.state.session.authenticated).toBe(false);
  expect(shop.state.error).toBeNull();
  expect(shop.state.screen).toBe('gallery');
  expect(shop.state.sessionSyncing).toBe(false);
});

it('coalesces simultaneous startup, focus and native readiness checks into one exchange', async () => {
  const shop = await store();
  let complete!: (response: Response) => void;
  const fetch = vi.fn((url: string, options: RequestInit) =>
    url.endsWith('/session') && options.method === 'GET'
      ? new Promise<Response>((resolve) => {
          complete = resolve;
        })
      : Promise.resolve(url.endsWith('/session') ? json(account()) : publicResponse(url)),
  );
  vi.stubGlobal('fetch', fetch);
  const a = shop.refreshSession(),
    b = shop.reconnectSession(),
    c = shop.refreshSession();
  complete(json({ authenticated: false }));
  await Promise.all([a, b, c]);
  expect(
    fetch.mock.calls.filter(([url, options]) => url.endsWith('/session') && options.method === 'POST'),
  ).toHaveLength(1);
});

it('clears the old account before an account switch and never displays its history for the new account', async () => {
  const shop = await store();
  shop.applySession(account());
  shop.patch({ history: [{ id: 'alice-private-order' }] as never });
  let complete!: (response: Response) => void;
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string, options: RequestInit) =>
      url.endsWith('/session') && options.method === 'POST'
        ? new Promise<Response>((resolve) => {
            complete = resolve;
          })
        : Promise.resolve(url.endsWith('/session') ? json({ authenticated: false }) : publicResponse(url)),
    ),
  );
  const pending = shop.refreshSession();
  await vi.waitFor(() => expect(complete).toBeTypeOf('function'));
  expect(shop.state.history).toEqual([]);
  expect(shop.state.session.authenticated).toBe(false);
  complete(json(account('bob')));
  await pending;
  expect(shop.state.session.user?.id).toBe('bob');
  expect(shop.state.history).toEqual([]);
});

it('does not let a delayed exchange restore an ended native session', async () => {
  const shop = await store();
  let complete!: (response: Response) => void;
  const fetch = vi.fn((url: string, options: RequestInit) =>
    url.endsWith('/session') && options.method === 'POST'
      ? new Promise<Response>((resolve) => {
          complete = resolve;
        })
      : Promise.resolve(json({ authenticated: false })),
  );
  vi.stubGlobal('fetch', fetch);
  const pending = shop.refreshSession();
  await vi.waitFor(() => expect(complete).toBeTypeOf('function'));
  shop.endSession();
  complete(json(account()));
  await pending;
  await shop.refreshSession();
  expect(shop.state.session.authenticated).toBe(false);
  expect(fetch).toHaveBeenCalledTimes(2);
});

it('respects explicit signout across later focus checks', async () => {
  const shop = await store();
  shop.applySession(account());
  const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
  vi.stubGlobal('fetch', fetch);
  await shop.signOut();
  await shop.refreshSession();
  await shop.refreshSession();
  expect(fetch).toHaveBeenCalledTimes(1);
  expect(shop.state.session.authenticated).toBe(false);
});

it('checks anonymous SSO at most once a minute until a native login notification', async () => {
  const shop = await store();
  const fetch = vi.fn((_url: string, options: RequestInit) =>
    Promise.resolve(
      options.method === 'POST' ? json({ code: 'login_required' }, 401) : json({ authenticated: false }),
    ),
  );
  vi.stubGlobal('fetch', fetch);
  await shop.refreshSession();
  await shop.refreshSession();
  await shop.refreshSession();
  expect(fetch.mock.calls.filter(([, options]) => options.method === 'POST')).toHaveLength(1);
  await shop.reconnectSession();
  expect(fetch.mock.calls.filter(([, options]) => options.method === 'POST')).toHaveLength(2);
});

it('reconnects after a native login that arrives while the old session request is finishing', async () => {
  const shop = await store();
  let complete!: (response: Response) => void,
    first = true;
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string) => {
      if (url.endsWith('/session') && first) {
        first = false;
        return new Promise<Response>((resolve) => {
          complete = resolve;
        });
      }
      return Promise.resolve(url.endsWith('/session') ? json(account('bob')) : publicResponse(url));
    }),
  );
  const old = shop.refreshSession();
  shop.endSession();
  const next = shop.reconnectSession();
  complete(json(account('alice')));
  await Promise.all([old, next]);
  expect(shop.state.session.user?.id).toBe('bob');
});

it('does not attempt external SSO when Local simulation explicitly provides test identities', async () => {
  const shop = await store();
  const fetch = vi.fn((url: string) =>
    Promise.resolve(
      url.endsWith('/session')
        ? json({ authenticated: false })
        : url.endsWith('/storefront/config')
          ? json({ environment: 'Local', paymentMode: 'Mock', mockSimulation: true })
          : publicResponse(url),
    ),
  );
  vi.stubGlobal('fetch', fetch);
  await shop.bootstrap();
  expect(fetch.mock.calls.filter(([url]) => url.endsWith('/session'))).toHaveLength(1);
});

it('retains a valid visible session on a network outage but clears known-invalid credentials', async () => {
  const shop = await store();
  shop.applySession(account());
  vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')));
  await shop.refreshSession();
  expect(shop.state.session.user?.id).toBe('alice');
  vi.stubGlobal(
    'fetch',
    vi.fn((_url: string, options: RequestInit) =>
      Promise.resolve(
        options.method === 'POST'
          ? json({ code: 'identity_unavailable' }, 503)
          : json({ authenticated: false }),
      ),
    ),
  );
  await shop.refreshSession();
  expect(shop.state.session.authenticated).toBe(false);
});
