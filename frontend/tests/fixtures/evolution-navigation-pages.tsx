import { Outlet, useLocation } from 'react-router-dom';
import { NotificationBell } from '../../src/components/ui/NotificationBell';

export const WorkspaceLayout = () => <><header><NotificationBell /></header><Outlet /></>;
export const InboxPage = () => {
  const location = useLocation();
  if (location.search.includes('simulateError=1')) throw new Error('PRIVATE_INTERNAL_ERROR_MUST_NOT_BE_SHOWN');
  return <h1>Bandeja de entrada</h1>;
};
export const DashboardPage = () => <h1>Inicio</h1>;
export const LoginPage = () => <h1>Iniciar sesión</h1>;
export const SettingsPage = DashboardPage;
export const ServicesPage = DashboardPage;
export const FaqsPage = DashboardPage;
export const ReservationsPage = DashboardPage;
export const SuperAdminPage = DashboardPage;
export const RequestsPage = DashboardPage;
export const CatalogPage = DashboardPage;
export const OrdersPage = DashboardPage;
