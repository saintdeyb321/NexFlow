import { Button } from '../../../components/ui/Button';
import { Select, FormField } from '../../../components/ui/Form';
import { useToast } from '../../../components/ui/Toast';
import { Alert } from '../../../components/ui/Feedback';
import { useState } from 'react';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { queryKeys } from '../../../core/query/queryKeys';
import { queryPolicies } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { PackagePlus } from 'lucide-react';
import { Modal } from '../../../components/ui/Modal';
import { assignModuleToWorkspace, getSystemModules } from '../services/admin.service';
import type { WorkspaceSummaryDto } from '../types/admin.types';

interface AssignModuleModalProps {
  workspace: WorkspaceSummaryDto | null;
  onClose: () => void;
}

export const AssignModuleModal = ({ workspace, onClose }: AssignModuleModalProps) => {
  const me = useAuthStore(state => state.me);
  const queryClient = useQueryClient();
  const toast = useToast();
  const [selectedModuleId, setSelectedModuleId] = useState('');
  const { data: modules = [], error: modulesError } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.system.modules(me?.user.id),
    queryFn: ({ signal }) => getSystemModules(signal),
    enabled: !!workspace && me?.user.isSuperAdmin === true,
  });
  const effectiveModuleId = selectedModuleId || modules[0]?.id || '';
  const assignMutation = useSessionMutation({
    mutationFn: ({ workspaceId, moduleId }: { workspaceId: string; moduleId: string }) => assignModuleToWorkspace(workspaceId, moduleId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.system.workspaces(me?.user.id) });
      toast.success('Módulo asignado.');
      onClose();
    },
    onError: error => toast.toastApiError(error),
  });

  if (!workspace) return null;

  return (
    <Modal isOpen={!!workspace} onClose={onClose} title="Asignar Nuevo Módulo" closeDisabled={assignMutation.isPending}>
      <form onSubmit={(e) => { e.preventDefault(); assignMutation.mutate({ workspaceId: workspace.id, moduleId: effectiveModuleId }); }} className="space-y-4">
        <div className="bg-purple-50 p-3 rounded-lg border border-purple-100 mb-4">
          <p className="text-sm text-purple-800">
            Asignando a: <strong>{workspace.name}</strong>
          </p>
        </div>

        <FormField label="Selecciona el Módulo">
          <Select
            value={effectiveModuleId}
            onChange={(e) => setSelectedModuleId(e.target.value)}
            className="w-full px-4 py-2 border border-gray-200 rounded-xl outline-none focus:ring-2 focus:ring-purple-500"
            required
          >
            {modules.map(m => (
              <option key={m.id} value={m.id}>{m.name} ({m.code})</option>
            ))}
          </Select>
        </FormField>

        {modulesError && <Alert tone="error">{getApiErrorPresentation(modulesError)}</Alert>}
        <div className="pt-4 flex justify-end gap-3">
          <Button variant="secondary" type="button" disabled={assignMutation.isPending} onClick={onClose} className="px-4 py-2 text-gray-600 hover:bg-gray-100 rounded-lg text-sm font-medium transition-colors">
            Cancelar
          </Button>
          <Button variant="primary" isLoading={assignMutation.isPending} type="submit" disabled={assignMutation.isPending || !effectiveModuleId} className="flex items-center px-4 py-2 bg-purple-600 text-white rounded-lg text-sm font-medium hover:bg-purple-700 disabled:opacity-50 transition-colors shadow-sm">
            {!assignMutation.isPending && <PackagePlus className="w-4 h-4 mr-2" />}
            Asignar Módulo
          </Button>
        </div>
      </form>
    </Modal>
  );
};
