import { createServer } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { fileURLToPath } from 'node:url';

export const startPreview = async () => {
  const root = fileURLToPath(new URL('../', import.meta.url));
  const server = await createServer({
    root, configFile: false, plugins: [
      { name: 'reservations-test-boundaries', enforce: 'pre', resolveId(id) {
        if (id.endsWith('/api/axiosClient')) return fileURLToPath(new URL('./fixtures/reservations-api.ts', import.meta.url));
        if (id.endsWith('/store/useAuthStore')) return fileURLToPath(new URL('./fixtures/reservations-store.ts', import.meta.url));
      } }, react(), tailwindcss(),
    ],
    server: { host: '127.0.0.1', port: 0 },
  });
  await server.listen();
  return { server, url: `${server.resolvedUrls.local[0]}tests/fixtures/reservations-preview.html` };
};
