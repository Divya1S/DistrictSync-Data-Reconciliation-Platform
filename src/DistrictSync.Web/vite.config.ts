import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// .NET Aspire injects the API endpoint as services__api__https__0 / services__api__http__0 and the
// port to listen on as PORT. Outside Aspire the dev server falls back to the API's default URL.
const apiTarget =
  process.env.services__api__https__0 ?? process.env.services__api__http__0 ?? 'http://localhost:5080';

const port = Number.parseInt(process.env.PORT ?? '5173', 10);

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port,
    strictPort: true,
    host: true,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true, secure: false },
      '/hubs': { target: apiTarget, changeOrigin: true, secure: false, ws: true },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: false,
    // Route pages are React.lazy; Rollup's default splitting keeps recharts (~160 kB gzipped) in a
    // chunk that is only fetched with the Overview route. Explicit manualChunks were dropped because
    // the object form dragged shared modules (react/jsx-runtime) into the charts chunk and made it eager.
    chunkSizeWarningLimit: 600,
  },
});
