import { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from '../../src/core/query/queryClient';
import { connectQueryIdentity, disconnectQueryIdentity } from '../../src/core/query/queryPersistence';
import { HoursTab } from '../../src/features/business/components/HoursTab';
import { Button } from '../../src/components/ui/Button';
import { FormField, Select } from '../../src/components/ui/Form';
import { calls, fixture, remote } from './hours-api';
import { useAuthStore, hoursIdentity } from './hours-store';
import type { MeResponse } from '../../src/core/types/auth.types';
import '../../src/index.css';

const setIdentity = (me: MeResponse | null) => {
  disconnectQueryIdentity(); if (me) connectQueryIdentity(me);
  useAuthStore.setState({ me });
};
connectQueryIdentity(useAuthStore.getState().me!);
Object.assign(window, { hoursAudit: { calls, fixture, remote, store: useAuthStore, client: queryClient, setIdentity, defaultIdentity: hoursIdentity } });
export const HoursPreview = () => {
  const [mounted, setMounted] = useState(true);
  const location = useAuthStore(state => state.selectedLocationId);
  return <main className="p-3 sm:p-6 max-w-6xl mx-auto"><p className="text-xs text-muted mb-4">Prueba local UX-03 · API simulada · Datos ficticios</p>
    <div className="flex flex-wrap gap-3 mb-5 items-end"><FormField label="Sede de prueba"><Select value={location} onChange={event => useAuthStore.getState().setSelectedLocationId(event.target.value)}>
      <option value="all">Todas las sedes</option><option value="empty">Sin configurar</option><option value="closed">Cerrada</option><option value="partial">Parcial</option><option value="north">Norte</option><option value="south">Sur</option>
    </Select></FormField><Button variant="secondary" onClick={() => setMounted(previous => !previous)}>Cambiar pestaña de prueba</Button></div>
    {mounted && <HoursTab />}
  </main>;
};
createRoot(document.getElementById('root')!).render(<QueryClientProvider client={queryClient}><HoursPreview /></QueryClientProvider>);
