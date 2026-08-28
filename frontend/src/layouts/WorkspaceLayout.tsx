import { Outlet, Link, useLocation } from 'react-router-dom';
import { useAuthStore } from '../core/store/useAuthStore';
import { 
  LayoutDashboard, BookOpen, Calendar, Settings, LogOut, Scissors, 
  ShieldAlert, MessageCircle, Package, ClipboardList 
} from 'lucide-react';

// 🔥 SPRINT 2 (Auditoría #8): Eliminadas las rutas fantasmas (CUSTOMERS, NOTIFICATIONS)
const MODULE_REGISTRY: Record<string, { route: string; label: string; icon: React.ElementType }> = {
  'RESERVATIONS': { route: '/reservations', label: 'Reservas', icon: Calendar },
  'CONVERSATIONS': { route: '/inbox', label: 'Mensajes', icon: MessageCircle },
  'FAQ': { route: '/faqs', label: 'Base (FAQ)', icon: BookOpen },
  'SERVICES': { route: '/services', label: 'Servicios', icon: Scissors },
  'CATALOG': { route: '/catalog', label: 'Catálogo', icon: Package },
  'REQUESTS': { route: '/requests', label: 'Solicitudes', icon: ClipboardList }
};

export const WorkspaceLayout = () => {
  const { me, logout } = useAuthStore();
  const { pathname } = useLocation();

  const entitlements = me?.entitlements || [];
  const isSuperAdmin = me?.user?.isSuperAdmin === true;

  const navItemClass = (path: string) => 
    `flex items-center px-4 py-3 mb-1 rounded-lg transition-colors ${
      pathname === path || (path !== '/' && pathname.startsWith(`${path}/`))
        ? 'bg-blue-50 text-blue-700 font-medium' 
        : 'text-gray-600 hover:bg-gray-50'
    }`;

  const activeModules = entitlements
    .filter(code => MODULE_REGISTRY[code])
    .map(code => ({ code, ...MODULE_REGISTRY[code] }));

  return (
    <div className="flex h-screen bg-gray-50">
      <aside className="w-64 bg-white border-r border-gray-200 flex flex-col">
        <div className="h-16 flex items-center px-6 border-b border-gray-200">
          <span className="font-bold text-xl text-blue-600 tracking-tight">NexFlow</span>
        </div>
        
        <nav className="flex-1 p-4 overflow-y-auto">
          <Link to="/" className={navItemClass('/')}>
            <LayoutDashboard className="w-5 h-5 mr-3" /> Dashboard
          </Link>

          {activeModules.map(({ code, route, label, icon: Icon }) => (
            <Link key={code} to={route} className={navItemClass(route)}>
              <Icon className="w-5 h-5 mr-3" /> {label}
            </Link>
          ))}
          
          <div className="mt-8 mb-2 px-4 text-xs font-semibold text-gray-400 uppercase tracking-wider">
            Administración
          </div>
          
          <Link to="/settings" className={navItemClass('/settings')}>
            <Settings className="w-5 h-5 mr-3" /> Negocio
          </Link>
        </nav>

        <div className="p-4 border-t border-gray-200 bg-gray-50">
          <div className="mb-3 px-2">
            <p className="text-sm font-semibold text-gray-800 truncate">
              {isSuperAdmin ? 'Administración Global' : (me?.workspace?.name || 'Configurando...')}
            </p>
            <p className="text-xs text-gray-500 truncate">{me?.user?.email}</p>
          </div>
          
          {isSuperAdmin && (
            <Link 
              to="/superadmin" 
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
      </aside>

      <main className="flex-1 overflow-y-auto">
        <div className="p-8 max-w-7xl mx-auto">
          <Outlet />
        </div>
      </main>
    </div>
  );
};