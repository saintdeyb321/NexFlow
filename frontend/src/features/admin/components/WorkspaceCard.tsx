import { Building, Ban, Play, Trash2, CalendarClock, Puzzle } from 'lucide-react';
import type { WorkspaceSummaryDto } from '../types/admin.types';

interface WorkspaceCardProps {
  workspace: WorkspaceSummaryDto;
  onToggleStatus: (ws: WorkspaceSummaryDto) => void;
  onDelete: (ws: WorkspaceSummaryDto) => void;
  // 🔥 NUEVOS PROPS
  onRenew?: (ws: WorkspaceSummaryDto) => void;
  onAssignModule?: (ws: WorkspaceSummaryDto) => void;
}

export const WorkspaceCard = ({ workspace, onToggleStatus, onDelete, onRenew, onAssignModule }: WorkspaceCardProps) => {
  const getStatusBadge = (status: number) => {
    switch (status) {
      case 0: return <span className="px-2.5 py-1 rounded-md text-xs font-semibold bg-yellow-100 text-yellow-700">PENDIENTE</span>;
      case 1: return <span className="px-2.5 py-1 rounded-md text-xs font-semibold bg-green-100 text-green-700">ACTIVO</span>;
      case 2: return <span className="px-2.5 py-1 rounded-md text-xs font-semibold bg-red-100 text-red-700">SUSPENDIDO</span>;
      default: return <span className="px-2.5 py-1 rounded-md text-xs font-semibold bg-gray-100 text-gray-700">DESCONOCIDO</span>;
    }
  };

  return (
    <div className="flex flex-col md:flex-row items-center justify-between p-4 bg-white border border-gray-200 rounded-xl hover:border-purple-200 hover:shadow-sm transition-all gap-4">
      <div className="flex items-center w-full md:w-auto">
        <div className="w-12 h-12 bg-purple-50 rounded-lg flex items-center justify-center mr-4 shrink-0">
          <Building className="w-5 h-5 text-purple-600" />
        </div>
        <div className="overflow-hidden">
          <h4 className="font-bold text-gray-900 truncate">{workspace.name}</h4>
          <p className="text-sm text-gray-500 truncate">{workspace.ownerEmail}</p>
        </div>
      </div>

      <div className="flex items-center w-full md:w-auto justify-between md:justify-end gap-4 shrink-0">
        <div className="flex items-center min-w-[100px] justify-center">
          {getStatusBadge(workspace.status)}
        </div>
        
        <div className="flex items-center space-x-1 border-l border-gray-200 pl-4">
          
          {/* 🔥 BOTÓN ASIGNAR MÓDULO */}
          {onAssignModule && (
             <button onClick={() => onAssignModule(workspace)} className="p-2 text-blue-500 hover:bg-blue-50 rounded-lg transition-colors" title="Asignar Módulo Extra">
               <Puzzle className="w-5 h-5" />
             </button>
          )}

          {/* 🔥 BOTÓN RENOVAR */}
          {onRenew && (
             <button onClick={() => onRenew(workspace)} className="p-2 text-indigo-500 hover:bg-indigo-50 rounded-lg transition-colors" title="Renovar Licencia">
               <CalendarClock className="w-5 h-5" />
             </button>
          )}

          <button
            onClick={() => onToggleStatus(workspace)}
            className={`p-2 rounded-lg transition-colors ${workspace.status === 2 ? 'text-green-600 hover:bg-green-50' : 'text-orange-500 hover:bg-orange-50'}`}
            title={workspace.status === 2 ? 'Reactivar Licencia' : 'Suspender Licencia'}
          >
            {workspace.status === 2 ? <Play className="w-5 h-5" /> : <Ban className="w-5 h-5" />}
          </button>
          
          <button
            onClick={() => onDelete(workspace)}
            className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-lg transition-colors"
            title="Eliminar Permanente"
          >
            <Trash2 className="w-5 h-5" />
          </button>
        </div>
      </div>
    </div>
  );
};