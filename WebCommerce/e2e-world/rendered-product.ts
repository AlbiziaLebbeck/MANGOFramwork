import type { Page } from '@playwright/test';

// AreaSpawner chooses a random real spawn. Locate the visible orange demo meshes in the rendered
// screenshot instead of assuming a fixed camera/position or calling the product-open bridge.
export async function clickRenderedProduct(page: Page, product: 'lamp' | 'star') {
  const bounds = await page.locator('#unity-canvas').boundingBox();
  if (!bounds) throw new Error('Native canvas missing');
  const locate = async () => {
  const screenshot = await page.screenshot({ clip: bounds });
  return page.evaluate(async ({ encoded, product }) => {
    const image = new Image(); image.src = 'data:image/png;base64,' + encoded; await image.decode();
    const canvas = new OffscreenCanvas(image.width, image.height);
    const context = canvas.getContext('2d')!; context.drawImage(image, 0, 0);
    const { data, width, height } = context.getImageData(0, 0, image.width, image.height);
    const mask = new Uint8Array(width * height), groups: { x: number; y: number; centerX: number; centerY: number; minX: number; maxX: number; count: number }[] = [];
    for (let y = Math.floor(height * .15); y < height * .75; y++) for (let x = 0; x < width; x++) {
      const i = (y * width + x) * 4, r = data[i], g = data[i + 1], b = data[i + 2];
      if (r > 190 && g > 60 && g < 190 && b < 110 && r - g > 40) mask[y * width + x] = 1;
    }
    for (let start = 0; start < mask.length; start++) {
      if (!mask[start]) continue;
      const queue = [start]; mask[start] = 0;
      let minX = width, maxX = 0, minY = height, maxY = 0, sumX = 0, sumY = 0;
      for (let i = 0; i < queue.length; i++) {
        const at = queue[i], x = at % width, y = Math.floor(at / width);
        minX = Math.min(minX, x); maxX = Math.max(maxX, x); minY = Math.min(minY, y); maxY = Math.max(maxY, y); sumX += x; sumY += y;
        for (const next of [x > 0 ? at - 1 : -1, x + 1 < width ? at + 1 : -1, at - width, at + width])
          if (next >= 0 && next < mask.length && mask[next]) { mask[next] = 0; queue.push(next); }
      }
      if (queue.length < 40 || maxX - minX < 12 || maxY - minY < 12) continue;
      // Measure stability using bounds, but click an actually visible mesh pixel: the avatar can occlude the bounds center.
      const cx = sumX / queue.length, cy = sumY / queue.length;
      const hit = queue.reduce((best, at) => ((at % width - cx) ** 2 + (Math.floor(at / width) - cy) ** 2 < (best % width - cx) ** 2 + (Math.floor(best / width) - cy) ** 2 ? at : best), queue[0]);
      groups.push({ x: hit % width, y: Math.floor(hit / width), centerX: (minX + maxX) / 2, centerY: (minY + maxY) / 2, minX, maxX, count: queue.length });
    }
    if (groups.length < 2) throw new Error('The real scene does not expose both demo products clearly enough to click.');
    groups.sort((a, b) => a.minX - b.minX);
    return product === 'lamp' ? groups[0] : groups[groups.length - 1];
  }, { encoded: screenshot.toString('base64'), product });
  };
  let point = await locate(), stable = 0;
  const samples = [point];
  // The owning player and Cinemachine camera settle after scene activation; do not click a stale frame.
  for (let i = 0; i < 25 && stable < 2; i++) {
    await page.waitForTimeout(200);
    const next = await locate();
    stable = Math.hypot(next.centerX - point.centerX, next.centerY - point.centerY) <= 4 ? stable + 1 : 0;
    point = next;
    samples.push(point);
  }
  if (stable < 2) throw new Error('Native product/camera projection did not settle: '+JSON.stringify(samples));
  await page.mouse.click(bounds.x + point.x, bounds.y + point.y, { delay: 120 });
}
