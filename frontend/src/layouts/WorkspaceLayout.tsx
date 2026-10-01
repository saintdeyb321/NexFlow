import { queryPolicies } from '../core/query/queryPolicies';
import { queryKeys } from '../core/query/queryKeys';
import { useEffect, useState } from 'react';
import { Outlet, Link, useLocation } from 'react-router-dom';
import { useAuthStore } from '../core/store/useAuthStore';
import { useQuery } from '@tanstack/react-query';
import { getLocations } from '../features/business/services/business.service';
import type { LocationDto } from '../features/business/types/business.types';
import { usePermissions } from '../core/auth/permissions';
import {
  LayoutDashboard, BookOpen, Calendar, Settings, LogOut, Scissors,
  ShieldAlert, MessageCircle, Package, ClipboardList, MapPin, ShoppingBag, Menu, ChevronRight, Layers
} from 'lucide-react';
import { Button, IconButton } from '../components/ui/Button';
import { Modal } from '../components/ui/Modal';
import { NotificationBell } from '../components/ui/NotificationBell';

const MODULE_REGISTRY: Record<string, { route: string; label: string; icon: React.ElementType }> = {
  'RESERVATIONS': { route: '/reservations', label: 'Reservas', icon: Calendar },
  'CONVERSATIONS': { route: '/inbox', label: 'Mensajes', icon: MessageCircle },
  'FAQ': { route: '/faqs', label: 'Base (FAQ)', icon: BookOpen },
  'SERVICES': { route: '/services', label: 'Servicios', icon: Scissors },
  'CATALOG': { route: '/catalog', label: 'Catálogo', icon: Package },
  'ORDERS': { route: '/orders', label: 'Pedidos', icon: ShoppingBag },
  'REQUESTS': { route: '/requests', label: 'Solicitudes', icon: ClipboardList }
};

