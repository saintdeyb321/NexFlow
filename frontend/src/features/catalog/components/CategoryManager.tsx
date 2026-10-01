import { Button } from '../../../components/ui/Button';
import { FormField, Input, Select } from '../../../components/ui/Form';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { useToast } from '../../../components/ui/Toast';
import { Alert, LoadingState, EmptyState } from '../../../components/ui/Feedback';
import { queryPolicies } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Modal } from '../../../components/ui/Modal';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { usePermissions } from '../../../core/auth/permissions';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { getCategories, saveCategory, deleteCategory } from '../services/catalog.service';
import type { BusinessCategoryDto } from '../../shared/types/business-offering.types';

type Scope = BusinessCategoryDto['scope'];

export const CategoryManager = ({ scope, onClose }: { scope: 'PRODUCT' | 'SERVICE'; onClose: () => void }) => {
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const { can } = usePermissions();
  const toast = useToast();
  const queryClient = useQueryClient();
  const empty = (): BusinessCategoryDto => ({ name: '', description: '', scope, isActive: true, displayOrder: 0 });
  const [form, setForm] = useState<BusinessCategoryDto>(empty);
  const [deleteId, setDeleteId] = useState<string | null>(null);
  const allowed = (categoryScope: Scope, capability: string) => categoryScope === 'SHARED'
    ? can('CATALOG', capability) && can('SERVICES', capability)
    : can(categoryScope === 'PRODUCT' ? 'CATALOG' : 'SERVICES', capability);
  const { data: categories = [], isLoading, error: loadError } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.catalog.categories(workspaceId, scope), queryFn: ({ signal }) => getCategories(scope, signal),
    enabled: Boolean(workspaceId) && can(scope === 'PRODUCT' ? 'CATALOG' : 'SERVICES', 'READ'),
  });
  const refresh = (categoryScope: Scope) => {
    queryClient.invalidateQueries({ queryKey: categoryScope === 'SHARED' ? queryKeys.catalog.allCategories(workspaceId) : queryKeys.catalog.categories(workspaceId, categoryScope) });
    queryClient.invalidateQueries({ queryKey: categoryScope === 'SHARED' ? queryKeys.artifacts.all(workspaceId) : queryKeys.artifacts.byScope(workspaceId, categoryScope) });
  };
  const save = useSessionMutation({ mutationFn: saveCategory, onSuccess: (_, category) => { refresh(category.scope); setForm(empty()); toast.success('Categoría guardada.'); },
    onError: (err: unknown) => toast.toastApiError(err) });
  const remove = useSessionMutation({ mutationFn: (category: BusinessCategoryDto) => deleteCategory(category.id!), onSuccess: (_, category) => { refresh(category.scope); setDeleteId(null); toast.success('Categoría eliminada.'); },
    onError: (err: unknown) => toast.toastApiError(err) });
  const canSave = allowed(form.scope, form.id ? 'UPDATE' : 'CREATE');
  return (
    <Modal isOpen onClose={onClose} title="Categorías" maxWidth="max-w-2xl" closeDisabled={save.isPending || remove.isPending}>
      {loadError && <Alert tone="error" className="mb-4">{getApiErrorPresentation(loadError)}</Alert>}
      {isLoading ? <LoadingState title="Cargando categorías..." /> : <ul className="space-y-3 mb-6">
        {categories.map(category => <li key={category.id} className="flex justify-between items-center border-b pb-2">
          <span>{category.name} · {category.scope} · {category.isActive ? 'Activa' : 'Inactiva'}</span>
          <div className="flex gap-2">
            <Button variant="ghost" disabled={!allowed(category.scope, 'UPDATE')} onClick={() => { setForm(category);  }}>Editar</Button>
            <Button variant="ghost"  disabled={!allowed(category.scope, 'DELETE') || remove.isPending} onClick={() => setDeleteId(category.id ?? null)}>Eliminar</Button>

          </div>
        </li>)}
      </ul>}
      <ConfirmDialog isOpen={deleteId !== null} title="Eliminar categoría" description="¿Eliminar esta categoría?" destructive confirmLabel="Eliminar" isLoading={remove.isPending} confirmDisabled={!categories.some(category => category.id === deleteId && allowed(category.scope, 'DELETE'))} onClose={() => setDeleteId(null)} onConfirm={() => { const category = categories.find(item => item.id === deleteId); if (category?.id && allowed(category.scope, 'DELETE')) remove.mutate(category); }} />
      {!isLoading && !loadError && categories.length === 0 && <EmptyState title="No hay categorías registradas." />}
      <form onSubmit={event => { event.preventDefault(); if (canSave) save.mutate(form); }} className="space-y-3">
        <fieldset disabled={!canSave || save.isPending} className="space-y-3">
          <FormField label="Nombre"><Input required value={form.name} onChange={event => setForm({ ...form, name: event.target.value })} /></FormField>
          <FormField label="Descripción"><Input value={form.description ?? ''} onChange={event => setForm({ ...form, description: event.target.value })} /></FormField>
          <FormField label="Scope"><Select disabled={Boolean(form.id)} value={form.scope} onChange={event => setForm({ ...form, scope: event.target.value as Scope })}>
            <option value={scope}>{scope}</option>
            {(form.scope === 'SHARED' || allowed('SHARED', 'CREATE')) && <option value="SHARED">SHARED</option>}
          </Select></FormField>
          <FormField label="Orden"><Input type="number" value={form.displayOrder} onChange={event => setForm({ ...form, displayOrder: Number(event.target.value) })} /></FormField>
          <label><Input type="checkbox" checked={form.isActive} onChange={event => setForm({ ...form, isActive: event.target.checked })} /> Activa</label>
          <Button variant="primary" isLoading={save.isPending} type="submit">{form.id ? 'Guardar cambios' : 'Crear categoría'}</Button>
        </fieldset>
        {form.id && <Button variant="ghost" type="button" onClick={() => setForm(empty())}>Nueva categoría</Button>}
      </form>
    </Modal>
  );
};
