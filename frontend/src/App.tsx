import { useEffect, useRef } from 'react';
import { useAuthStore } from './core/store/useAuthStore';
import { AppRouter } from './app/router/AppRouter';
import { Loader2 } from 'lucide-react'; // Asumiendo que usas Lucide, lo importamos

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

  return <AppRouter />;
}

export default App;