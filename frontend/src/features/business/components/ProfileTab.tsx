import { Button } from '../../../components/ui/Button';
import { Input, Textarea, FormField } from '../../../components/ui/Form';
import { useToast } from '../../../components/ui/useToast';
import { LoadingState, ErrorState, EmptyState } from '../../../components/ui/Feedback';

import { useAuthStore } from '../../../core/store/useAuthStore';
import { usePermissions, can as hasCapability } from '../../../core/auth/permissions';
import { useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../../core/query/queryKeys';
import { getQuerySession, isCurrentQuerySession } from '../../../core/query/queryPersistence';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { Pencil, X } from 'lucide-react';
import { updateBusinessProfile } from '../services/business.service';
import { useBusinessProfile } from '../hooks/useBusinessProfile';
import type { BusinessProfile } from '../types/business.types';

export const ProfileTab = () => {
  const { can } = usePermissions();
  const me = useAuthStore(state => state.me);
  if (!me?.workspace || !can('BUSINESS_PROFILE', 'READ')) return <EmptyState title="Perfil no disponible" description="No tienes permiso para consultar el perfil del negocio." />;
  return <ProfileEditor key={JSON.stringify([me.user.id, me.workspace.id, getQuerySession()])} workspaceId={me.workspace.id} userId={me.user.id} />;
};

const ProfileEditor = ({ workspaceId, userId }: { workspaceId: string; userId: string }) => {
  const toast = useToast();
  const { can } = usePermissions();
  const queryClient = useQueryClient();
  const [draft, setProfile] = useState<BusinessProfile | null>(null);
  const isEditing = draft !== null;
  const { data, isLoading, error, refetch } = useBusinessProfile();
  const submitting = useRef(false);
  const session = getQuerySession();
  const profile = draft ?? data;
  const saveMutation = useSessionMutation({
    mutationFn: (next: BusinessProfile) => {
      const me = useAuthStore.getState().me;
      if (me?.workspace?.id !== workspaceId || me.user.id !== userId || !hasCapability(me, 'BUSINESS_PROFILE', 'UPDATE'))
        return Promise.reject(new Error('No tienes permiso para guardar el perfil.'));
      return updateBusinessProfile(next);
    },
    onSuccess: async (_, saved) => {
      const queryKey = queryKeys.business.profile(workspaceId);
      await queryClient.cancelQueries({ queryKey, exact: true });
      if (!isCurrentQuerySession(session)) return;
      queryClient.setQueryData(queryKey, { ...saved });
      setProfile(null);
      toast.success('Perfil actualizado correctamente');
      await queryClient.invalidateQueries({ queryKey, exact: true });
    },
    onError: error => toast.toastApiError(error),
    onSettled: () => { submitting.current = false; },
  });
  const isSaving = saveMutation.isPending;
  const handleCancel = () => setProfile(null);
  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (submitting.current || !isEditing) return;
    const me = useAuthStore.getState().me;
    if (profile && me?.workspace?.id === workspaceId && me.user.id === userId && hasCapability(me, 'BUSINESS_PROFILE', 'UPDATE')) {
      submitting.current = true;
      saveMutation.mutate({ ...profile });
    }
  };

  const inputClass = isEditing ? '' : 'bg-surface-soft';

  if (isLoading) return <LoadingState title="Cargando perfil..." />;
  if (error || !profile) return <ErrorState onRetry={() => void refetch()} />;

  return (
    <div className="bg-surface rounded-xl shadow-sm border border-line overflow-hidden relative">
      <div className="px-5 sm:px-6 py-4 border-b border-line bg-surface-soft flex flex-wrap gap-3 justify-between items-center">
        <h2 className="text-lg font-bold text-foreground">Identidad y contacto</h2>
        {!isEditing && (
          <Button variant="ghost" disabled={isSaving || !can('BUSINESS_PROFILE', 'UPDATE')} onClick={() => setProfile({ ...profile })} className="flex items-center text-sm font-medium transition-colors">
            <Pencil aria-hidden="true" className="w-4 h-4 mr-2" /> Editar Perfil
          </Button>
        )}
      </div>

      <form onSubmit={handleSubmit} className="p-5 sm:p-6 space-y-6">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
          <FormField label="Nombre Comercial">
            <Input type="text" disabled={isSaving || !isEditing || !can('BUSINESS_PROFILE', 'UPDATE')} value={profile.commercialName} onChange={e => setProfile({...profile, commercialName: e.target.value})} required className={inputClass} />
          </FormField>
          <FormField label="RUC / Identificador Fiscal">
            <Input type="text" disabled={isSaving || !isEditing || !can('BUSINESS_PROFILE', 'UPDATE')} value={profile.taxId} onChange={e => setProfile({...profile, taxId: e.target.value})} required className={inputClass} />
          </FormField>
          <FormField label="Correo de Contacto">
            <Input type="email" disabled={isSaving || !isEditing || !can('BUSINESS_PROFILE', 'UPDATE')} value={profile.contactEmail} onChange={e => setProfile({...profile, contactEmail: e.target.value})} required className={inputClass} />
          </FormField>
          <FormField label="WhatsApp">
            <Input type="text" disabled={isSaving || !isEditing || !can('BUSINESS_PROFILE', 'UPDATE')} value={profile.whatsAppNumber} onChange={e => setProfile({...profile, whatsAppNumber: e.target.value})} required className={inputClass} />
          </FormField>
        </div>

        <FormField label="Descripción del Negocio">
          <Textarea disabled={isSaving || !isEditing || !can('BUSINESS_PROFILE', 'UPDATE')} value={profile.description} onChange={e => setProfile({...profile, description: e.target.value})} rows={3} className={inputClass} />
        </FormField>

        {isEditing && (
          <div className="flex flex-wrap justify-end pt-4 gap-3 border-t mt-4">
            <Button variant="secondary" type="button" disabled={isSaving} onClick={handleCancel} className="flex items-center text-sm font-medium transition-colors">
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
