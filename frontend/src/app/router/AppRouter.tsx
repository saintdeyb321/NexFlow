import { createBrowserRouter, RouterProvider } from 'react-router-dom';
import { ProtectedRoute } from './ProtectedRoute';
import { WorkspaceLayout } from '../../layouts/WorkspaceLayout';
import { LoginPage } from '../../auth/pages/LoginPage';
import { SettingsPage } from '../../features/business/pages/SettingsPage';
import { ServicesPage } from '../../features/business/pages/ServicesPage';
import { FaqsPage } from '../../features/business/pages/FaqsPage';
import { ReservationsPage } from '../../features/reservations/pages/ReservationsPage';
import { SuperAdminPage } from '../../features/admin/pages/SuperAdminPage';
// Placeholders de los próximos sprints
const DashboardPlaceholder = () => <div className="text-2xl font-bold text-gray-800">Bienvenido al Dashboard</div>;

const router = createBrowserRouter([
  {
    path: '/login',
    element: <LoginPage />, // <-- CONECTAMOS LA PANTALLA REAL AQUÍ
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
          { path: 'reservations', element: <ReservationsPage /> },
          { path: 'faqs', element: <FaqsPage/> },
          { path: 'settings', element: <SettingsPage /> },
          { path: 'services', element: <ServicesPage/> },
          { path: 'superadmin', element: <SuperAdminPage /> },
        ],
      },
    ],
  },
]);

export const AppRouter = () => {
  return <RouterProvider router={router} />;
};