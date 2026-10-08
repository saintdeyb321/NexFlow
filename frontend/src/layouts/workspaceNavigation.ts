import { BookOpen, Calendar, ClipboardList, LayoutDashboard, MessageCircle, Package, Scissors, Settings, ShieldAlert, ShoppingBag } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';

interface NavigationItem { route: string; label: string; icon: LucideIcon }
export interface NavigationGroup { label: string; items: NavigationItem[] }
export const MODULE_REGISTRY: Record<string, NavigationItem> = {
  CONVERSATIONS: { route: '/inbox', label: 'Mensajes', icon: MessageCircle },
  RESERVATIONS: { route: '/reservations', label: 'Reservas', icon: Calendar },
  ORDERS: { route: '/orders', label: 'Pedidos', icon: ShoppingBag },
  REQUESTS: { route: '/requests', label: 'Solicitudes', icon: ClipboardList },
  SERVICES: { route: '/services', label: 'Servicios', icon: Scissors },
  CATALOG: { route: '/catalog', label: 'Catálogo', icon: Package },
  FAQ: { route: '/faqs', label: 'Base de conocimiento', icon: BookOpen },
};
const dashboard: NavigationItem = { route: '/', label: 'Dashboard', icon: LayoutDashboard };
const settings: NavigationItem = { route: '/settings', label: 'Negocio', icon: Settings };
const superadmin: NavigationItem = { route: '/superadmin', label: 'Consola SuperAdmin', icon: ShieldAlert };
export const navigationGroups = (can: (module: string, capability: string) => boolean, isSuperAdmin: boolean): NavigationGroup[] => {
  const permitted = (codes: string[]) => codes.filter(code => can(code, 'READ')).map(code => MODULE_REGISTRY[code]);
  return [
    { label: 'Operación', items: [...(can('CONVERSATIONS', 'READ') ? [dashboard] : []), ...permitted(['CONVERSATIONS', 'RESERVATIONS', 'ORDERS', 'REQUESTS'])] },
    { label: 'Gestión', items: permitted(['SERVICES', 'CATALOG', 'FAQ']) },
    { label: 'Administración', items: [
      ...(['BUSINESS_PROFILE', 'LOCATIONS', 'BUSINESS_HOURS', 'CONVERSATIONS'].some(module => can(module, 'READ')) ? [settings] : []),
      ...(isSuperAdmin ? [superadmin] : []),
    ] },
  ].filter(group => group.items.length > 0);
};
export const matchesNavigationRoute = (pathname: string, route: string) => pathname === route || (route !== '/' && pathname.startsWith(`${route}/`));
export const currentNavigationLabel = (pathname: string) => [dashboard, settings, superadmin, ...Object.values(MODULE_REGISTRY)]
  .find(item => matchesNavigationRoute(pathname, item.route))?.label ?? 'NexFlow';
export interface CommercialIdentity { label: string; status: 'ready' | 'loading' | 'error' }
export const commercialIdentity = ({ global, authorized, status, name }: { global: boolean; authorized: boolean; status: 'pending' | 'error' | 'success'; name?: string | null }): CommercialIdentity => {
  if (global) return { label: 'Administración Global', status: 'ready' };
  if (!authorized) return { label: 'Por definir', status: 'ready' };
  if (status === 'pending') return { label: 'Cargando negocio…', status: 'loading' };
  if (status === 'error') return { label: 'Negocio no disponible', status: 'error' };
  return { label: name?.trim() || 'Por definir', status: 'ready' };
};
