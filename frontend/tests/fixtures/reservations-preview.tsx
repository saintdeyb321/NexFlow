import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ToastProvider } from '../../src/components/ui/Toast';
import { ReservationsPage } from '../../src/features/reservations/pages/ReservationsPage';
import { calls, fixture } from './reservations-api';
import { useAuthStore } from './reservations-store';
import '../../src/index.css';

const client = new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false }, mutations: { retry: false } } });
// This entry is served only by the dedicated localhost test server, never the production entry.
Object.assign(window, { reservationAudit: { calls, fixture, store: useAuthStore, client } });
createRoot(document.getElementById('root')!).render(<QueryClientProvider client={client}><ToastProvider>
  <div className="p-3 sm:p-6"><p className="text-xs text-muted mb-4">Prueba local UX-01B · API simulada · Datos ficticios</p><ReservationsPage /></div>
</ToastProvider></QueryClientProvider>);
