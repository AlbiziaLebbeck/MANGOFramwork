import { describe, expect, it, vi, afterEach } from 'vitest';
import { date, money, request, DashboardError } from '../src/dashboard/api';

afterEach(() => vi.unstubAllGlobals());
describe('payment dashboard', () => {
  it('formats bank evidence in Bangkok time and Gregorian year consistently with server logs', () => {
    expect(date('2026-10-21T14:14:03Z')).toContain('2026');
    expect(date('2026-10-21T14:14:03Z', true)).toBe('21:14:03');
    expect(money(100)).toContain('1.00');
    expect(date(null)).toBe('—');
  });
  it('keeps authenticated requests same-origin, uncached and includes CSRF for logout', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetcher);
    await request('/logout', undefined, 'POST', 'unit-csrf');
    expect(fetcher).toHaveBeenCalledWith(
      '/mangoxttb/dashboard/api/logout',
      expect.objectContaining({
        method: 'POST',
        credentials: 'include',
        cache: 'no-store',
        headers: expect.objectContaining({ 'X-CSRF-Token': 'unit-csrf' }),
      }),
    );
  });
  it('reports authorization failures so the UI can clear restricted data', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue(
          new Response(JSON.stringify({ code: 'dashboard_access_required' }), { status: 403 }),
        ),
    );
    await expect(request('/overview')).rejects.toEqual(new DashboardError(403, 'dashboard_access_required'));
  });
});
