import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Package, Plus, Trash2, FolderPlus, Edit2 } from 'lucide-react';
import { getProducts, saveProduct, deleteProduct, getCategories, saveCategory } from '../services/catalog.service';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { ArtifactGenerator } from '../../artifacts/components/ArtifactGenerator';
import { ProductModal } from '../components/ProductModal';
import type { ProductCategoryDto, ProductDto } from '../types/catalog.types';

export const CatalogPage = () => {
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const selectedLocationId = useAuthStore((state) => state.selectedLocationId);

  // Estados visuales controlados
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [productToEdit, setProductToEdit] = useState<ProductDto | null>(null);
  
  const [notification, setNotification] = useState<{ msg: string, type: 'success' | 'error' } | null>(null);
  const [deleteConfirmId, setDeleteConfirmId] = useState<string | null>(null);
  const [showCategoryPrompt, setShowCategoryPrompt] = useState(false);
  const [newCategoryName, setNewCategoryName] = useState('');
  
  const { data: products = [], isLoading } = useQuery({ 
    queryKey: ['catalog', workspaceId, selectedLocationId], 
    queryFn: () => getProducts(selectedLocationId), 
    enabled: !!workspaceId 
  });
  
  const { data: categories = [] } = useQuery({ 
    queryKey: ['catalogCategories', workspaceId, 'PRODUCT'], 
    queryFn: () => getCategories('PRODUCT'), 
    enabled: !!workspaceId 
  });

  const saveMutation = useMutation({
    mutationFn: saveProduct,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['catalog', workspaceId] });
      setIsModalOpen(false);
      setProductToEdit(null);
      setNotification({ msg: 'Producto guardado exitosamente.', type: 'success' });
    },
    onError: (error: any) => setNotification({ msg: error.message || 'Error al guardar el producto.', type: 'error' })
  });

  const deleteMutation = useMutation({
    mutationFn: deleteProduct,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['catalog', workspaceId] });
      setDeleteConfirmId(null);
      setNotification({ msg: 'Producto eliminado exitosamente.', type: 'success' });
    },
    onError: (error: any) => setNotification({ msg: error.message || 'Error al eliminar el producto.', type: 'error' })
  });

  const createCategoryMutation = useMutation({
    mutationFn: saveCategory,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['catalogCategories', workspaceId] });
      setShowCategoryPrompt(false);
      setNewCategoryName('');
      setNotification({ msg: 'Categoría creada exitosamente.', type: 'success' });
    },
    onError: (error: any) => setNotification({ msg: error.message || 'Error al crear la categoría.', type: 'error' })
  });

  const handleQuickAddCategory = () => {
    if (newCategoryName.trim()) {
      createCategoryMutation.mutate({ 
        name: newCategoryName, 
        isActive: true, 
        displayOrder: 0, 
        description: null, 
        scope: 'PRODUCT' 
      } as ProductCategoryDto);
    }
  };

  const handleCreateNew = () => {
    setProductToEdit(null);
    setIsModalOpen(true);
  };

  const handleEdit = (prod: ProductDto) => {
    setProductToEdit(prod);
    setIsModalOpen(true);
  };

  if (isLoading) return <div className="p-8 text-center text-gray-500 animate-pulse">Cargando catálogo...</div>;

  return (
    <div className="max-w-6xl mx-auto animate-in fade-in">
      
      {notification && (
        <div className={`mb-4 p-4 rounded-lg flex justify-between items-center ${notification.type === 'error' ? 'bg-red-50 text-red-700 border border-red-200' : 'bg-green-50 text-green-700 border border-green-200'}`}>
          <span>{notification.msg}</span>
          <button onClick={() => setNotification(null)} className="text-sm font-bold opacity-70 hover:opacity-100">X</button>
        </div>
      )}

      {showCategoryPrompt && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-white p-6 rounded-xl shadow-lg w-96">
            <h3 className="text-lg font-bold mb-4">Nueva Categoría de Producto</h3>
            <input 
              type="text" 
              autoFocus
              placeholder="Ej: Bebidas, Postres, Herramientas..." 
              value={newCategoryName} 
              onChange={e => setNewCategoryName(e.target.value)}
              className="w-full border rounded-lg p-2 mb-4"
            />
            <div className="flex justify-end gap-2">
              <button onClick={() => setShowCategoryPrompt(false)} className="px-4 py-2 bg-gray-100 rounded-lg text-gray-700">Cancelar</button>
              <button onClick={handleQuickAddCategory} disabled={createCategoryMutation.isPending || !newCategoryName.trim()} className="px-4 py-2 bg-blue-600 text-white rounded-lg disabled:opacity-50">Crear</button>
            </div>
          </div>
        </div>
      )}

      <div className="mb-6 flex justify-between items-end">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center">
            <Package className="w-6 h-6 mr-3 text-blue-600" /> Catálogo de Productos
          </h1>
          {/* 🔥 SPRINT 03: Eliminado el concepto obsoleto de "inventario" */}
          <p className="text-sm text-gray-500 mt-1">Administra los productos que tu asistente puede mostrar a los clientes.</p>
        </div>
        <div className="flex gap-2">
          <button onClick={() => setShowCategoryPrompt(true)} className="flex items-center px-4 py-2 bg-gray-100 text-gray-700 font-medium rounded-lg hover:bg-gray-200 transition-colors">
            <FolderPlus className="w-4 h-4 mr-2" /> Categoría
          </button>
          <button onClick={handleCreateNew} className="flex items-center px-4 py-2 bg-blue-600 text-white font-medium rounded-lg hover:bg-blue-700 transition-colors">
            <Plus className="w-4 h-4 mr-2" /> Producto
          </button>
        </div>
      </div>
      
      <ArtifactGenerator scope="PRODUCT" title="Folleto de Productos (PDF)" />

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {products.length === 0 ? (
          <div className="col-span-full p-12 text-center text-gray-500 bg-white border border-gray-200 rounded-xl shadow-sm">
            <Package className="w-10 h-10 mx-auto text-gray-300 mb-3" />
            <p className="font-medium text-gray-600">No hay productos registrados</p>
            <p className="text-sm mt-1">Agrega tu primer producto para que la IA pueda ofrecerlo.</p>
          </div>
        ) : (
          products.map((prod) => {
            const catName = categories.find(c => c.id === prod.categoryId)?.name || 'Sin Categoría';
            return (
              <div key={prod.id} className={`bg-white border ${prod.isActive ? 'border-gray-200 hover:border-blue-300' : 'border-gray-100 bg-gray-50 opacity-80'} rounded-xl p-5 shadow-sm transition-colors group relative`}>
                <div className="flex justify-between items-start mb-2">
                  <div>
                    <h3 className={`font-bold text-lg leading-tight ${prod.isActive ? 'text-gray-900' : 'text-gray-500 line-through'}`}>{prod.name}</h3>
                    <span className="text-xs text-gray-500 bg-gray-100 px-2 py-0.5 rounded-full mt-1 inline-block">{catName}</span>
                  </div>
                  <span className={`text-xs font-bold px-2 py-1 rounded-lg shrink-0 ${prod.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-200 text-gray-600'}`}>
                    {prod.currency} {((prod.priceMinorUnits || 0) / 100).toFixed(2)}
                  </span>
                </div>
                <p className="text-sm text-gray-600 mb-4 h-10 overflow-hidden line-clamp-2 mt-2">{prod.description}</p>
                <div className="flex justify-between items-center pt-3 border-t border-gray-100">
                  <span className={`text-xs font-medium ${prod.isActive ? 'text-green-600' : 'text-red-500'}`}>
                    {prod.isActive ? 'Disponible' : 'Inactivo / Oculto'}
                  </span>
                  
                  {/* Controles integrados CRUD */}
                  <div className="flex gap-1 relative">
                    <button 
                      onClick={() => handleEdit(prod)}
                      className="p-2 text-gray-400 hover:text-blue-600 hover:bg-blue-50 rounded-full transition-colors opacity-0 group-hover:opacity-100"
                      title="Editar"
                    >
                      <Edit2 className="w-4 h-4" />
                    </button>
                    
                    <button 
                      onClick={() => setDeleteConfirmId(prod.id!)} 
                      className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-full transition-colors opacity-0 group-hover:opacity-100"
                      title="Eliminar"
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>

                    {deleteConfirmId === prod.id && (
                      <div className="absolute right-0 bottom-full mb-2 bg-white border border-red-200 shadow-xl p-3 rounded-lg z-10 w-48">
                        <p className="text-xs text-red-600 font-medium mb-2">¿Eliminar producto?</p>
                        <div className="flex justify-between gap-2">
                          <button onClick={() => setDeleteConfirmId(null)} className="flex-1 text-xs bg-gray-100 py-1 rounded">No</button>
                          <button onClick={() => deleteMutation.mutate(prod.id!)} disabled={deleteMutation.isPending} className="flex-1 text-xs bg-red-600 text-white py-1 rounded">Sí</button>
                        </div>
                      </div>
                    )}
                  </div>
                </div>
              </div>
            );
          })
        )}
      </div>

      <ProductModal
        isOpen={isModalOpen}
        onClose={() => { setIsModalOpen(false); setProductToEdit(null); }}
        onSave={(data) => saveMutation.mutate(data)}
        isSaving={saveMutation.isPending}
        categories={categories}
        productToEdit={productToEdit}
      />
    </div>
  );
};