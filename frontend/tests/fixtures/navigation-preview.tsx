import { useEffect } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from '../../src/core/query/queryClient';
import { connectQueryIdentity, disconnectQueryIdentity, persistQueryCache, clearNexFlowStorage } from '../../src/core/query/queryPersistence';
import { WorkspaceLayout } from '../../src/layouts/WorkspaceLayout';
import { ProfileTab } from '../../src/features/business/components/ProfileTab';
import { ToastProvider } from '../../src/components/ui/Toast';
import { calls, fixture, remote, locations, initialProfile } from './navigation-api';
import { useAuthStore, navigationIdentity } from './navigation-store';
import type { MeResponse } from '../../src/core/types/auth.types';
import '../../src/index.css';

const setIdentity = (me: MeResponse | null) => { disconnectQueryIdentity(); if (me) connectQueryIdentity(me); useAuthStore.setState({ me, selectedLocationId: 'all' }); };
queryClient.setDefaultOptions({ queries: { ...queryClient.getDefaultOptions().queries, retry: false } });
connectQueryIdentity(useAuthStore.getState().me!);
let logoutCount = 0;
useAuthStore.setState({ logout: async () => { logoutCount++; disconnectQueryIdentity(); clearNexFlowStorage(); useAuthStore.setState({ me: null, selectedLocationId: 'all' }); } });
const audit = { calls, fixture, remote, locations, initialProfile, store: useAuthStore, client: queryClient, setIdentity, persistQueryCache, defaultIdentity: navigationIdentity, logoutCount: () => logoutCount, boot: Date.now() };
Object.assign(window, { navigationAudit: audit });
export const NavigationPreview = () => {
  const navigate = useNavigate();
  const location = useLocation();
  useEffect(() => { Object.assign(audit, { navigate }); }, [navigate]);
  return <Routes><Route path="/" element={<WorkspaceLayout />}>
    <Route path="settings" element={<ProfileTab />} />
    <Route path="*" element={<p data-page>{location.pathname} · Contenido ficticio para probar la navegación</p>} />
    <Route index element={<p data-page>Dashboard · Contenido ficticio para probar la navegación</p>} />
  </Route></Routes>;
};
createRoot(document.getElementById('root')!).render(<QueryClientProvider client={queryClient}><ToastProvider><MemoryRouter><NavigationPreview /></MemoryRouter></ToastProvider></QueryClientProvider>);
