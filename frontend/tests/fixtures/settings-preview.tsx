import { createRoot } from 'react-dom/client';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from '../../src/core/query/queryClient';
import { connectQueryIdentity, disconnectQueryIdentity } from '../../src/core/query/queryPersistence';
import { WorkspaceLayout } from '../../src/layouts/WorkspaceLayout';
import { SettingsPage } from '../../src/features/business/pages/SettingsPage';
import { ToastProvider } from '../../src/components/ui/Toast';
import { calls, fixture, profile, initialLocations, disconnected } from './settings-api';
import { useAuthStore, settingsIdentity } from './settings-store';
import type { MeResponse } from '../../src/core/types/auth.types';
import '../../src/index.css';

const setIdentity = (me: MeResponse | null) => { disconnectQueryIdentity(); if (me) connectQueryIdentity(me); useAuthStore.setState({ me, selectedLocationId: 'all' }); };
queryClient.setDefaultOptions({ queries: { ...queryClient.getDefaultOptions().queries, retry: false } });
connectQueryIdentity(useAuthStore.getState().me!);
Object.assign(window, { settingsAudit: { calls, fixture, store: useAuthStore, client: queryClient, setIdentity, defaultIdentity: settingsIdentity, profile, initialLocations, disconnected } });
createRoot(document.getElementById('root')!).render(<QueryClientProvider client={queryClient}><ToastProvider><MemoryRouter initialEntries={['/settings']}>
  <Routes><Route path="/" element={<WorkspaceLayout />}><Route path="settings" element={<SettingsPage />} /></Route></Routes>
</MemoryRouter></ToastProvider></QueryClientProvider>);
