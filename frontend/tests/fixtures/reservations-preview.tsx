import { createRoot } from 'react-dom/client';
import { flushSync } from 'react-dom';
import { useEffect, useState } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient as client } from '../../src/core/query/queryClient';
import { connectQueryIdentity, disconnectQueryIdentity } from '../../src/core/query/queryPersistence';
import { ToastProvider } from '../../src/components/ui/Toast';
import { ReservationsPage } from '../../src/features/reservations/pages/ReservationsPage';
import { calls, fixture, profiles, zones, initialProfile, rowsFor } from './reservations-api';
import { useAuthStore, reservationIdentity } from './reservations-store';
import { ProfileTab } from '../../src/features/business/components/ProfileTab';
import type { MeResponse } from '../../src/core/types/auth.types';
import '../../src/index.css';

client.setDefaultOptions({ queries: { retry: false, refetchOnWindowFocus: false }, mutations: { retry: false } });
connectQueryIdentity(useAuthStore.getState().me!);
const setIdentity = (me: MeResponse | null) => flushSync(() => {
  disconnectQueryIdentity();
  if (me) connectQueryIdentity(me);
  useAuthStore.setState({ me, selectedLocationId: 'all' });
});
// This entry is served only by the dedicated localhost test server, never the production entry.
const audit = { calls, fixture, store: useAuthStore, client, setIdentity, defaultIdentity: reservationIdentity, profiles, zones, initialProfile, rowsFor };
Object.assign(window, { reservationAudit: audit });
export const ReservationsPreview = () => {
  const [showProfile, setShowProfile] = useState(false);
  useEffect(() => { Object.assign(audit, { setShowProfile }); }, []);
  return <div className="p-3 sm:p-6"><p className="text-xs text-muted mb-4">Prueba local UX-01B · API simulada · Datos ficticios</p>
    {showProfile && <div data-test-profile><ProfileTab /></div>}<ReservationsPage /></div>;
};
createRoot(document.getElementById('root')!).render(<QueryClientProvider client={client}><ToastProvider><ReservationsPreview /></ToastProvider></QueryClientProvider>);
