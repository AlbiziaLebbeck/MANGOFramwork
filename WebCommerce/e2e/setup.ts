import { request, type FullConfig } from '@playwright/test';
import { randomUUID } from 'node:crypto';

// Repeated browser runs consume physical demo stock. Use the audited API for named Local/Mock fixtures only.
export default async function setup(config: FullConfig) {
  const baseURL = config.projects[0].use.baseURL;
  if (!baseURL || !/^http:\/\/(127\.0\.0\.1|localhost):5176\/?$/.test(baseURL))
    throw new Error('Fixture setup requires the dedicated loopback Local server.');
  const client = await request.newContext({ baseURL, extraHTTPHeaders: { Origin: new URL(baseURL).origin } });
  try {
    const mode = await (await client.get('/ttb-commerce/v1/storefront/config')).json();
    if (mode.environment !== 'Local' || mode.paymentMode !== 'Mock' || mode.mockSimulation !== true)
      throw new Error('Fixture stock changes are forbidden outside Local/Mock.');
    const response = await client.post('/ttb-commerce/v1/session/local', { data: { subject: 'demo-admin' } });
    if (!response.ok()) throw new Error('Cannot authenticate the Local fixture administrator.');
    const session = await response.json();
    if (!session.user?.roles?.includes('admin') || !session.csrfToken)
      throw new Error('Local fixture administrator permission missing.');
    const catalog = await (await client.get('/ttb-commerce/v1/catalog/products?limit=100')).json();
    for (const slug of ['mango-orbit-lamp', 'mango-sculpture']) {
      const product = catalog.items.find((x: any) => x.slug === slug);
      if (!product) throw new Error('Required demo product missing: ' + slug);
      const detail = await client.get('/ttb-commerce/v1/admin/catalog/' + product.id);
      if (!detail.ok()) throw new Error('Cannot inspect Local fixture stock.');
      const entry = await detail.json();
      for (const variant of entry.variants.filter((x: any) => x.active && x.trackStock)) {
        const available = variant.onHand - variant.reserved;
        if (available >= 50) continue;
        const adjusted = await client.post(`/ttb-commerce/v1/admin/variants/${variant.id}/stock`, {
          headers: { 'X-CSRF-Token': session.csrfToken },
          data: {
            operationId: randomUUID(),
            onHandDelta: 100 - available,
            reason: 'Local browser QA fixture replenishment; synthetic stock only',
          },
        });
        if (!adjusted.ok()) throw new Error('Audited Local fixture stock adjustment failed.');
      }
    }
  } finally {
    await client.dispose();
  }
}
