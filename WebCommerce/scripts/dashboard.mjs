import { rename } from 'node:fs/promises';
// Keep the public entry predictable; Vite emits the name of its HTML input.
await rename(
  new URL('../../Services/TtbCommerce/src/TtbCommerce.Api/Dashboard/dashboard.html', import.meta.url),
  new URL('../../Services/TtbCommerce/src/TtbCommerce.Api/Dashboard/index.html', import.meta.url),
);
