import { Button } from '../../../components/ui/Button';
import { Select, FormField } from '../../../components/ui/Form';
import { useToast } from '../../../components/ui/Toast';

import { useAuthStore } from '../../../core/store/useAuthStore';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Calendar } from 'lucide-react';
import { Modal } from '../../../components/ui/Modal';
import { renewWorkspaceLicense } from '../services/admin.service';
import type { WorkspaceSummaryDto } from '../types/admin.types';

interface RenewLicenseModalProps {
  workspace: WorkspaceSummaryDto | null;
  onClose: () => void;
}

export const RenewLicenseModal = ({ workspace, onClose }: RenewLicenseModalProps) => {
  const queryClient = useQueryClient();
  const toast = useToast();
  const userId = useAuthStore(state => state.me?.user.id);
  const [duration, setDuration] = useState<number>(1);

  const renewMutation = useSessionMutation({
    mutationFn: () => renewWorkspaceLicense(workspace!.id, duration),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.system.workspaces(userId) });
      toast.success('Licencia renovada.');
      onClose();
    },
    onError: error => toast.toastApiError(error),
  });

  if (!workspace) return null;

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    renewMutation.mutate();
  };

  return (
    <Modal isOpen={!!workspace} onClose={onClose} title="Renovar Licencia" closeDisabled={renewMutation.isPending}>
      <form onSubmit={handleSubmit} className="space-y-4">
        <div className="bg-purple-50 p-3 rounded-lg border border-purple-100 mb-4">
          <p className="text-sm text-purple-800">
            Extendiendo licencia para: <strong>{workspace.name}</strong>
          </p>
        </div>

        <FormField label="Duración de la extensión">
          <Select
            value={duration}
            onChange={(e) => setDuration(Number(e.target.value))}
            className="w-full border focus:ring-purple-500"
          >
            <option value={1}>1 Mes</option>
            <option value={3}>3 Meses (Trimestre)</option>
            <option value={6}>6 Meses (Semestre)</option>
            <option value={12}>12 Meses (Anual)</option>
          </Select>
        </FormField>

        <div className="pt-4 flex justify-end gap-3">
          <Button variant="secondary" type="button" disabled={renewMutation.isPending} onClick={onClose} className="text-sm font-medium transition-colors">
            Cancelar
          </Button>
          <Button variant="primary" isLoading={renewMutation.isPending} type="submit" disabled={renewMutation.isPending} className="flex items-center text-sm font-medium disabled:opacity-50 transition-colors">
            {!renewMutation.isPending && <Calendar aria-hidden="true" className="w-4 h-4 mr-2" />}
            Confirmar Renovación
          </Button>
        </div>
      </form>
    </Modal>
  );
};
