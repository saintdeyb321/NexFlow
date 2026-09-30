import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Calendar, Loader2 } from 'lucide-react';
import { Modal } from '../../../components/ui/Modal';
import { renewWorkspaceLicense } from '../services/admin.service';
import type { WorkspaceSummaryDto } from '../types/admin.types';

interface RenewLicenseModalProps {
  workspace: WorkspaceSummaryDto | null;
  onClose: () => void;
}

export const RenewLicenseModal = ({ workspace, onClose }: RenewLicenseModalProps) => {
  const queryClient = useQueryClient();
  const userId = useAuthStore(state => state.me?.user.id);
  const [duration, setDuration] = useState<number>(1);

  const renewMutation = useSessionMutation({
    mutationFn: () => renewWorkspaceLicense(workspace!.id, duration),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.system.workspaces(userId) });
      onClose();
    }
  });

  if (!workspace) return null;

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    renewMutation.mutate();
  };

  return (
    <Modal isOpen={!!workspace} onClose={onClose} title="Renovar Licencia">
      <form onSubmit={handleSubmit} className="space-y-4">
        <div className="bg-purple-50 p-3 rounded-lg border border-purple-100 mb-4">
          <p className="text-sm text-purple-800">
            Extendiendo licencia para: <strong>{workspace.name}</strong>
          </p>
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Duración de la extensión</label>
          <select 
            value={duration} 
            onChange={(e) => setDuration(Number(e.target.value))}
            className="w-full px-4 py-2 border border-gray-200 rounded-xl outline-none focus:ring-2 focus:ring-purple-500"
          >
            <option value={1}>1 Mes</option>
            <option value={3}>3 Meses (Trimestre)</option>
            <option value={6}>6 Meses (Semestre)</option>
            <option value={12}>12 Meses (Anual)</option>
          </select>
        </div>

        {renewMutation.error && <p role="alert">{getApiErrorPresentation(renewMutation.error)}</p>}
        <div className="pt-4 flex justify-end gap-3">
          <button type="button" onClick={onClose} className="px-4 py-2 text-gray-600 hover:bg-gray-100 rounded-lg text-sm font-medium transition-colors">
            Cancelar
          </button>
          <button type="submit" disabled={renewMutation.isPending} className="flex items-center px-4 py-2 bg-purple-600 text-white rounded-lg text-sm font-medium hover:bg-purple-700 disabled:opacity-50 transition-colors shadow-sm">
            {renewMutation.isPending ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : <Calendar className="w-4 h-4 mr-2" />}
            Confirmar Renovación
          </button>
        </div>
      </form>
    </Modal>
  );
};