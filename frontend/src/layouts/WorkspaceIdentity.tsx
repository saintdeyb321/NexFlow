import { RefreshCw } from 'lucide-react';
import { IconButton } from '../components/ui/Button';
import type { CommercialIdentity } from './workspaceNavigation';

export const WorkspaceIdentity = ({ identity, dark = false, fetching, onRetry }: {
  identity: CommercialIdentity; dark?: boolean; fetching: boolean; onRetry: () => void;
}) => <div className="flex items-center gap-2 min-w-0" data-business-identity>
  <span title={identity.label} role={identity.status === 'loading' ? 'status' : undefined}
    className={`truncate font-semibold ${dark ? 'text-white text-base' : 'text-foreground text-sm sm:text-base'} ${identity.status === 'loading' ? 'animate-pulse' : ''}`}>{identity.label}</span>
  {identity.status === 'error' && <IconButton label="Reintentar carga del negocio" title="Reintentar carga del negocio" disabled={fetching} onClick={onRetry}
    className={`shrink-0 ${dark ? 'text-slate-300 hover:text-white hover:bg-sidebar-hover' : ''}`}><RefreshCw aria-hidden="true" className="w-4 h-4" /></IconButton>}
</div>;
