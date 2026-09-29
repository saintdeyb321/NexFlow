import { useEffect, useRef } from 'react';
import { useAuthStore } from './core/store/useAuthStore';
import { AppRouter } from './app/router/AppRouter';
import { Loader2 } from 'lucide-react'; 
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ErrorBoundary } from './core/layout/ErrorBoundary';

// 🔥 SPRINT 01: Exportado para permitir limpieza total en el flujo de logout
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      retry: 1, 
    },
  },
});

function App() {
  const { checkSession, isBootstrapping, logout } = useAuthStore();
  const hasBootstrapped = useRef(false);

  useEffect(() => {
    if (!hasBootstrapped.current) {
      checkSession();
      hasBootstrapped.current = true;
    }
  }, [checkSession]);

  // 🔥 SPRINT 01: Limpieza absoluta de la caché de TanStack Query para evitar filtración de datos fantasma
  useEffect(() => {
    const handleSessionExpired = async () => {
      queryClient.clear();
      await logout();
      alert("Tu sesión ha expirado. Por favor, inicia sesión nuevamente."); 
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
        <AppRouter />
      </QueryClientProvider>
    </ErrorBoundary>
  );
}

export default App;