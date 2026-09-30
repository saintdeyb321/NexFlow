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
  ShieldAlert, MessageCircle, Package, ClipboardList, MapPin, ShoppingBag, Menu, X 
} from 'lucide-react';
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

  const navItemClass = (path: string) => 
    `flex items-center px-4 py-3 mb-1 rounded-lg transition-colors ${
      pathname === path || (path !== '/' && pathname.startsWith(`${path}/`))
        ? 'bg-blue-50 text-blue-700 font-medium' 
        : 'text-gray-600 hover:bg-gray-50'
    }`;

  const activeModules = Object.keys(MODULE_REGISTRY)
    .filter(code => can(code, 'READ'))
    .map((code: string) => ({ code, ...MODULE_REGISTRY[code] }));

  const SidebarContent = () => (
    <>
      <div className="h-16 flex items-center px-6 border-b border-gray-200 shrink-0">
        <span className="font-bold text-xl text-blue-600 tracking-tight">NexFlow</span>
      </div>
      
      <div className="p-4 border-b border-gray-100 bg-gray-50/50 shrink-0">
        <label className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2 block">Sede Activa</label>
        <div className="relative">
          <MapPin className="w-4 h-4 absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400" />
          <select
            value={selectedLocationId}
            onChange={(e) => setSelectedLocationId(e.target.value)}
            className="w-full pl-9 pr-8 py-2 bg-white border border-gray-200 rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 appearance-none cursor-pointer shadow-sm text-gray-700 font-medium"
          >
            <option value="all">Todas las sedes</option>
            {locations.map((loc) => (
              <option key={loc.id} value={loc.id}>{loc.name}</option>
            ))}
          </select>
        </div>
      </div>

      <nav className="flex-1 p-4 overflow-y-auto custom-scrollbar">
        {can('CONVERSATIONS', 'READ') && <Link to="/" onClick={() => setIsMobileMenuOpen(false)} className={navItemClass('/')}>
          <LayoutDashboard className="w-5 h-5 mr-3" /> Dashboard
        </Link>}

        {activeModules.map(({ code, route, label, icon: Icon }: { code: string, route: string, label: string, icon: React.ElementType }) => (
          <Link key={code} to={route} onClick={() => setIsMobileMenuOpen(false)} className={navItemClass(route)}>
            <Icon className="w-5 h-5 mr-3" /> {label}
          </Link>
        ))}
        
        <div className="mt-8 mb-2 px-4 text-xs font-semibold text-gray-400 uppercase tracking-wider">
          Administración
        </div>
        
        {canReadSettings && <Link to="/settings" onClick={() => setIsMobileMenuOpen(false)} className={navItemClass('/settings')}>
          <Settings className="w-5 h-5 mr-3" /> Negocio
        </Link>}
      </nav>

      <div className="p-4 border-t border-gray-200 bg-gray-50 shrink-0">
        <div className="mb-3 px-2">
          <p className="text-sm font-semibold text-gray-800 truncate">
            {isSuperAdmin ? 'Administración Global' : (me?.workspace?.name || 'Configurando...')}
          </p>
          <p className="text-xs text-gray-500 truncate">{me?.user?.email}</p>
        </div>
        
        {isSuperAdmin && (
          <Link 
            to="/superadmin" 
            onClick={() => setIsMobileMenuOpen(false)}
            className={`w-full flex items-center px-4 py-2 mb-2 text-sm font-medium rounded-lg transition-colors ${
              pathname.startsWith('/superadmin') 
                ? 'bg-purple-600 text-white shadow-sm' 
                : 'text-purple-700 bg-purple-50 hover:bg-purple-100'
            }`}
          >
            <ShieldAlert className="w-4 h-4 mr-2" /> Consola SuperAdmin
          </Link>
        )}

        <button 
          onClick={logout} 
          className="w-full flex items-center px-4 py-2 text-sm font-medium text-red-600 hover:bg-red-50 rounded-lg transition-colors"
        >
          <LogOut className="w-4 h-4 mr-2" /> Cerrar Sesión
        </button>
      </div>
    </>
  );

  return (
    <div className="flex h-[100dvh] bg-gray-50 overflow-hidden">
      {/* Desktop Sidebar */}
      <aside className="hidden md:flex w-64 bg-white border-r border-gray-200 flex-col z-20 shrink-0 h-full">
        <SidebarContent />
      </aside>

      {/* Mobile Drawer */}
      {isMobileMenuOpen && (
        <div className="md:hidden fixed inset-0 z-50 flex">
          <div className="fixed inset-0 bg-gray-900/80 backdrop-blur-sm" onClick={() => setIsMobileMenuOpen(false)} />
          <aside className="relative flex w-4/5 max-w-sm flex-col bg-white h-full animate-in slide-in-from-left">
            <button onClick={() => setIsMobileMenuOpen(false)} className="absolute right-4 top-4 p-2 text-gray-500 hover:bg-gray-100 rounded-full">
              <X className="w-6 h-6" />
            </button>
            <SidebarContent />
          </aside>
        </div>
      )}

      <main className="flex-1 flex flex-col h-full overflow-hidden relative">
        <header className="h-16 bg-white border-b border-gray-200 flex items-center justify-between px-4 md:px-8 shadow-sm shrink-0 z-10">
          <div className="flex items-center md:hidden">
            <button onClick={() => setIsMobileMenuOpen(true)} className="p-2 -ml-2 text-gray-600 hover:bg-gray-100 rounded-lg">
              <Menu className="w-6 h-6" />
            </button>
            <span className="font-bold text-lg text-blue-600 ml-2">NexFlow</span>
          </div>
          <div className="flex-1 flex justify-end">
             <NotificationBell />
          </div>
        </header>

        <div className="flex-1 overflow-y-auto p-4 md:p-8">
          <Outlet />
        </div>
      </main>
    </div>
  );
};
