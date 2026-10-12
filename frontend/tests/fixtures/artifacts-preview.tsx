import { createRoot } from 'react-dom/client';
import { flushSync } from 'react-dom';
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient as client } from '../../src/core/query/queryClient';
import { connectQueryIdentity, disconnectQueryIdentity } from '../../src/core/query/queryPersistence';
import { ToastProvider } from '../../src/components/ui/Toast';
import { ArtifactGenerator } from '../../src/features/artifacts/components/ArtifactGenerator';
import { useAuthStore, reservationIdentity } from './reservations-store';
import { fixture, calls, states, quota, key, initial } from './artifacts-api';
import type { MeResponse } from '../../src/core/types/auth.types';
import '../../src/index.css';

const identity: MeResponse = { ...structuredClone(reservationIdentity), entitlements: ['CATALOG', 'SERVICES'], capabilities: { CATALOG: ['READ', 'GENERATE'], SERVICES: ['READ', 'GENERATE'] } };
const setIdentity = (me: MeResponse | null) => flushSync(() => {
  disconnectQueryIdentity(); if (me) connectQueryIdentity(me); useAuthStore.setState({ me, selectedLocationId: 'all' });
});
client.setDefaultOptions({ queries: { retry: false, refetchOnWindowFocus: false }, mutations: { retry: false } });
setIdentity(identity);
Object.assign(window, { artifactAudit: { fixture, calls, states, quota, key, initial, client, setIdentity, identity } });
createRoot(document.getElementById('root')!).render(<QueryClientProvider client={client}><ToastProvider><div className="p-5">
  <ArtifactGenerator scope="PRODUCT" title="Folleto PRODUCT" /><ArtifactGenerator scope="SERVICE" title="Folleto SERVICE" />
</div></ToastProvider></QueryClientProvider>);
