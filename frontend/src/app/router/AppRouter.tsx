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
import { DashboardPage } from '../../features/dashboard/pages/DashboardPage';
import { InboxPage } from '../../features/conversations/pages/InboxPage';
import { RequestsPage } from '../../features/requests/pages/RequestsPage';
import { CatalogPage } from '../../features/catalog/pages/CatalogPage';

// 1. GUARDIÁN DE MÓDULOS
const ModuleGuard = ({ requiredModule, children }: { requiredModule: string, children: React.ReactNode }) => {
  const { me } = useAuthStore();
  const hasAccess = me?.entitlements?.includes(requiredModule);
  if (!hasAccess) return <Navigate to="/" replace />;
  return <>{children}</>;
};

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
        element: <WorkspaceLayout />, // 🔥 Todo el Onboarding fue eliminado de raíz
        children: [
          { index: true, element: <DashboardPage /> },
          { path: 'superadmin', element: <SuperAdminPage /> },
          { path: 'reservations', element: <ModuleGuard requiredModule="RESERVATIONS"><ReservationsPage /></ModuleGuard> },
          { path: 'faqs', element: <ModuleGuard requiredModule="FAQ"><FaqsPage/></ModuleGuard> },
          { path: 'services', element: <ModuleGuard requiredModule="SERVICES"><ServicesPage/></ModuleGuard> },
          { path: 'inbox', element: <ModuleGuard requiredModule="CONVERSATIONS"><InboxPage/></ModuleGuard> },
          { path: 'requests', element: <ModuleGuard requiredModule="REQUESTS"><RequestsPage/></ModuleGuard> },
          { path: 'catalog', element: <ModuleGuard requiredModule="CATALOG"><CatalogPage/></ModuleGuard> },
          { path: 'settings', element: <SettingsPage /> },
        ],
      },
    ],
  },
]);

export const AppRouter = () => {
  return <RouterProvider router={router} />;
};