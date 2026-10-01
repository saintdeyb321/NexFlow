import { Button, IconButton } from '../../../components/ui/Button';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { useToast } from '../../../components/ui/Toast';
import { OfferingImage } from '../../../components/ui/OfferingImage';
import { PageHeader } from '../../../components/ui/Layout';
import { LoadingState, EmptyState, StatusBadge, Badge, ErrorState } from '../../../components/ui/Feedback';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { queryPolicies } from '../../../core/query/queryPolicies';
import { CategoryManager } from '../../catalog/components/CategoryManager';
import { usePermissions } from '../../../core/auth/permissions';
import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Package, Plus, Trash2, FolderPlus, Edit2 } from 'lucide-react';
import { getProducts, saveProduct, deleteProduct, getCategories } from '../services/catalog.service';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { ArtifactGenerator } from '../../artifacts/components/ArtifactGenerator';
import { ProductModal } from '../components/ProductModal';
import type { ProductDto } from '../types/catalog.types';

export const CatalogPage = () => {
  const queryClient = useQueryClient();
  const toast = useToast();
  const { can } = usePermissions();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const selectedLocationId = useAuthStore((state) => state.selectedLocationId);

  // Estados visuales controlados
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [productToEdit, setProductToEdit] = useState<ProductDto | null>(null);

  const [deleteConfirmId, setDeleteConfirmId] = useState<string | null>(null);
  const [showCategoryPrompt, setShowCategoryPrompt] = useState(false);

  const { data: products = [], isLoading, isError, refetch } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.catalog.products(workspaceId, selectedLocationId),
    queryFn: ({ signal }) => getProducts(selectedLocationId, signal),
    enabled: !!workspaceId && can('CATALOG', 'READ')
  });

  const { data: categories = [] } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.catalog.categories(workspaceId, 'PRODUCT'),
    queryFn: ({ signal }) => getCategories('PRODUCT', signal),
    enabled: !!workspaceId && can('CATALOG', 'READ')
  });

  const saveMutation = useSessionMutation({
    mutationFn: saveProduct,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.catalog.allProducts(workspaceId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.artifacts.byScope(workspaceId, 'PRODUCT') });
      setIsModalOpen(false);
      setProductToEdit(null);
      toast.success('Producto guardado exitosamente.');
    },
    onError: (error: unknown) => toast.toastApiError(error)
  });

  const deleteMutation = useSessionMutation({
    mutationFn: deleteProduct,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.catalog.allProducts(workspaceId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.artifacts.byScope(workspaceId, 'PRODUCT') });
      setDeleteConfirmId(null);
      toast.success('Producto eliminado exitosamente.');
    },
    onError: (error: unknown) => toast.toastApiError(error)
  });

  const handleCreateNew = () => {
    setProductToEdit(null);
    setIsModalOpen(true);
  };

  const handleEdit = (prod: ProductDto) => {
    setProductToEdit(prod);
    setIsModalOpen(true);
  };

  if (isLoading) return <LoadingState title="Cargando catálogo..." />;

  if (isError) return <ErrorState onRetry={() => void refetch()} />;

  return (
    <div className="nf-page">

      {showCategoryPrompt && <CategoryManager scope="PRODUCT" onClose={() => setShowCategoryPrompt(false)} />}

      <PageHeader title="Catálogo de Productos" description="Administra los productos que tu asistente puede mostrar a los clientes." icon={<Package aria-hidden="true" className="w-6 h-6 text-primary" />} actions={
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" disabled={!can('CATALOG', 'READ')} onClick={() => setShowCategoryPrompt(true)} className="flex items-center font-medium transition-colors">
            <FolderPlus aria-hidden="true" className="w-4 h-4 mr-2" /> Categoría
          </Button>
          <Button variant="primary" disabled={!can('CATALOG', 'CREATE')} onClick={handleCreateNew} className="flex items-center font-medium transition-colors">
            <Plus aria-hidden="true" className="w-4 h-4 mr-2" /> Producto
          </Button>
        </div>
      } />

      {can('CATALOG', 'GENERATE') && <ArtifactGenerator scope="PRODUCT" title="Folleto de Productos (PDF)" />}

      <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-3 gap-5">
        {products.length === 0 ? (
          <EmptyState action={can('CATALOG', 'CREATE') ? <Button onClick={handleCreateNew}><Plus aria-hidden="true" className="w-4 h-4" /> Crear producto</Button> : undefined} className="col-span-full nf-panel" icon={<Package aria-hidden="true" className="w-10 h-10" />} title="No hay productos registrados" description="Agrega tu primer producto para que la IA pueda ofrecerlo." />
        ) : (
          products.map((prod) => {
            const catName = categories.find(c => c.id === prod.categoryId)?.name || 'Sin Categoría';
            return (
              <article key={prod.id} className="nf-panel overflow-hidden flex flex-col min-w-0">
                <OfferingImage src={prod.imageUrl} name={prod.name} icon={<Package aria-hidden="true" className="w-10 h-10" />} className="h-44 border-b border-line" />
                <div className="p-5 flex-1 flex flex-col">
                <div className="flex justify-between items-start mb-2">
                  <div className="min-w-0 pr-2">
                    <h3 className={`font-bold text-lg leading-tight ${prod.isActive ? 'text-foreground' : 'text-muted'}`}>{prod.name}</h3>
                    <Badge className="mt-1">{catName}</Badge>
                  </div>
                  <span className="text-sm font-semibold text-primary shrink-0">
                    {prod.currency} {((prod.priceMinorUnits || 0) / 100).toFixed(2)}
                  </span>
                </div>
                <p className="text-sm leading-relaxed text-muted mb-4 line-clamp-2 mt-2">{prod.description}</p>
                <p className="text-xs text-muted mb-4">{prod.locationScope === 'ALL' ? 'Todas las sedes' : `${prod.locationIds?.length ?? 0} sedes específicas`}</p>
                <div className="flex flex-wrap gap-2 justify-between items-center mt-auto pt-3 border-t border-line">
                  <StatusBadge label={prod.isActive ? 'Disponible' : 'Inactivo / Oculto'} tone={prod.isActive ? 'success' : 'neutral'} />

                  {/* Controles integrados CRUD */}
                  <div className="flex gap-1 relative">
                    <IconButton variant="ghost" label="Editar"
                      disabled={!can('CATALOG', 'UPDATE')} onClick={() => handleEdit(prod)}
                      className="transition-colors"
                      title="Editar"
                    >
                      <Edit2 aria-hidden="true" className="w-4 h-4" />
                    </IconButton>

                    <IconButton variant="ghost" label="Eliminar"
                      disabled={!can('CATALOG', 'DELETE')} onClick={() => setDeleteConfirmId(prod.id!)}
                      className="transition-colors text-danger"
                      title="Eliminar"
                    >
                      <Trash2 aria-hidden="true" className="w-4 h-4" />
                    </IconButton>

                  </div>
                </div>
                </div>
              </article>
            );
          })
        )}
      </div>

      <ConfirmDialog isOpen={deleteConfirmId !== null} title="Eliminar producto" description="¿Eliminar este producto?" destructive confirmLabel="Eliminar" isLoading={deleteMutation.isPending} confirmDisabled={!can('CATALOG', 'DELETE')} onClose={() => setDeleteConfirmId(null)} onConfirm={() => { if (deleteConfirmId && can('CATALOG', 'DELETE')) deleteMutation.mutate(deleteConfirmId); }} />
      <ProductModal
        isOpen={isModalOpen && can('CATALOG', productToEdit ? 'UPDATE' : 'CREATE')}
        onClose={() => { setIsModalOpen(false); setProductToEdit(null); }}
        onSave={(data) => { if (can('CATALOG', data.id ? 'UPDATE' : 'CREATE')) saveMutation.mutate(data); }}
        isSaving={saveMutation.isPending}
        categories={categories}
        productToEdit={productToEdit}
      />
    </div>
  );
};
