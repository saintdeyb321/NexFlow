import { useEffect, useState } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Menu } from 'lucide-react';
import { useAuthStore } from '../core/store/useAuthStore';
import { usePermissions } from '../core/auth/permissions';
import { getQuerySession } from '../core/query/queryPersistence';
import { queryPolicies } from '../core/query/queryPolicies';
import { queryKeys } from '../core/query/queryKeys';
import { getLocations } from '../features/business/services/business.service';
import { useBusinessProfile } from '../features/business/hooks/useBusinessProfile';
import { IconButton } from '../components/ui/Button';
import { Modal } from '../components/ui/Modal';
import { NotificationBell } from '../components/ui/NotificationBell';
import { WorkspaceSidebar } from './WorkspaceSidebar';
import { WorkspaceIdentity } from './WorkspaceIdentity';
import { commercialIdentity, currentNavigationLabel, navigationGroups } from './workspaceNavigation';

export const WorkspaceLayout = () => {
  const me = useAuthStore(state => state.me);
  return <WorkspaceShell key={JSON.stringify([me?.user.id, me?.workspace?.id, getQuerySession()])} />;
};

const WorkspaceShell = () => {
  const { me, logout, selectedLocationId, setSelectedLocationId } = useAuthStore();
  const { pathname } = useLocation();
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);
  const { can } = usePermissions();
  const canReadLocations = can('LOCATIONS', 'READ');
  const isSuperAdmin = me?.user.isSuperAdmin === true;
  const workspaceId = me?.workspace?.id;
  const profile = useBusinessProfile(!isSuperAdmin);
  const identity = commercialIdentity({
    global: isSuperAdmin, authorized: !!workspaceId && can('BUSINESS_PROFILE', 'READ'),
    status: profile.status, name: profile.data?.commercialName,
  });
  const { data: locations = [], isSuccess: locationsLoaded } = useQuery({
    ...queryPolicies.stable, queryKey: queryKeys.locations.all(workspaceId),
    queryFn: ({ signal }) => getLocations(signal), enabled: !!workspaceId && canReadLocations,
  });
  useEffect(() => {
    if (selectedLocationId !== 'all' && (!canReadLocations || (locationsLoaded && !locations.some(location => location.id === selectedLocationId))))
      setSelectedLocationId('all');
  }, [workspaceId, selectedLocationId, canReadLocations, locationsLoaded, locations, setSelectedLocationId]);

  const closeMenu = () => setIsMobileMenuOpen(false);
  const groups = navigationGroups(can, isSuperAdmin);
  const pageLabel = currentNavigationLabel(pathname);
  const accountName = [me?.user.firstName, me?.user.lastName].filter(Boolean).join(' ') || 'Mi cuenta';
  const sidebar = (selectorId: string) => <WorkspaceSidebar me={me} identity={identity} fetching={profile.isFetching} onRetry={() => void profile.refetch()}
    groups={groups} pathname={pathname} onNavigate={closeMenu} onLogout={() => { closeMenu(); void logout(); }}
    locations={canReadLocations ? locations : []} canReadLocations={canReadLocations} selectedLocationId={selectedLocationId} selectorId={selectorId} onSelectLocation={setSelectedLocationId} />;

  return <div className="flex h-dvh bg-background overflow-hidden">
    <a href="#workspace-content" className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-[100] focus:bg-surface focus:p-3 focus:rounded-lg">Ir al contenido</a>
    <aside aria-label="Menú del negocio" className="hidden lg:flex w-64 xl:w-72 bg-sidebar flex-col shrink-0 h-full">{sidebar('workspace-location-desktop')}</aside>
    <Modal isOpen={isMobileMenuOpen} onClose={closeMenu} title="NexFlow" placement="drawer">{sidebar('workspace-location-mobile')}</Modal>
    <main className="flex-1 min-w-0 flex flex-col h-full relative">
      <header className="min-h-18 bg-surface border-b border-line flex items-center justify-between gap-3 px-3 sm:px-6 lg:px-8 py-3 shrink-0">
        <div className="flex items-center min-w-0 gap-2 sm:gap-3">
          <IconButton label="Abrir menú" aria-expanded={isMobileMenuOpen} onClick={() => setIsMobileMenuOpen(true)} className="lg:hidden"><Menu aria-hidden="true" className="w-5 h-5" /></IconButton>
          <nav aria-label="Ruta actual" className="min-w-0">
            <WorkspaceIdentity identity={identity} fetching={profile.isFetching} onRetry={() => void profile.refetch()} />
            <p title={pageLabel} className="text-xs text-muted truncate mt-0.5" data-current-module>{pageLabel}</p>
          </nav>
        </div>
        <div className="flex items-center gap-2 sm:gap-3 shrink-0"><NotificationBell /><span title={accountName} className="hidden sm:flex h-9 w-9 rounded-full bg-indigo-50 text-primary items-center justify-center text-sm font-semibold" aria-label={accountName}>{(me?.user.firstName || me?.user.email || 'N').slice(0, 1).toUpperCase()}</span></div>
      </header>
      <div id="workspace-content" tabIndex={-1} className="flex-1 min-h-0 min-w-0 overflow-y-auto p-4 sm:p-6 lg:p-8"><Outlet /></div>
    </main>
  </div>;
};
