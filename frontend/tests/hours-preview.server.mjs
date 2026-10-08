import { createServer } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { fileURLToPath } from 'node:url';

// Same localhost Vite/CDP boundaries as the existing category and agenda tests.
export const startPreview = async () => {
  const server = await createServer({
    root: fileURLToPath(new URL('../', import.meta.url)), configFile: false,
    plugins: [{ name: 'hours-test-boundaries', enforce: 'pre', resolveId(id) {
      if (id.endsWith('/api/axiosClient')) return fileURLToPath(new URL('./fixtures/hours-api.ts', import.meta.url));
      if (id.endsWith('/store/useAuthStore')) return fileURLToPath(new URL('./fixtures/hours-store.ts', import.meta.url));
    } }, react(), tailwindcss()], server: { host: '127.0.0.1', port: 0 },
  });
  await server.listen();
  return { server, url: `${server.resolvedUrls.local[0]}tests/fixtures/hours-preview.html` };
};
