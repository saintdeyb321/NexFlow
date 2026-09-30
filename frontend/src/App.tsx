import { useEffect } from 'react';
import { onAuthStateChanged } from 'firebase/auth';
import { auth } from './app/config/firebase';
import { getQuerySession } from './core/query/queryPersistence';
import { useAuthStore } from './core/store/useAuthStore';
import { AppRouter } from './app/router/AppRouter';
import { Loader2 } from 'lucide-react'; 
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from './core/query/queryClient';
import { ErrorBoundary } from './core/layout/ErrorBoundary';

function App() {
  const { checkSession, isBootstrapping, logout, me } = useAuthStore();
  useEffect(() => onAuthStateChanged(auth, () => { void checkSession(true); }), [checkSession]);

  // 🔥 SPRINT 01: Limpieza absoluta de la caché de TanStack Query para evitar filtración de datos fantasma
  useEffect(() => {
    const handleSessionExpired = async () => {
      await logout();
    };

    window.addEventListener('session-expired', handleSessionExpired);
    return () => window.removeEventListener('session-expired', handleSessionExpired);
  }, [logout]);

  if (isBootstrapping) {
    return (
      <div className="flex flex-col h-screen w-screen items-center justify-center bg-gray-50">
        <div className="flex items-center text-blue-600 mb-4">
          <Loader2 className="w-8 h-8 animate-spin mr-3" />
          <h1 className="text-2xl font-bold tracking-tight">NexFlow</h1>
        </div>
        <p className="text-sm text-gray-500 font-medium animate-pulse">
          Estableciendo conexión segura...
        </p>
      </div>
    );
  }

  return (
    <ErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <AppRouter key={`${me?.user.id ?? ''}:${me?.workspace?.id ?? ''}:${getQuerySession()}`} />
      </QueryClientProvider>
    </ErrorBoundary>
  );
}

export default App;
