import { Navigate, Outlet } from 'react-router-dom';
import { useAuthStore } from '../../core/store/useAuthStore';

export const ProtectedRoute = () => {
  const { isAuthenticated, isLoading } = useAuthStore();

  // Mientras valida con Firebase y el Backend, no mostramos nada para evitar destellos
  if (isLoading) return null; 

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  // Si está autenticado, renderiza las rutas hijas (el Dashboard, Configuración, etc.)
  return <Outlet />;
};