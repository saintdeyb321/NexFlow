import { useEffect, useRef } from 'react';
import { useAuthStore } from './core/store/useAuthStore';
import { AppRouter } from './app/router/AppRouter';
import { Loader2 } from 'lucide-react'; 
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// 🔥 Auditoría (Fase 5): Instancia del cliente de TanStack Query con reglas globales
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false, // Evita recargar cada vez que cambias de pestaña
      retry: 1, // Solo 1 reintento automático si falla la red, para no ocultar errores
    },
  },
});

function App() {
  const { checkSession, isBootstrapping } = useAuthStore();
  const hasBootstrapped = useRef(false);

  useEffect(() => {
    // 🔥 CORRECCIÓN (Fallo #21): El candado useRef evita la doble llamada en React StrictMode
    if (!hasBootstrapped.current) {
      checkSession();
      hasBootstrapped.current = true;
    }
  }, [checkSession]);

  if (isBootstrapping) {
    // 🔥 CORRECCIÓN (Sprint 19): Pantalla de carga global profesional
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

  // 🔥 Auditoría (Fase 5): Envolvemos el enrutador para habilitar caché global y polling
  return (
    <QueryClientProvider client={queryClient}>
      <AppRouter />
    </QueryClientProvider>
  );
}

export default App;