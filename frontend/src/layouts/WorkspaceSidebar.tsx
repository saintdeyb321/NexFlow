import { Link } from 'react-router-dom';
import { Layers, LogOut, MapPin } from 'lucide-react';
import { Button } from '../components/ui/Button';
import type { MeResponse } from '../core/types/auth.types';
import type { LocationDto } from '../features/business/types/business.types';
import { WorkspaceIdentity } from './WorkspaceIdentity';
import { matchesNavigationRoute } from './workspaceNavigation';
import type { CommercialIdentity, NavigationGroup } from './workspaceNavigation';

interface WorkspaceSidebarProps {
  me: MeResponse | null; identity: CommercialIdentity; fetching: boolean; onRetry: () => void;
  groups: NavigationGroup[]; pathname: string; onNavigate: () => void; onLogout: () => void;
  locations: LocationDto[]; canReadLocations: boolean; selectedLocationId: string; selectorId: string; onSelectLocation: (id: string) => void;
}
export const WorkspaceSidebar = ({ me, identity, fetching, onRetry, groups, pathname, onNavigate, onLogout, locations, canReadLocations, selectedLocationId, selectorId, onSelectLocation }: WorkspaceSidebarProps) => {
  const accountName = [me?.user.firstName, me?.user.lastName].filter(Boolean).join(' ') || 'Mi cuenta';
  return <div className="flex flex-col h-full min-h-0 overflow-hidden">
    <Link to="/" onClick={onNavigate} className="hidden lg:flex h-16 items-center gap-3 px-5 shrink-0 text-white">
      <span className="w-9 h-9 rounded-xl bg-primary flex items-center justify-center"><Layers aria-hidden="true" className="w-5 h-5" /></span>
      <span className="font-semibold text-xl tracking-tight">NexFlow<span className="text-accent">.</span></span>
    </Link>
    <div className="px-5 py-4 border-y border-white/10 shrink-0 space-y-3">
      <div><p className="text-[10px] font-semibold text-slate-400 uppercase tracking-[.15em] mb-1">{me?.user.isSuperAdmin ? 'Consola global' : 'Tu negocio'}</p>
        <WorkspaceIdentity identity={identity} dark fetching={fetching} onRetry={onRetry} /></div>
      {me?.workspace && <div><label htmlFor={selectorId} className="text-xs text-slate-400 mb-2 block">Sede activa</label>
        <div className="relative"><MapPin aria-hidden="true" className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
          <select id={selectorId} value={selectedLocationId} disabled={!canReadLocations} onChange={event => onSelectLocation(event.target.value)}
            title={!canReadLocations ? 'No tienes permiso para consultar sedes.' : undefined}
            className="nf-control bg-sidebar-hover text-slate-100 border-white/10 pl-9">
            <option value="all">Todas las sedes</option>{locations.filter(location => location.id).map(location => <option key={location.id} value={location.id}>{location.name}</option>)}
          </select>
        </div>
      </div>}
    </div>
    <nav aria-label="Navegación principal" className="flex-1 px-3 py-4 space-y-5 overflow-y-auto min-h-0 overscroll-contain">
      {groups.map(group => <div key={group.label} role="group" aria-label={group.label}>
        <p className="px-4 mb-2 text-[10px] font-semibold text-slate-400 uppercase tracking-[.15em]">{group.label}</p>
        <div className="space-y-1">{group.items.map(({ route, label, icon: Icon }) => <Link key={route} to={route} aria-current={matchesNavigationRoute(pathname, route) ? 'page' : undefined}
          onClick={onNavigate} className={`nf-nav-link ${matchesNavigationRoute(pathname, route) ? 'font-semibold' : ''}`}>
          <Icon aria-hidden="true" className="w-5 h-5 shrink-0" /><span className="min-w-0">{label}</span>
        </Link>)}</div>
      </div>)}
    </nav>
    <div className="px-4 py-3 border-t border-white/10 shrink-0" aria-label="Mi cuenta">
      <div className="flex items-center gap-3 mb-2 px-2">
        <span className="h-9 w-9 shrink-0 rounded-full bg-sidebar-hover flex items-center justify-center text-sm font-semibold text-indigo-200" aria-hidden="true">{(me?.user.firstName || me?.user.email || 'N').slice(0, 1).toUpperCase()}</span>
        <div className="min-w-0"><p className="text-[10px] text-slate-400 mb-0.5">Mi cuenta</p><p title={accountName} className="text-sm font-medium text-slate-100 truncate">{accountName}</p><p title={me?.user.email} className="text-xs text-slate-400 truncate">{me?.user.email}</p></div>
      </div>
      <Button variant="ghost" onClick={onLogout} className="w-full justify-start text-slate-300 hover:text-white hover:bg-sidebar-hover"><LogOut aria-hidden="true" className="w-4 h-4" />Cerrar sesión</Button>
    </div>
  </div>;
};
