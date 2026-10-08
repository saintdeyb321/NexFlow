import { useRef, useState } from 'react';
import { ChevronDown } from 'lucide-react';
import { Button } from '../../../components/ui/Button';
import { FormField, Input, Textarea } from '../../../components/ui/Form';
import { Alert } from '../../../components/ui/Feedback';
import { usePermissions, can as hasCapability } from '../../../core/auth/permissions';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { saveCategory } from '../services/catalog.service';
import { categoryAllowed, categoryDraft, categoryPayload, categoryUse, validateCategory } from '../utils/categoryManagement';
import type { CategoryContext, CategoryErrors } from '../utils/categoryManagement';
import type { BusinessCategoryDto } from '../../shared/types/business-offering.types';

interface CategoryFormProps {
  context: CategoryContext;
  category?: BusinessCategoryDto;
  onCancel: () => void;
  onSaved: (category: BusinessCategoryDto) => void;
  onBusyChange: (busy: boolean) => void;
}

export const CategoryForm = ({ context, category, onCancel, onSaved, onBusyChange }: CategoryFormProps) => {
  const { can } = usePermissions();
  const [draft, setDraft] = useState(() => categoryDraft(category));
  const [errors, setErrors] = useState<CategoryErrors>({});
  const [apiError, setApiError] = useState('');
  const nameInput = useRef<HTMLInputElement>(null);
  const orderInput = useRef<HTMLInputElement>(null);
  const advanced = useRef<HTMLDetailsElement>(null);
  const submitting = useRef(false);
  const targetScope = category?.scope ?? (draft.shared ? 'SHARED' : context);
  const canWrite = categoryAllowed(can, targetScope, category ? 'UPDATE' : 'CREATE');
  const canShare = !category && categoryAllowed(can, 'SHARED', 'CREATE');
  const save = useSessionMutation({
    mutationFn: saveCategory,
    onSuccess: onSaved,
    onError: error => setApiError(getApiErrorPresentation(error)),
    onSettled: () => { submitting.current = false; onBusyChange(false); },
  });

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    if (submitting.current || save.isPending) return;
    const latestCan = (module: 'CATALOG' | 'SERVICES', capability: 'CREATE' | 'UPDATE' | 'DELETE') => hasCapability(useAuthStore.getState().me, module, capability);
    if (!categoryAllowed(latestCan, targetScope, category ? 'UPDATE' : 'CREATE')) {
      setApiError('No tienes permiso para guardar esta categoría.'); return;
    }
    const nextErrors = validateCategory(draft); setErrors(nextErrors); setApiError('');
    if (nextErrors.displayOrder && advanced.current) advanced.current.open = true;
    if (nextErrors.name) { nameInput.current?.focus(); return; }
    if (nextErrors.displayOrder) { orderInput.current?.focus(); return; }
    submitting.current = true; onBusyChange(true);
    save.mutate(categoryPayload(draft, context, category));
  };

  return <form onSubmit={submit} noValidate className="space-y-5">
    <div className="rounded-xl border border-line bg-surface-soft p-4">
      <p className="font-medium text-foreground">{categoryUse[targetScope]}</p>
      <p className="mt-1 text-sm text-muted">{category ? 'El uso de una categoría existente se conserva; no puede cambiarse al editar.' : 'Organiza tus elementos con un nombre claro y fácil de reconocer.'}</p>
    </div>
    {!canWrite && <Alert tone="warning">{targetScope === 'SHARED' ? 'Necesitas el permiso correspondiente en productos y servicios para guardar esta categoría.' : 'No tienes permiso para guardar esta categoría. Puedes cancelar para volver al listado.'}</Alert>}
    {apiError && <Alert tone="error">{apiError}</Alert>}
    <fieldset disabled={!canWrite || save.isPending} className="space-y-5">
      <FormField label="Nombre" required error={errors.name}>
        <Input ref={nameInput} data-autofocus value={draft.name} onChange={event => setDraft({ ...draft, name: event.target.value })} placeholder="Ej.: Bebidas, Consultas" autoComplete="off" />
      </FormField>
      <FormField label="Descripción" helperText="Opcional. Una breve explicación para reconocer esta categoría.">
        <Textarea rows={3} value={draft.description} onChange={event => setDraft({ ...draft, description: event.target.value })} />
      </FormField>
      {canShare && <label className="flex items-start gap-3 rounded-xl border border-line p-4 min-h-11">
        <Input type="checkbox" className="mt-0.5" checked={draft.shared} onChange={event => setDraft({ ...draft, shared: event.target.checked })} />
        <span className="text-sm"><span className="font-medium">Usar también en productos y servicios</span><span className="block text-xs text-muted mt-1">La categoría estará disponible en ambos módulos.</span></span>
      </label>}
      <label className="flex items-start gap-3 min-h-11 rounded-xl border border-line p-4">
        <Input type="checkbox" checked={draft.isActive} onChange={event => setDraft({ ...draft, isActive: event.target.checked })} className="mt-0.5" />
        <span className="text-sm"><span className="font-medium">Activa</span><span className="block text-xs text-muted mt-1">Puedes conservar una categoría inactiva sin eliminarla.</span></span>
      </label>
      <details ref={advanced} className="group rounded-xl border border-line p-4">
        <summary tabIndex={0} className="min-h-11 flex items-center justify-between gap-3 cursor-pointer font-medium text-sm">Opciones avanzadas<ChevronDown aria-hidden="true" className="w-4 h-4 shrink-0 group-open:rotate-180" /></summary>
        <FormField label="Orden de visualización" error={errors.displayOrder} helperText="Los números menores aparecen primero. El valor inicial es 0; los órdenes existentes se conservan." className="mt-3">
          <Input ref={orderInput} type="number" step="1" value={draft.displayOrder} onChange={event => setDraft({ ...draft, displayOrder: event.target.value })} />
        </FormField>
      </details>
    </fieldset>
    <div className="border-t border-line pt-4 flex flex-wrap justify-end gap-2">
      <Button variant="secondary" disabled={save.isPending} onClick={() => { if (!submitting.current) onCancel(); }}>Cancelar</Button>
      <Button type="submit" isLoading={save.isPending} disabled={!canWrite}>{category ? 'Guardar cambios' : 'Crear categoría'}</Button>
    </div>
  </form>;
};
