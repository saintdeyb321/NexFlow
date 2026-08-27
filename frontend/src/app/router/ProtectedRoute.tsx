import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuthStore } from '../../core/store/useAuthStore';

export const ProtectedRoute = () => {
  const { isAuthenticated, isLoading, me } = useAuthStore();
  const location = useLocation();

  if (isLoading) return null; 

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  // 🔥 CORRECCIÓN (Fallo #15): Si el SuperAdmin intenta entrar a cualquier ruta
  // que no sea /superadmin, lo forzamos a su panel.
  if (me?.user?.isSuperAdmin && !location.pathname.startsWith('/superadmin')) {
    return <Navigate to="/superadmin" replace />;
  }

  // Si es un inquilino normal y trata de entrar a /superadmin, lo mandamos a la raíz
  if (!me?.user?.isSuperAdmin && location.pathname.startsWith('/superadmin')) {
    return <Navigate to="/" replace />;
  }

  return <Outlet />;
};