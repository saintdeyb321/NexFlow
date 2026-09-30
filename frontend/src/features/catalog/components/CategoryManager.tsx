import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
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
  const queryClient = useQueryClient();
  const empty = (): BusinessCategoryDto => ({ name: '', description: '', scope, isActive: true, displayOrder: 0 });
  const [form, setForm] = useState<BusinessCategoryDto>(empty);
  const [error, setError] = useState<string | null>(null);
  const [deleteId, setDeleteId] = useState<string | null>(null);
  const allowed = (categoryScope: Scope, capability: string) => categoryScope === 'SHARED'
    ? can('CATALOG', capability) && can('SERVICES', capability)
    : can(categoryScope === 'PRODUCT' ? 'CATALOG' : 'SERVICES', capability);
  const { data: categories = [], isLoading, error: loadError } = useQuery({
    queryKey: ['catalogCategories', workspaceId, scope], queryFn: () => getCategories(scope),
    enabled: Boolean(workspaceId) && can(scope === 'PRODUCT' ? 'CATALOG' : 'SERVICES', 'READ'),
  });
  const refresh = () => {
    queryClient.invalidateQueries({ queryKey: ['catalogCategories', workspaceId] });
    queryClient.invalidateQueries({ queryKey: ['serviceCategories', workspaceId] });
  };
  const save = useMutation({ mutationFn: saveCategory, onSuccess: () => { refresh(); setForm(empty()); setError(null); },
    onError: (err: unknown) => setError(getApiErrorPresentation(err)) });
  const remove = useMutation({ mutationFn: deleteCategory, onSuccess: () => { refresh(); setDeleteId(null); setError(null); },
    onError: (err: unknown) => setError(getApiErrorPresentation(err)) });
  const canSave = allowed(form.scope, form.id ? 'UPDATE' : 'CREATE');
  return (
    <Modal isOpen onClose={onClose} title="Categorías" maxWidth="max-w-2xl">
      {(error || loadError) && <p role="alert" className="mb-4 text-red-600">{error || getApiErrorPresentation(loadError)}</p>}
      {isLoading ? <p>Cargando categorías...</p> : <ul className="space-y-3 mb-6">
        {categories.map(category => <li key={category.id} className="flex justify-between items-center border-b pb-2">
          <span>{category.name} · {category.scope} · {category.isActive ? 'Activa' : 'Inactiva'}</span>
          <div className="flex gap-2">
            <button disabled={!allowed(category.scope, 'UPDATE')} onClick={() => { setForm(category); setError(null); }}>Editar</button>
            <button disabled={!allowed(category.scope, 'DELETE') || remove.isPending} onClick={() => setDeleteId(category.id ?? null)}>Eliminar</button>
            {deleteId === category.id && <>
              <button disabled={!allowed(category.scope, 'DELETE') || remove.isPending} onClick={() => category.id && remove.mutate(category.id)}>Confirmar eliminación</button>
              <button onClick={() => setDeleteId(null)}>Volver</button>
            </>}
          </div>
        </li>)}
      </ul>}
      <form onSubmit={event => { event.preventDefault(); if (canSave) save.mutate(form); }} className="space-y-3">
        <fieldset disabled={!canSave || save.isPending} className="space-y-3">
          <label className="block">Nombre<input required value={form.name} onChange={event => setForm({ ...form, name: event.target.value })} className="w-full border rounded-lg p-2" /></label>
          <label className="block">Descripción<input value={form.description ?? ''} onChange={event => setForm({ ...form, description: event.target.value })} className="w-full border rounded-lg p-2" /></label>
          <label className="block">Scope<select disabled={Boolean(form.id)} value={form.scope} onChange={event => setForm({ ...form, scope: event.target.value as Scope })} className="w-full border rounded-lg p-2">
            <option value={scope}>{scope}</option>
            {(form.scope === 'SHARED' || allowed('SHARED', 'CREATE')) && <option value="SHARED">SHARED</option>}
          </select></label>
          <label className="block">Orden<input type="number" value={form.displayOrder} onChange={event => setForm({ ...form, displayOrder: Number(event.target.value) })} className="w-full border rounded-lg p-2" /></label>
          <label><input type="checkbox" checked={form.isActive} onChange={event => setForm({ ...form, isActive: event.target.checked })} /> Activa</label>
          <button type="submit" className="px-4 py-2 bg-blue-600 text-white rounded-lg">{form.id ? 'Guardar cambios' : 'Crear categoría'}</button>
        </fieldset>
        {form.id && <button type="button" onClick={() => setForm(empty())}>Nueva categoría</button>}
      </form>
    </Modal>
  );
};
