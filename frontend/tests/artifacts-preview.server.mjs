import { createServer } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { fileURLToPath } from 'node:url';

export const startPreview = async () => {
  const root = fileURLToPath(new URL('../', import.meta.url));
  const hangingResponses = new Set();
  const server = await createServer({ root, configFile: false, plugins: [
    { name: 'artifact-test-boundaries', enforce: 'pre', resolveId(id) {
      if (id.endsWith('/app/config/firebase')) return fileURLToPath(new URL('./fixtures/artifacts-auth.ts', import.meta.url));
      if (id.endsWith('/store/useAuthStore')) return fileURLToPath(new URL('./fixtures/reservations-store.ts', import.meta.url));
    }, configureServer(vite) {
      vite.middlewares.use('/tests/artifact-http-hang', (_request, response) => {
        hangingResponses.add(response);
        response.once('close', () => hangingResponses.delete(response));
        // Intentionally no headers/body: exercise the real browser XHR timeout.
      });
      vite.httpServer.once('close', () => { for (const response of hangingResponses) response.destroy(); });
    } }, react(), tailwindcss(),
  ], server: { host: '127.0.0.1', port: 0 } });
  await server.listen(); return { server, url: `${server.resolvedUrls.local[0]}tests/fixtures/artifacts-preview.html` };
};
