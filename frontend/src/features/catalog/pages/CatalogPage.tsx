import { Button, IconButton } from '../../../components/ui/Button';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { useToast } from '../../../components/ui/Toast';
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
    <div className="max-w-6xl mx-auto animate-in fade-in">

      {showCategoryPrompt && <CategoryManager scope="PRODUCT" onClose={() => setShowCategoryPrompt(false)} />}

      <PageHeader title="Catálogo de Productos" description="Administra los productos que tu asistente puede mostrar a los clientes." icon={<Package className="w-6 h-6 text-blue-600" />} actions={
        <div className="flex gap-2">
          <Button variant="secondary" disabled={!can('CATALOG', 'READ')} onClick={() => setShowCategoryPrompt(true)} className="flex items-center px-4 py-2 bg-gray-100 text-gray-700 font-medium rounded-lg hover:bg-gray-200 transition-colors">
            <FolderPlus className="w-4 h-4 mr-2" /> Categoría
          </Button>
          <Button variant="primary" disabled={!can('CATALOG', 'CREATE')} onClick={handleCreateNew} className="flex items-center px-4 py-2 bg-blue-600 text-white font-medium rounded-lg hover:bg-blue-700 transition-colors">
            <Plus className="w-4 h-4 mr-2" /> Producto
          </Button>
        </div>
      } />

      {can('CATALOG', 'GENERATE') && <ArtifactGenerator scope="PRODUCT" title="Folleto de Productos (PDF)" />}

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {products.length === 0 ? (
          <EmptyState className="col-span-full bg-white border border-gray-200 rounded-xl" icon={<Package className="w-10 h-10" />} title="No hay productos registrados" description="Agrega tu primer producto para que la IA pueda ofrecerlo." />
        ) : (
          products.map((prod) => {
            const catName = categories.find(c => c.id === prod.categoryId)?.name || 'Sin Categoría';
            return (
              <div key={prod.id} className={`bg-white border ${prod.isActive ? 'border-gray-200 hover:border-blue-300' : 'border-gray-100 bg-gray-50 opacity-80'} rounded-xl p-5 shadow-sm transition-colors group relative`}>
                <div className="flex justify-between items-start mb-2">
                  <div>
                    <h3 className={`font-bold text-lg leading-tight ${prod.isActive ? 'text-gray-900' : 'text-gray-500 line-through'}`}>{prod.name}</h3>
                    <Badge className="mt-1">{catName}</Badge>
                  </div>
                  <span className={`text-xs font-bold px-2 py-1 rounded-lg shrink-0 ${prod.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-200 text-gray-600'}`}>
                    {prod.currency} {((prod.priceMinorUnits || 0) / 100).toFixed(2)}
                  </span>
                </div>
                <p className="text-sm text-gray-600 mb-4 h-10 overflow-hidden line-clamp-2 mt-2">{prod.description}</p>
                <div className="flex justify-between items-center pt-3 border-t border-gray-100">
                  <StatusBadge label={prod.isActive ? 'Disponible' : 'Inactivo / Oculto'} tone={prod.isActive ? 'success' : 'error'} />

                  {/* Controles integrados CRUD */}
                  <div className="flex gap-1 relative">
                    <IconButton variant="ghost" label="Editar"
                      disabled={!can('CATALOG', 'UPDATE')} onClick={() => handleEdit(prod)}
                      className="p-2 text-gray-400 hover:text-blue-600 hover:bg-blue-50 rounded-full transition-colors opacity-100 md:opacity-0 md:group-hover:opacity-100 [@media(hover:none)]:opacity-100 [@media(pointer:coarse)]:opacity-100 focus:opacity-100 group-focus-within:opacity-100"
                      title="Editar"
                    >
                      <Edit2 className="w-4 h-4" />
                    </IconButton>

                    <IconButton variant="ghost" label="Eliminar"
                      disabled={!can('CATALOG', 'DELETE')} onClick={() => setDeleteConfirmId(prod.id!)}
                      className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-full transition-colors opacity-100 md:opacity-0 md:group-hover:opacity-100 [@media(hover:none)]:opacity-100 [@media(pointer:coarse)]:opacity-100 focus:opacity-100 group-focus-within:opacity-100"
                      title="Eliminar"
                    >
                      <Trash2 className="w-4 h-4" />
                    </IconButton>

                  </div>
                </div>
              </div>
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
