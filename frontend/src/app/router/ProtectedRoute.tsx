import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuthStore } from '../../core/store/useAuthStore';

export const ProtectedRoute = () => {
  const { isAuthenticated, isLoading, me } = useAuthStore();
  const location = useLocation();

  if (isLoading) return null; 

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  if (!me?.user?.isSuperAdmin && location.pathname.startsWith('/superadmin')) {
    return <Navigate to="/" replace />;
  }

  return <Outlet />;
};