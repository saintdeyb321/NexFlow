import { Building, Ban, Play, Trash2, CalendarClock, Puzzle } from 'lucide-react';
import { Button } from '../../../components/ui/Button';
import { StatusBadge } from '../../../components/ui/Feedback';
import type { WorkspaceSummaryDto } from '../types/admin.types';

interface WorkspaceCardProps {
  workspace: WorkspaceSummaryDto;
  onToggleStatus: (ws: WorkspaceSummaryDto) => void;
  onDelete: (ws: WorkspaceSummaryDto) => void;
  onRenew: (ws: WorkspaceSummaryDto) => void;
  onAssignModule: (ws: WorkspaceSummaryDto) => void;
}
export const WorkspaceCard = ({ workspace, onToggleStatus, onDelete, onRenew, onAssignModule }: WorkspaceCardProps) => {
  const getStatusBadge = (status: number) => {
    switch (status) {
      case 0: return <StatusBadge label="Pendiente" tone="warning" />;
      case 1: return <StatusBadge label="Activo" tone="success" />;
      case 5: return <StatusBadge label="Eliminación en progreso" tone="warning" />;
      case 2: return <StatusBadge label="Suspendido" tone="error" />;
      default: return <StatusBadge label="Desconocido" />;
    }
  };
  return <article className="nf-panel p-5 flex flex-col gap-5 min-w-0">
    <div className="flex flex-wrap justify-between items-start gap-4">
      <div className="flex items-center gap-3 min-w-0"><span aria-hidden="true" className="w-11 h-11 bg-violet-50 rounded-xl text-violet-700 flex items-center justify-center shrink-0"><Building aria-hidden="true" className="w-5 h-5" /></span>
        <div className="min-w-0"><h3 className="font-semibold break-words">{workspace.name}</h3><p className="text-sm text-muted break-all">{workspace.ownerEmail}</p></div>
      </div>
      {getStatusBadge(workspace.status)}
    </div>
    <p className="text-xs text-muted">Creado el {new Date(workspace.createdAt).toLocaleDateString()}</p>
    {workspace.status === 5 && <p className="text-sm text-amber-800 bg-amber-50 rounded-xl px-4 py-3">La eliminación está en curso. Las acciones de este workspace están deshabilitadas.</p>}
    <div className="flex flex-wrap gap-2 pt-4 border-t border-line">
      <Button variant="secondary" disabled={workspace.status === 5} onClick={() => onAssignModule(workspace)}><Puzzle aria-hidden="true" className="w-4 h-4" /> Módulos</Button>
      <Button variant="secondary" disabled={workspace.status === 5} onClick={() => onRenew(workspace)}><CalendarClock aria-hidden="true" className="w-4 h-4" /> Renovar licencia</Button>
      <Button variant="ghost" disabled={workspace.status === 5} onClick={() => onToggleStatus(workspace)}>
        {workspace.status === 2 ? <Play aria-hidden="true" className="w-4 h-4" /> : <Ban aria-hidden="true" className="w-4 h-4" />}{workspace.status === 2 ? 'Reactivar' : 'Suspender'}
      </Button>
      <Button variant="ghost" disabled={workspace.status === 5} onClick={() => onDelete(workspace)} className=""><Trash2 aria-hidden="true" className="w-4 h-4" /> Eliminar</Button>
    </div>
  </article>;
};
