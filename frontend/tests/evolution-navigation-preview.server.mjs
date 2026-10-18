import { createServer } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { fileURLToPath } from 'node:url';

export const startPreview = async () => {
  const fixture = name => fileURLToPath(new URL(`./fixtures/${name}`, import.meta.url));
  const server = await createServer({ root: fileURLToPath(new URL('../', import.meta.url)), configFile: false, plugins: [
    { name: 'evolution-navigation-local-boundaries', enforce: 'pre', resolveId(id, importer) {
      if (id.endsWith('/api/axiosClient')) return fixture('evolution-navigation-api.ts');
      if (id.endsWith('/store/useAuthStore')) return fixture('evolution-navigation-store.ts');
      if (importer?.endsWith('/AppRouter.tsx') && (id.includes('/pages/') || id.endsWith('/WorkspaceLayout'))) return fixture('evolution-navigation-pages.tsx');
    }, configureServer(vite) {
      vite.middlewares.use((request, _response, next) => {
        if (/^\/(?:inbox|conversations|login|missing-route)(?:[?#]|$)/.test(request.url) || request.url === '/') request.url = '/tests/fixtures/evolution-navigation-preview.html';
        next();
      });
    } }, react(), tailwindcss(),
  ], server: { host: '127.0.0.1', port: 0 } });
  await server.listen(); return { server, url: `${server.resolvedUrls.local[0]}inbox` };
};
