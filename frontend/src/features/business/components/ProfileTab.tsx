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

  const inputClass = isEditing
    ? "w-full border rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-blue-500 bg-white"
    : "w-full border border-transparent rounded-lg px-3 py-2 text-sm outline-none bg-gray-50 text-gray-700 cursor-not-allowed";

  if (isLoading) return <LoadingState title="Cargando perfil..." />;
  if (error || !profile) return <ErrorState onRetry={() => void refetch()} />;

  return (
    <div className="bg-white rounded-xl shadow-sm border border-gray-200 overflow-hidden relative">
      <div className="px-6 py-4 border-b border-gray-200 bg-gray-50 flex justify-between items-center">
        <h2 className="text-lg font-bold text-gray-800">Información General</h2>
        {!isEditing && (
          <Button variant="ghost" disabled={!can('BUSINESS_PROFILE', 'UPDATE')} onClick={() => setProfile(profile)} className="flex items-center text-sm text-blue-600 hover:text-blue-800 font-medium bg-blue-50 hover:bg-blue-100 px-3 py-1.5 rounded-lg transition-colors">
            <Pencil className="w-4 h-4 mr-2" /> Editar Perfil
          </Button>
        )}
      </div>

      <form onSubmit={handleSubmit} className="p-6 space-y-5">
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
          <div className="flex justify-end pt-4 gap-3 border-t mt-4">
            <Button variant="secondary" type="button" onClick={handleCancel} className="px-5 py-2 flex items-center bg-gray-100 text-gray-600 text-sm font-medium rounded-lg hover:bg-gray-200 transition-colors">
              <X className="w-4 h-4 mr-1" /> Cancelar
            </Button>
            <Button variant="primary" isLoading={isSaving} type="submit" disabled={isSaving || !can('BUSINESS_PROFILE', 'UPDATE')} className="px-5 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:opacity-50 transition-colors shadow-sm">
              {isSaving ? 'Guardando...' : 'Guardar Cambios'}
            </Button>
          </div>
        )}
      </form>
    </div>
  );
};
