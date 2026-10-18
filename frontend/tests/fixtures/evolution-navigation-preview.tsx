import { createRoot } from 'react-dom/client';
import { QueryClientProvider } from '@tanstack/react-query';
import { AppRouter } from '../../src/app/router/AppRouter';
import { queryClient } from '../../src/core/query/queryClient';
import { ToastProvider } from '../../src/components/ui/Toast';
import { fixture, calls } from './evolution-navigation-api';
import { useAuthStore } from './evolution-navigation-store';
import '../../src/index.css';

queryClient.setDefaultOptions({ queries: { retry: false } });
Object.assign(window, { evolutionNavigation: { fixture, calls, store: useAuthStore, refreshNotifications: () => queryClient.invalidateQueries({ queryKey: ['workspace', 'workspace-a', 'notifications'] }) } });
createRoot(document.getElementById('root')!).render(<QueryClientProvider client={queryClient}><ToastProvider><AppRouter /></ToastProvider></QueryClientProvider>);
