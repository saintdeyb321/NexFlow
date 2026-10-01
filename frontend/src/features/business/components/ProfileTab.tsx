import { Button } from '../../../components/ui/Button';
import { Input, Textarea, FormField } from '../../../components/ui/Form';
import { useToast } from '../../../components/ui/Toast';
import { LoadingState, ErrorState } from '../../../components/ui/Feedback';

import { useAuthStore } from '../../../core/store/useAuthStore';
import { usePermissions } from '../../../core/auth/permissions';
import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../../core/query/queryKeys';
import { queryPolicies } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { Pencil, X } from 'lucide-react';
import { getBusinessProfile, updateBusinessProfile } from '../services/business.service';
import type { BusinessProfile } from '../types/business.types';

export const ProfileTab = () => {
  const toast = useToast();
  const { can } = usePermissions();
  const queryClient = useQueryClient();
  const [draft, setProfile] = useState<BusinessProfile | null>(null);
  const isEditing = draft !== null;
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const { data, isLoading, error, refetch } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.business.profile(workspaceId),
    queryFn: ({ signal }) => getBusinessProfile(signal),
    enabled: Boolean(workspaceId) && can('BUSINESS_PROFILE', 'READ'),
  });
  const profile = draft ?? data;
  const saveMutation = useSessionMutation({
    mutationFn: updateBusinessProfile,
    onSuccess: () => {
      setProfile(null);
      queryClient.invalidateQueries({ queryKey: queryKeys.business.profile(workspaceId) });
      toast.success('Perfil actualizado correctamente');
    },
    onError: error => toast.toastApiError(error),
  });
  const isSaving = saveMutation.isPending;
  const handleCancel = () => setProfile(null);
  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (profile && can('BUSINESS_PROFILE', 'UPDATE')) saveMutation.mutate(profile);
  };

  const inputClass = isEditing ? '' : 'bg-surface-soft';

  if (isLoading) return <LoadingState title="Cargando perfil..." />;
  if (error || !profile) return <ErrorState onRetry={() => void refetch()} />;

  return (
    <div className="bg-surface rounded-xl shadow-sm border border-line overflow-hidden relative">
      <div className="px-5 sm:px-6 py-4 border-b border-line bg-surface-soft flex flex-wrap gap-3 justify-between items-center">
        <h2 className="text-lg font-bold text-foreground">Identidad y contacto</h2>
        {!isEditing && (
          <Button variant="ghost" disabled={!can('BUSINESS_PROFILE', 'UPDATE')} onClick={() => setProfile(profile)} className="flex items-center text-sm font-medium transition-colors">
            <Pencil aria-hidden="true" className="w-4 h-4 mr-2" /> Editar Perfil
          </Button>
        )}
      </div>

      <form onSubmit={handleSubmit} className="p-5 sm:p-6 space-y-6">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
          <FormField label="Nombre Comercial">
            <Input type="text" disabled={!isEditing || !can('BUSINESS_PROFILE', 'UPDATE')} value={profile.commercialName} onChange={e => setProfile({...profile, commercialName: e.target.value})} required className={inputClass} />
          </FormField>
          <FormField label="RUC / Identificador Fiscal">
            <Input type="text" disabled={!isEditing || !can('BUSINESS_PROFILE', 'UPDATE')} value={profile.taxId} onChange={e => setProfile({...profile, taxId: e.target.value})} required className={inputClass} />
          </FormField>
          <FormField label="Correo de Contacto">
            <Input type="email" disabled={!isEditing || !can('BUSINESS_PROFILE', 'UPDATE')} value={profile.contactEmail} onChange={e => setProfile({...profile, contactEmail: e.target.value})} required className={inputClass} />
          </FormField>
          <FormField label="WhatsApp">
            <Input type="text" disabled={!isEditing || !can('BUSINESS_PROFILE', 'UPDATE')} value={profile.whatsAppNumber} onChange={e => setProfile({...profile, whatsAppNumber: e.target.value})} required className={inputClass} />
          </FormField>
        </div>

        <FormField label="Descripción del Negocio">
          <Textarea disabled={!isEditing || !can('BUSINESS_PROFILE', 'UPDATE')} value={profile.description} onChange={e => setProfile({...profile, description: e.target.value})} rows={3} className={inputClass} />
        </FormField>

        {isEditing && (
          <div className="flex flex-wrap justify-end pt-4 gap-3 border-t mt-4">
            <Button variant="secondary" type="button" onClick={handleCancel} className="flex items-center text-sm font-medium transition-colors">
              <X aria-hidden="true" className="w-4 h-4 mr-1" /> Cancelar
            </Button>
            <Button variant="primary" isLoading={isSaving} type="submit" disabled={isSaving || !can('BUSINESS_PROFILE', 'UPDATE')} className="text-sm font-medium disabled:opacity-50 transition-colors">
              {isSaving ? 'Guardando...' : 'Guardar Cambios'}
            </Button>
          </div>
        )}
      </form>
    </div>
  );
};
