import { Button } from '../../../components/ui/Button';
import { Select, Input, Textarea, FormField } from '../../../components/ui/Form';
import { Modal } from '../../../components/ui/Modal';
import { useToast } from '../../../components/ui/Toast';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { usePermissions } from '../../../core/auth/permissions';
import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Save } from 'lucide-react';
import { createRequest } from '../services/request.service';
import type { RequestType } from '../types/request.types';

interface CreateRequestModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export const CreateRequestModal = ({ isOpen, onClose }: CreateRequestModalProps) => {
  const toast = useToast();
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const { can } = usePermissions();

  const [formData, setFormData] = useState({
    type: 'Tramite' as RequestType,
    title: '',
    description: '',
    consumerPhone: ''
  });

  const createMutation = useSessionMutation({
    mutationFn: createRequest,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.requests.lists(workspaceId) });
      toast.success('Solicitud creada.');
      onClose();
      setFormData({ type: 'Tramite', title: '', description: '', consumerPhone: '' });
    },
    onError: (error: unknown) => {
      toast.toastApiError(error);
    }
  });

  if (!isOpen) return null;

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!can('REQUESTS', 'CREATE')) return;
    createMutation.mutate({
      ...formData,
      conversationId: 'MANUAL_ENTRY'
    });
  };

  return (
    <Modal isOpen={isOpen} onClose={onClose} title="Nueva Solicitud Manual" size="lg" closeDisabled={createMutation.isPending}>
        <form onSubmit={handleSubmit} className="space-y-4">
          <FormField label="Tipo de Solicitud">
            <Select
              value={formData.type}
              onChange={e => setFormData({...formData, type: e.target.value as RequestType})}
              className="w-full border focus:ring-primary text-sm"
            >
              <option value="Tramite">Trámite Administrativo</option>
              <option value="CommercialInquiry">Consulta Comercial</option>
              <option value="Support">Soporte Técnico</option>
              <option value="Other">Otro / General</option>
            </Select>
          </FormField>

          <FormField label="Teléfono del Cliente (Referencia)">
            <Input
              type="text"
              value={formData.consumerPhone}
              onChange={e => setFormData({...formData, consumerPhone: e.target.value})}
              placeholder="Ej: +51987654321"
              className="w-full border focus:ring-primary text-sm"
              required
            />
          </FormField>

          <FormField label="Título corto">
            <Input
              type="text"
              value={formData.title}
              onChange={e => setFormData({...formData, title: e.target.value})}
              placeholder="Ej: Solicitud de cotización mayorista"
              className="w-full border focus:ring-primary text-sm"
              required
            />
          </FormField>

          <FormField label="Descripción detallada">
            <Textarea
              rows={4}
              value={formData.description}
              onChange={e => setFormData({...formData, description: e.target.value})}
              placeholder="Describe el requerimiento del cliente..."
              className="w-full border focus:ring-primary text-sm resize-none"
              required
            />
          </FormField>

          <div className="pt-4 flex justify-end space-x-3 border-t border-line">
            <Button variant="secondary" type="button" disabled={createMutation.isPending} onClick={onClose} className="text-sm font-medium transition-colors">
              Cancelar
            </Button>
            <Button variant="primary" isLoading={createMutation.isPending} type="submit" disabled={createMutation.isPending || !can('REQUESTS', 'CREATE')} className="flex items-center text-sm font-medium disabled:opacity-50 transition-colors">
              <Save aria-hidden="true" className="w-4 h-4 mr-2" />
              {createMutation.isPending ? 'Guardando...' : 'Crear Solicitud'}
            </Button>
          </div>
        </form>
    </Modal>
  );
};
