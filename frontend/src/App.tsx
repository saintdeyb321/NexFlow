import { useEffect } from 'react';
import { useAuthStore } from './core/store/useAuthStore';
import { AppRouter } from './app/router/AppRouter';

function App() {
  const { checkSession, isLoading } = useAuthStore();

  useEffect(() => {
    // Al abrir la app, le preguntamos a Firebase y al Backend quién es este usuario
    checkSession();
  }, [checkSession]);

  if (isLoading) {
    // Pantalla de carga global (Splash Screen) mientras el backend responde
    return (
      <div className="flex h-screen w-screen items-center justify-center bg-gray-50">
        <div className="text-xl font-semibold text-blue-600 animate-pulse">
          Iniciando NexFlow...
        </div>
      </div>
    );
  }

  return <AppRouter />;
}

export default App;