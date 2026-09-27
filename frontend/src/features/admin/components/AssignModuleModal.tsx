import { useState, useEffect } from 'react';
import { useMutation} from '@tanstack/react-query';
import { PackagePlus, Loader2 } from 'lucide-react';
import { Modal } from '../../../components/ui/Modal';
import { assignModuleToWorkspace, getSystemModules } from '../services/admin.service';
import type { WorkspaceSummaryDto } from '../types/admin.types';

interface AssignModuleModalProps {
  workspace: WorkspaceSummaryDto | null;
  onClose: () => void;
}

export const AssignModuleModal = ({ workspace, onClose }: AssignModuleModalProps) => {
  const [modules, setModules] = useState<any[]>([]);
  const [selectedModuleId, setSelectedModuleId] = useState<string>('');

  useEffect(() => {
    if (workspace) {
      getSystemModules().then(res => {
        setModules(res);
        if (res.length > 0) setSelectedModuleId(res[0].id);
      }).catch(console.error);
    }
  }, [workspace]);

  const assignMutation = useMutation({
    mutationFn: () => assignModuleToWorkspace(workspace!.id, selectedModuleId),
    onSuccess: () => {
      alert("Módulo asignado con éxito.");
      onClose();
    },
    onError: (error: any) => {
      alert(`Error al asignar módulo: ${error.response?.data?.message || 'Error desconocido'}`);
    }
  });

  if (!workspace) return null;

  return (
    <Modal isOpen={!!workspace} onClose={onClose} title="Asignar Nuevo Módulo">
      <form onSubmit={(e) => { e.preventDefault(); assignMutation.mutate(); }} className="space-y-4">
        <div className="bg-purple-50 p-3 rounded-lg border border-purple-100 mb-4">
          <p className="text-sm text-purple-800">
            Asignando a: <strong>{workspace.name}</strong>
          </p>
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Selecciona el Módulo</label>
          <select 
            value={selectedModuleId} 
            onChange={(e) => setSelectedModuleId(e.target.value)}
            className="w-full px-4 py-2 border border-gray-200 rounded-xl outline-none focus:ring-2 focus:ring-purple-500"
            required
          >
            {modules.map(m => (
              <option key={m.id} value={m.id}>{m.name} ({m.code})</option>
            ))}
          </select>
        </div>

        <div className="pt-4 flex justify-end gap-3">
          <button type="button" onClick={onClose} className="px-4 py-2 text-gray-600 hover:bg-gray-100 rounded-lg text-sm font-medium transition-colors">
            Cancelar
          </button>
          <button type="submit" disabled={assignMutation.isPending || !selectedModuleId} className="flex items-center px-4 py-2 bg-purple-600 text-white rounded-lg text-sm font-medium hover:bg-purple-700 disabled:opacity-50 transition-colors shadow-sm">
            {assignMutation.isPending ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : <PackagePlus className="w-4 h-4 mr-2" />}
            Asignar Módulo
          </button>
        </div>
      </form>
    </Modal>
  );
};