import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({command})=>({
  define: command==='build'?{'process.env.NODE_ENV':JSON.stringify('production')}:{},
  plugins: [react()], base: './',
  server: { port: 5176, strictPort: true, watch:{ignored:['**/test-results/**','**/playwright-report/**']}, proxy: { '/ttb-commerce/': { target: 'http://127.0.0.1:5286', changeOrigin: true } } },
  preview: { port:5176,strictPort:true,proxy:{'/ttb-commerce/':{target:'http://127.0.0.1:5286',changeOrigin:true}} },
  build: { lib: { entry: 'src/main.tsx', name: 'MangoTtbOverlay', formats: ['iife'], fileName: () => 'mango-ttb.js', cssFileName: 'mango-ttb' } },
}));
