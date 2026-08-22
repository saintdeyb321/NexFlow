import { Outlet, Link, useLocation } from 'react-router-dom';
import { useAuthStore } from '../core/store/useAuthStore';
import { LayoutDashboard, BookOpen, Calendar, Settings, LogOut, Scissors, ShieldAlert, MessageCircle } from 'lucide-react';

export const WorkspaceLayout = () => {
  const { me, logout } = useAuthStore();
  const { pathname } = useLocation();

  const entitlements = me?.entitlements || [];
  
  // CORRECCIÓN: Ahora la autorización viene firmada por el backend, cero correos estáticos.
  const isSuperAdmin = me?.user?.isSuperAdmin === true;

  const navItemClass = (path: string) => 
    `flex items-center px-4 py-3 mb-1 rounded-lg transition-colors ${
      pathname === path || pathname.startsWith(`${path}/`)
        ? 'bg-blue-50 text-blue-700 font-medium' 
        : 'text-gray-600 hover:bg-gray-50'
    }`;

  return (
    <div className="flex h-screen bg-gray-50">
      {/* Sidebar */}
      <aside className="w-64 bg-white border-r border-gray-200 flex flex-col">
        <div className="h-16 flex items-center px-6 border-b border-gray-200">
          <span className="font-bold text-xl text-blue-600 tracking-tight">NexFlow</span>
        </div>
        
        <nav className="flex-1 p-4 overflow-y-auto">
          {/* Dashboard (Siempre visible) */}
          <Link to="/" className={navItemClass('/')}>
            <LayoutDashboard className="w-5 h-5 mr-3" /> Dashboard
          </Link>

          {/* RENDERIZADO MODULAR DICTADO POR LA LICENCIA */}
          {entitlements.includes('RESERVATIONS') && (
            <Link to="/reservations" className={navItemClass('/reservations')}>
              <Calendar className="w-5 h-5 mr-3" /> Reservas
            </Link>
          )}
          
          {entitlements.includes('CONVERSATIONS') && (
            <Link to="/inbox" className={navItemClass('/inbox')}>
              <MessageCircle className="w-5 h-5 mr-3" /> Mensajes
            </Link>
          )}

          {entitlements.includes('FAQ') && (
            <Link to="/faqs" className={navItemClass('/faqs')}>
              <BookOpen className="w-5 h-5 mr-3" /> Conocimiento (FAQ)
            </Link>
          )}

          {/* CORRECCIÓN: Servicios también es un módulo y debe estar protegido */}
          {entitlements.includes('SERVICES') && (
            <Link to="/services" className={navItemClass('/services')}>
              <Scissors className="w-5 h-5 mr-3" /> Servicios
            </Link>
          )}

          
          
          <div className="mt-8 mb-2 px-4 text-xs font-semibold text-gray-400 uppercase tracking-wider">
            Administración
          </div>
          
          {/* Configuraciones globales del Workspace (Módulo Base) */}
          <Link to="/settings" className={navItemClass('/settings')}>
            <Settings className="w-5 h-5 mr-3" /> Negocio
          </Link>
          
        </nav>

        {/* User Profile & Logout */}
        <div className="p-4 border-t border-gray-200 bg-gray-50">
          <div className="mb-3 px-2">
            <p className="text-sm font-semibold text-gray-800 truncate">{me?.workspace?.name || 'Configurando...'}</p>
            <p className="text-xs text-gray-500 truncate">{me?.user?.email}</p>
          </div>
          
          {isSuperAdmin && (
            <Link to="/superadmin" className="w-full flex items-center px-4 py-2 mb-2 text-sm font-medium text-purple-700 bg-purple-50 hover:bg-purple-100 rounded-lg transition-colors">
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

      {/* Main Content Area */}
      <main className="flex-1 overflow-y-auto">
        <div className="p-8 max-w-7xl mx-auto">
          <Outlet />
        </div>
      </main>
    </div>
  );
};