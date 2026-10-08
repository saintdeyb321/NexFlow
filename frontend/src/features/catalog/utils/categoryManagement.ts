import type { QueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../../core/query/queryKeys.ts';
import type { BusinessCategoryDto } from '../../shared/types/business-offering.types';

export type CategoryContext = 'PRODUCT' | 'SERVICE';
export type CategoryScope = BusinessCategoryDto['scope'];
export type CategoryWrite = 'CREATE' | 'UPDATE' | 'DELETE';
export type CategoryPermission = (module: 'CATALOG' | 'SERVICES', capability: CategoryWrite) => boolean;
export const categoryUse: Record<CategoryScope, string> = {
  PRODUCT: 'Para productos', SERVICE: 'Para servicios', SHARED: 'Para productos y servicios',
};
export const categoryAllowed = (can: CategoryPermission, scope: CategoryScope, capability: CategoryWrite) =>
  scope === 'SHARED' ? can('CATALOG', capability) && can('SERVICES', capability) : can(scope === 'PRODUCT' ? 'CATALOG' : 'SERVICES', capability);

export interface CategoryDraft {
  name: string;
  description: string;
  isActive: boolean;
  displayOrder: string;
  shared: boolean;
}
export type CategoryErrors = Partial<Record<'name' | 'displayOrder', string>>;
export const categoryDraft = (category?: BusinessCategoryDto): CategoryDraft => ({
  name: category?.name ?? '', description: category?.description ?? '', isActive: category?.isActive ?? true,
  displayOrder: String(category?.displayOrder ?? 0), shared: category?.scope === 'SHARED',
});
export const validateCategory = (draft: CategoryDraft): CategoryErrors => {
  const errors: CategoryErrors = {};
  if (!draft.name.trim()) errors.name = 'Escribe un nombre para la categoría.';
  const order = Number(draft.displayOrder.trim());
  if (!/^[+-]?\d+$/.test(draft.displayOrder.trim()) || !Number.isInteger(order) || order < -2147483648 || order > 2147483647)
    errors.displayOrder = 'Introduce un número entero entre -2147483648 y 2147483647.';
  return errors;
};
export const categoryPayload = (draft: CategoryDraft, context: CategoryContext, existing?: BusinessCategoryDto): BusinessCategoryDto => ({
  ...(existing?.id ? { id: existing.id } : {}),
  name: draft.name.trim(),
  description: existing && draft.description === (existing.description ?? '') ? existing.description : draft.description.trim() || null,
  scope: existing?.scope ?? (draft.shared ? 'SHARED' : context),
  isActive: draft.isActive, displayOrder: Number(draft.displayOrder.trim()),
});

export const invalidateCategoryViews = (client: QueryClient, workspaceId: string, scope: CategoryScope) => Promise.all([
  client.invalidateQueries({ queryKey: scope === 'SHARED' ? queryKeys.catalog.allCategories(workspaceId) : queryKeys.catalog.categories(workspaceId, scope) }),
  client.invalidateQueries({ queryKey: scope === 'SHARED' ? queryKeys.artifacts.all(workspaceId) : queryKeys.artifacts.byScope(workspaceId, scope) }),
]);

export const filterCategories = (categories: BusinessCategoryDto[], search: string, state: 'all' | 'active' | 'inactive') => {
  const normalize = (text: string) => text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLocaleLowerCase('es');
  const term = normalize(search.trim());
  return categories.filter(category => (state === 'all' || category.isActive === (state === 'active'))
    && normalize(`${category.name} ${category.description ?? ''}`).includes(term))
    .sort((a, b) => a.displayOrder - b.displayOrder || a.name.localeCompare(b.name, 'es') || (a.id ?? '').localeCompare(b.id ?? ''));
};
