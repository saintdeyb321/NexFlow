import { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from '../../src/core/query/queryClient';
import { connectQueryIdentity, disconnectQueryIdentity } from '../../src/core/query/queryPersistence';
import { ToastProvider } from '../../src/components/ui/Toast';
import { Button } from '../../src/components/ui/Button';
import { CatalogPage } from '../../src/features/catalog/pages/CatalogPage';
import { ServicesPage } from '../../src/features/services/pages/ServicesPage';
import { calls, fixture, remote } from './categories-api';
import { useAuthStore, categoryIdentity } from './categories-store';
import type { MeResponse } from '../../src/core/types/auth.types';
import '../../src/index.css';

const setIdentity = (me: MeResponse) => { disconnectQueryIdentity(); connectQueryIdentity(me); useAuthStore.setState({ me }); };
connectQueryIdentity(useAuthStore.getState().me);
Object.assign(window, { categoryAudit: { calls, fixture, remote, store: useAuthStore, client: queryClient, setIdentity, defaultIdentity: categoryIdentity } });
export const CategoryPreview = () => {
  const [page, setPage] = useState<'product' | 'service'>('product');
  return <div className="p-3 sm:p-6"><p className="text-xs text-muted mb-4">Prueba local UX-02 · API simulada · Datos ficticios</p>
    <div className="flex gap-2 mb-4"><Button onClick={() => setPage('product')}>Ver productos</Button><Button onClick={() => setPage('service')}>Ver servicios</Button></div>
    {page === 'product' ? <CatalogPage /> : <ServicesPage />}
  </div>;
};
createRoot(document.getElementById('root')!).render(<QueryClientProvider client={queryClient}><ToastProvider><CategoryPreview /></ToastProvider></QueryClientProvider>);
