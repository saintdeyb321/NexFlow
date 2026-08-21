import { createBrowserRouter, RouterProvider, Navigate } from 'react-router-dom';
import { ProtectedRoute } from './ProtectedRoute';
import { WorkspaceLayout } from '../../layouts/WorkspaceLayout';
import { LoginPage } from '../../auth/pages/LoginPage';
import { SettingsPage } from '../../features/business/pages/SettingsPage';
import { ServicesPage } from '../../features/business/pages/ServicesPage';
import { FaqsPage } from '../../features/business/pages/FaqsPage';
import { ReservationsPage } from '../../features/reservations/pages/ReservationsPage';
import { SuperAdminPage } from '../../features/admin/pages/SuperAdminPage';
import { useAuthStore } from '../../core/store/useAuthStore';

// 1. GUARDIÁN DE MÓDULOS: Bloquea si no tiene el módulo en su licencia
const ModuleGuard = ({ requiredModule, children }: { requiredModule: string, children: React.ReactNode }) => {
  const { me } = useAuthStore();
  const hasAccess = me?.entitlements?.includes(requiredModule);
  
  if (!hasAccess) return <Navigate to="/" replace />;
  return <>{children}</>;
};

// 2. GUARDIÁN DE SUPER ADMIN: Bloquea intrusos
const SuperAdminGuard = ({ children }: { children: React.ReactNode }) => {
  const { me } = useAuthStore();
  
  if (!me?.user?.isSuperAdmin) return <Navigate to="/" replace />;
  return <>{children}</>;
};

const DashboardPlaceholder = () => <div className="text-2xl font-bold text-gray-800">Bienvenido al Dashboard</div>;

const router = createBrowserRouter([
  {
    path: '/login',
    element: <LoginPage />,
  },
  {
    path: '/',
    element: <ProtectedRoute />, 
    children: [
      {
        path: '/',
        element: <WorkspaceLayout />,
        children: [
          { index: true, element: <DashboardPlaceholder /> },
          
          // RUTAS MODULARES PROTEGIDAS
          { 
            path: 'reservations', 
            element: <ModuleGuard requiredModule="RESERVATIONS"><ReservationsPage /></ModuleGuard>
          },
          { 
            path: 'faqs', 
            element: <ModuleGuard requiredModule="FAQ"><FaqsPage/></ModuleGuard>
          },
          { 
            path: 'services', 
            element: <ModuleGuard requiredModule="SERVICES"><ServicesPage/></ModuleGuard>
          },
          
          // RUTAS CORE GLOBALES
          { path: 'settings', element: <SettingsPage /> },
          
          // RUTA SUPERADMIN PROTEGIDA
          { 
            path: 'superadmin', 
            element: <SuperAdminGuard><SuperAdminPage /></SuperAdminGuard> 
          },
        ],
      },
    ],
  },
]);

export const AppRouter = () => {
  return <RouterProvider router={router} />;
};