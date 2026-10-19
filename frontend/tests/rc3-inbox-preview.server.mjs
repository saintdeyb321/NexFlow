import { createServer } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { fileURLToPath } from 'node:url';

export const startPreview = async () => {
  const fixture = name => fileURLToPath(new URL(`./fixtures/${name}`, import.meta.url));
  const server = await createServer({ root: fileURLToPath(new URL('../', import.meta.url)), configFile: false, plugins: [
    { name: 'rc3-inbox-local-boundaries', enforce: 'pre', resolveId(id) {
      if (id.endsWith('/api/axiosClient')) return fixture('rc3-inbox-api.ts');
      if (id.endsWith('/store/useAuthStore')) return fixture('rc3-inbox-store.ts');
    } }, react(), tailwindcss(),
  ], server: { host: '127.0.0.1', port: 0 } });
  await server.listen(); return { server, url: `${server.resolvedUrls.local[0]}tests/fixtures/rc3-inbox-preview.html` };
};