export const WorkspaceLayout = () => {
  const { me, logout, selectedLocationId, setSelectedLocationId } = useAuthStore();
  const { pathname } = useLocation();
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);

  const { can } = usePermissions();
  const canReadLocations = can('LOCATIONS', 'READ');
  const canReadSettings = ['BUSINESS_PROFILE', 'LOCATIONS', 'BUSINESS_HOURS', 'CONVERSATIONS'].some(module => can(module, 'READ'));
  const isSuperAdmin = me?.user?.isSuperAdmin === true;
  const workspaceId = me?.workspace?.id;

  const { data: locations = [], isSuccess: locationsLoaded } = useQuery<LocationDto[]>({
    ...queryPolicies.stable,
    queryKey: queryKeys.locations.all(workspaceId),
    queryFn: ({ signal }) => getLocations(signal),
    enabled: !!workspaceId && canReadLocations,

  });

  useEffect(() => {
    if (selectedLocationId !== 'all' && (!canReadLocations || (locationsLoaded && !locations.some(location => location.id === selectedLocationId))))
      setSelectedLocationId('all');
  }, [workspaceId, selectedLocationId, canReadLocations, locationsLoaded, locations, setSelectedLocationId]);

  const isActive = (path: string) => pathname === path || (path !== '/' && pathname.startsWith(`${path}/`));
  const navItemClass = () => 'nf-nav-link';

  const activeModules = Object.keys(MODULE_REGISTRY)
    .filter(code => can(code, 'READ'))
    .map((code: string) => ({ code, ...MODULE_REGISTRY[code] }));

  const sidebarContent = (locationSelectorId: string) => (
    <div className="flex flex-col h-full min-h-0">
      <Link to="/" onClick={() => setIsMobileMenuOpen(false)} className="hidden lg:flex h-20 items-center gap-3 px-6 shrink-0 text-white">
        <span className="w-9 h-9 rounded-xl bg-primary flex items-center justify-center"><Layers aria-hidden="true" className="w-5 h-5" /></span>
        <span className="font-semibold text-xl tracking-tight">NexFlow<span className="text-accent">.</span></span>
      </Link>
      <div className="px-5 py-5 border-y border-white/10 shrink-0">
        <p className="text-[10px] font-semibold text-slate-400 uppercase tracking-[.15em] mb-1">Workspace</p>
        <p className="text-sm font-semibold text-white break-words mb-4">{isSuperAdmin ? 'Administración Global' : (me?.workspace?.name || 'Configurando...')}</p>
        <label htmlFor={locationSelectorId} className="text-xs text-slate-400 mb-2 block">Sede activa</label>
        <div className="relative">
          <MapPin aria-hidden="true" className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
          <select id={locationSelectorId} value={selectedLocationId} onChange={e => setSelectedLocationId(e.target.value)}
            className="nf-control bg-sidebar-hover text-slate-100 border-white/10 pl-9">
            <option value="all">Todas las sedes</option>
            {locations.map(loc => <option key={loc.id} value={loc.id}>{loc.name}</option>)}
          </select>
        </div>
      </div>
      <nav aria-label="Navegación principal" className="flex-1 p-3 space-y-1 overflow-y-auto min-h-0">
        <p className="px-4 pt-3 pb-2 text-[10px] font-semibold text-slate-500 uppercase tracking-[.15em]">Operación</p>
        {can('CONVERSATIONS', 'READ') && <Link to="/" aria-current={isActive('/') ? 'page' : undefined} onClick={() => setIsMobileMenuOpen(false)} className={navItemClass()}>
          <LayoutDashboard aria-hidden="true" className="w-5 h-5" /> Dashboard
        </Link>}
        {activeModules.map(({ code, route, label, icon: Icon }) => (
          <Link key={code} to={route} aria-current={isActive(route) ? 'page' : undefined} onClick={() => setIsMobileMenuOpen(false)} className={navItemClass()}>
            <Icon aria-hidden="true" className="w-5 h-5 shrink-0" /> {label}
          </Link>
        ))}
        <p className="px-4 pt-6 pb-2 text-[10px] font-semibold text-slate-500 uppercase tracking-[.15em]">Administración</p>
        {canReadSettings && <Link to="/settings" aria-current={isActive('/settings') ? 'page' : undefined} onClick={() => setIsMobileMenuOpen(false)} className={navItemClass()}>
          <Settings aria-hidden="true" className="w-5 h-5" /> Negocio
        </Link>}
        {isSuperAdmin && <Link to="/superadmin" aria-current={isActive('/superadmin') ? 'page' : undefined} onClick={() => setIsMobileMenuOpen(false)} className="nf-nav-link text-violet-300">
          <ShieldAlert aria-hidden="true" className="w-5 h-5" /> Consola SuperAdmin
        </Link>}
      </nav>
      <div className="p-4 border-t border-white/10 shrink-0">
        <div className="flex items-center gap-3 mb-3 px-2">
          <span className="h-9 w-9 shrink-0 rounded-full bg-sidebar-hover flex items-center justify-center text-sm font-semibold text-indigo-200" aria-hidden="true">{(me?.user?.firstName || me?.user?.email || 'N').slice(0,1).toUpperCase()}</span>
          <div className="min-w-0"><p className="text-sm font-medium text-slate-100 truncate">{me?.user?.firstName || 'Mi cuenta'}</p><p className="text-xs text-slate-400 truncate">{me?.user?.email}</p></div>
        </div>
        <Button variant="ghost" onClick={logout} className="w-full justify-start text-slate-300 hover:text-white hover:bg-sidebar-hover">
          <LogOut aria-hidden="true" className="w-4 h-4" /> Cerrar sesión
        </Button>
      </div>
    </div>
  );

  return (
    <div className="flex h-dvh bg-background overflow-hidden">
      <a href="#workspace-content" className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-[100] focus:bg-surface focus:p-3 focus:rounded-lg">Ir al contenido</a>
      <aside className="hidden lg:flex w-64 xl:w-72 bg-sidebar flex-col shrink-0 h-full">{sidebarContent('workspace-location-desktop')}</aside>
      <Modal isOpen={isMobileMenuOpen} onClose={() => setIsMobileMenuOpen(false)} title="NexFlow" placement="drawer">
        {sidebarContent('workspace-location-mobile')}
      </Modal>
      <main className="flex-1 min-w-0 flex flex-col h-full relative">
        <header className="h-18 bg-surface border-b border-line flex items-center justify-between gap-3 px-4 sm:px-6 lg:px-8 shrink-0">
          <div className="flex items-center min-w-0 gap-3">
            <IconButton label="Abrir menú" aria-expanded={isMobileMenuOpen} onClick={() => setIsMobileMenuOpen(true)} className="lg:hidden"><Menu aria-hidden="true" className="w-5 h-5" /></IconButton>
            <span className="font-semibold text-lg lg:hidden">NexFlow<span className="text-primary">.</span></span>
            <div className="hidden lg:flex items-center gap-2 text-sm min-w-0"><span className="text-muted truncate max-w-56">{me?.workspace?.name || 'Administración'}</span><ChevronRight aria-hidden="true" className="h-4 w-4 text-muted shrink-0" /><span className="font-medium truncate">{activeModules.find(module => pathname.startsWith(module.route))?.label || (pathname === '/settings' ? 'Negocio' : pathname.startsWith('/superadmin') ? 'SuperAdmin' : 'Dashboard')}</span></div>
          </div>
          <div className="flex items-center gap-3 shrink-0"><NotificationBell /><span className="hidden sm:flex h-9 w-9 rounded-full bg-indigo-50 text-primary items-center justify-center text-sm font-semibold" aria-label={me?.user?.firstName || 'Mi cuenta'}>{(me?.user?.firstName || me?.user?.email || 'N').slice(0,1).toUpperCase()}</span></div>
        </header>
        <div id="workspace-content" tabIndex={-1} className="flex-1 min-h-0 min-w-0 overflow-y-auto p-4 sm:p-6 lg:p-8"><Outlet /></div>
      </main>
    </div>
  );
};
