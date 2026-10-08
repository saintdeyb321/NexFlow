import { useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { FolderOpen, Pencil, Plus, Search, Trash2 } from 'lucide-react';
import { Button } from '../../../components/ui/Button';
import { FormField, Input, Select } from '../../../components/ui/Form';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { Modal } from '../../../components/ui/Modal';
import { Alert, LoadingState, EmptyState, ErrorState, StatusBadge } from '../../../components/ui/Feedback';
import { queryPolicies } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { getQuerySession } from '../../../core/query/queryPersistence';
import { queryKeys } from '../../../core/query/queryKeys';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { usePermissions, can as hasCapability } from '../../../core/auth/permissions';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { getCategories, deleteCategory } from '../services/catalog.service';
import { CategoryForm } from './CategoryForm';
import { categoryAllowed, categoryUse, filterCategories, invalidateCategoryViews } from '../utils/categoryManagement';
import type { CategoryContext } from '../utils/categoryManagement';
import type { BusinessCategoryDto } from '../../shared/types/business-offering.types';

export const CategoryManager = ({ scope, onClose }: { scope: CategoryContext; onClose: () => void }) => {
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const { can } = usePermissions();
  if (!workspaceId || !can(scope === 'PRODUCT' ? 'CATALOG' : 'SERVICES', 'READ')) return (
    <Modal isOpen onClose={onClose} title="Categorías"><Alert tone="warning">No tienes permiso para consultar categorías en este workspace.</Alert></Modal>
  );
  return <WorkspaceCategories key={`${workspaceId}:${scope}:${getQuerySession()}`} workspaceId={workspaceId} scope={scope} onClose={onClose} />;
};

const WorkspaceCategories = ({ workspaceId, scope, onClose }: { workspaceId: string; scope: CategoryContext; onClose: () => void }) => {
  const { can } = usePermissions();
  const queryClient = useQueryClient();
  const [editor, setEditor] = useState<{ category?: BusinessCategoryDto } | null>(null);
  const [search, setSearch] = useState('');
  const [state, setState] = useState<'all' | 'active' | 'inactive'>('all');
  const [deleteTarget, setDeleteTarget] = useState<BusinessCategoryDto | null>(null);
  const [deleteError, setDeleteError] = useState('');
  const [feedback, setFeedback] = useState('');
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const setBusyState = (value: boolean) => { busyRef.current = value; setBusy(value); };
  const categories = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.catalog.categories(workspaceId, scope),
    queryFn: ({ signal }) => getCategories(scope, signal),
  });
  const refresh = (category: BusinessCategoryDto) => invalidateCategoryViews(queryClient, workspaceId, category.scope);
  const remove = useSessionMutation({
    mutationFn: (category: BusinessCategoryDto) => deleteCategory(category.id!),
    onSuccess: (_data, category) => { void refresh(category); setDeleteTarget(null); setFeedback('Categoría eliminada.'); },
    onError: error => setDeleteError(getApiErrorPresentation(error)),
    onSettled: () => setBusyState(false),
  });
  const confirmDelete = () => {
    if (!deleteTarget?.id || busyRef.current || remove.isPending) return;
    const latestCan = (module: 'CATALOG' | 'SERVICES', capability: 'CREATE' | 'UPDATE' | 'DELETE') => hasCapability(useAuthStore.getState().me, module, capability);
    if (!categoryAllowed(latestCan, deleteTarget.scope, 'DELETE')) { setDeleteError('No tienes permiso para eliminar esta categoría.'); return; }
    setDeleteError(''); setFeedback(''); setBusyState(true); remove.mutate(deleteTarget);
  };
  const canCreate = categoryAllowed(can, scope, 'CREATE');
  const all = categories.data ?? [];
  const visible = filterCategories(all, search, state);

  return <Modal key={editor ? editor.category?.id ?? 'new' : 'list'} isOpen
    title={editor ? editor.category ? 'Editar categoría' : `Nueva categoría para ${scope === 'PRODUCT' ? 'productos' : 'servicios'}` : `Categorías de ${scope === 'PRODUCT' ? 'productos' : 'servicios'}`}
    onClose={() => { if (!busyRef.current) onClose(); }} size="xl" closeDisabled={busy}>
    {editor ? <CategoryForm context={scope} category={editor.category} onCancel={() => setEditor(null)} onBusyChange={setBusyState} onSaved={category => { void refresh(category); setFeedback(editor.category ? 'Categoría actualizada.' : 'Categoría creada.'); setBusyState(false); setEditor(null); }} /> : <>
      {feedback && <Alert tone="success" className="mb-4">{feedback}</Alert>}
      <div className="flex flex-wrap justify-between items-start gap-3 mb-5">
        <p className="text-sm text-muted max-w-sm">Organiza tus {scope === 'PRODUCT' ? 'productos' : 'servicios'}. Puedes conservar categorías inactivas y editar las que permitan tus permisos.</p>
        {canCreate ? <Button data-autofocus disabled={busy} onClick={() => { if (!busyRef.current) { setFeedback(''); setEditor({}); } }}><Plus aria-hidden="true" className="w-4 h-4" />Nueva categoría</Button>
          : <p className="text-xs text-muted">No tienes permiso para crear categorías en este módulo.</p>}
      </div>
      <div className="grid gap-4 sm:grid-cols-[1fr_10rem] mb-5">
        <FormField label="Buscar categorías">{control => <div className="relative"><Search aria-hidden="true" className="absolute left-3 top-3 w-4 h-4 text-muted" /><Input {...control} data-autofocus={!canCreate || undefined} type="search" value={search} onChange={event => setSearch(event.target.value)} className="pl-9" placeholder="Nombre o descripción" /></div>}</FormField>
        <FormField label="Estado"><Select value={state} onChange={event => setState(event.target.value as typeof state)}><option value="all">Todas</option><option value="active">Activas</option><option value="inactive">Inactivas</option></Select></FormField>
      </div>
      {categories.isPending ? <LoadingState title="Cargando categorías..." /> : categories.isError && !categories.data ? <ErrorState description={getApiErrorPresentation(categories.error)} onRetry={() => void categories.refetch()} /> : <>
        {categories.isError && <ErrorState description={getApiErrorPresentation(categories.error)} onRetry={() => void categories.refetch()} />}
        {categories.isFetching && <Alert className="mb-4">Actualizando categorías…</Alert>}
        <p className="text-xs text-muted mb-3" role="status">{visible.length} de {all.length} categorías</p>
        {visible.length === 0 ? <EmptyState icon={<FolderOpen className="w-6 h-6" />} title={all.length === 0 ? 'Aún no hay categorías' : 'No hay categorías que coincidan'}
          description={all.length === 0 ? 'Crea una categoría para organizar este módulo.' : 'Prueba otro nombre o cambia el filtro de estado.'}
          action={all.length > 0 ? <Button variant="secondary" onClick={() => { setSearch(''); setState('all'); }}>Limpiar filtros</Button> : undefined} /> : (
          <ul className="space-y-3" aria-label="Categorías registradas">{visible.map(category => {
            const canEdit = Boolean(category.id) && categoryAllowed(can, category.scope, 'UPDATE');
            const canDelete = Boolean(category.id) && categoryAllowed(can, category.scope, 'DELETE');
            return <li key={category.id ?? `${category.scope}:${category.name}`} className="rounded-xl border border-line p-4 min-w-0">
              <div className="flex flex-wrap justify-between items-start gap-3">
                <div className="min-w-0 flex-1 [overflow-wrap:anywhere]"><h3 className="font-semibold text-foreground">{category.name}</h3>
                  <p className="text-xs text-muted mt-1">{categoryUse[category.scope]}</p>
                  {category.description && <p className="text-sm text-muted mt-2 whitespace-pre-wrap">{category.description}</p>}
                </div>
                <StatusBadge label={category.isActive ? 'Activa' : 'Inactiva'} tone={category.isActive ? 'success' : 'neutral'} />
              </div>
              <div className="mt-3 pt-3 border-t border-line flex flex-wrap gap-2 items-center">
                {canEdit && <Button variant="secondary" disabled={busy} aria-label={`Editar categoría ${category.name}`} onClick={() => { if (!busyRef.current) { setFeedback(''); setEditor({ category: { ...category } }); } }}><Pencil aria-hidden="true" className="w-4 h-4" />Editar</Button>}
                {canDelete && <Button variant="ghost" disabled={busy} className="text-danger" aria-label={`Eliminar categoría ${category.name}`} onClick={() => { if (!busyRef.current) { setDeleteError(''); setDeleteTarget(category); } }}><Trash2 aria-hidden="true" className="w-4 h-4" />Eliminar</Button>}
                {!canEdit && !canDelete && <span className="text-xs text-muted">Solo lectura{!category.id ? ': categoría sin identificador.' : '.'}</span>}
                {category.scope === 'SHARED' && (!canEdit || !canDelete) && <p className="text-xs text-muted w-full">Editar o eliminar una categoría de ambos módulos requiere el permiso correspondiente en productos y servicios.</p>}
              </div>
            </li>;
          })}</ul>
        )}
      </>}
      <div className="border-t border-line mt-5 pt-4 flex justify-end"><Button variant="secondary" disabled={busy} onClick={() => { if (!busyRef.current) onClose(); }}>Cerrar</Button></div>
    </>}
    <ConfirmDialog isOpen={deleteTarget !== null} title="Eliminar categoría" description={<p>¿Eliminar <strong className="text-foreground [overflow-wrap:anywhere]">{deleteTarget?.name}</strong>? Solo se puede eliminar si no tiene productos ni servicios asociados.</p>}
      destructive confirmLabel="Eliminar categoría" cancelLabel="Conservar categoría" isLoading={remove.isPending} confirmDisabled={!deleteTarget || !categoryAllowed(can, deleteTarget.scope, 'DELETE')}
      onClose={() => { if (!busyRef.current) { setDeleteTarget(null); setDeleteError(''); } }} onConfirm={confirmDelete}>
      {deleteError && <Alert tone="error">{deleteError}</Alert>}
    </ConfirmDialog>
  </Modal>;
};
