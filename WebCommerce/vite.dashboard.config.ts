import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { resolve } from 'node:path';

export default defineConfig({
  plugins: [react()],
  base: '/mangoxttb/dashboard/',
  server: { host: '127.0.0.1', port: 5187, strictPort: true },
  build: {
    outDir: '../Services/TtbCommerce/src/TtbCommerce.Api/Dashboard',
    emptyOutDir: true,
    rollupOptions: { input: resolve(import.meta.dirname, 'dashboard.html') },
  },
});
