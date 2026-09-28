import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Package, Plus, Trash2, FolderPlus } from 'lucide-react';
import { getProducts, saveProduct, deleteProduct, getCategories, saveCategory } from '../services/catalog.service';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { ImageUploader } from '../../../components/ui/ImageUploader';
import { ArtifactGenerator } from '../../artifacts/components/ArtifactGenerator';
import type { ProductCategoryDto, ProductDto } from '../types/catalog.types';

export const CatalogPage = () => {
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const selectedLocationId = useAuthStore((state) => state.selectedLocationId);

  const [showModal, setShowModal] = useState(false);
  
  // 🔥 SPRINT 11: Manejo de estado visual (Elimina window.prompt, alert y confirm)
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

  const [newProduct, setNewProduct] = useState<Partial<ProductDto>>({ 
    name: '', description: '', categoryId: '', priceMinorUnits: 0, currency: 'PEN', 
    isActive: true, type: 'PRODUCT', locationScope: 'ALL', locationIds: [] 
  });

  const saveMutation = useMutation({
    mutationFn: saveProduct,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['catalog', workspaceId] });
      setShowModal(false);
      setNewProduct({ name: '', description: '', categoryId: categories[0]?.id || '', priceMinorUnits: 0, currency: 'PEN', isActive: true, type: 'PRODUCT', locationScope: 'ALL', locationIds: [] });
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

  const handleSave = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newProduct.categoryId) return setNotification({ msg: "Debes seleccionar una categoría.", type: 'error' });
    
    const productToSave: ProductDto = {
        ...(newProduct as ProductDto),
        locationScope: newProduct.locationScope || 'ALL',
        locationIds: newProduct.locationScope === 'ALL' ? [] : (newProduct.locationIds || [])
    };
    saveMutation.mutate(productToSave);
  };

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

  if (isLoading) return <div className="p-8 text-center text-gray-500 animate-pulse">Cargando catálogo...</div>;

  return (
    <div className="max-w-6xl mx-auto animate-in fade-in">
      
      {notification && (
        <div className={`mb-4 p-4 rounded-lg flex justify-between items-center ${notification.type === 'error' ? 'bg-red-50 text-red-700 border border-red-200' : 'bg-green-50 text-green-700 border border-green-200'}`}>
          <span>{notification.msg}</span>
          <button onClick={() => setNotification(null)} className="text-sm font-bold opacity-70 hover:opacity-100">X</button>
        </div>
      )}

      {/* 🔥 SPRINT 11: Prompt de categoría custom (Evita window.prompt) */}
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
          <p className="text-sm text-gray-500 mt-1">Administra tu inventario y genera folletos comerciales PDF.</p>
        </div>
        <div className="flex gap-2">
          <button onClick={() => setShowCategoryPrompt(true)} className="flex items-center px-4 py-2 bg-gray-100 text-gray-700 font-medium rounded-lg hover:bg-gray-200 transition-colors">
            <FolderPlus className="w-4 h-4 mr-2" /> Categoría
          </button>
          <button onClick={() => { setNewProduct({ ...newProduct, categoryId: categories[0]?.id || '' }); setShowModal(true); }} className="flex items-center px-4 py-2 bg-blue-600 text-white font-medium rounded-lg hover:bg-blue-700 transition-colors">
            <Plus className="w-4 h-4 mr-2" /> Producto
          </button>
        </div>
      </div>
      
      <ArtifactGenerator scope="PRODUCT" title="Catálogo de Productos (PDF)" />

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {products.length === 0 ? (
          <div className="col-span-full p-12 text-center text-gray-500 bg-white border border-gray-200 rounded-xl shadow-sm">
            <Package className="w-10 h-10 mx-auto text-gray-300 mb-3" />
            <p className="font-medium text-gray-600">No hay productos registrados</p>
            <p className="text-sm mt-1">Agrega tu primer producto para comenzar a vender.</p>
          </div>
        ) : (
          products.map((prod) => {
            const catName = categories.find(c => c.id === prod.categoryId)?.name || 'Sin Categoría';
            return (
              <div key={prod.id} className="bg-white border border-gray-200 rounded-xl p-5 shadow-sm hover:border-blue-300 transition-colors group relative">
                <div className="flex justify-between items-start mb-2">
                  <div>
                    <h3 className="font-bold text-gray-900 text-lg leading-tight">{prod.name}</h3>
                    <span className="text-xs text-gray-500 bg-gray-100 px-2 py-0.5 rounded-full mt-1 inline-block">{catName}</span>
                  </div>
                  <span className="bg-green-100 text-green-800 text-xs font-bold px-2 py-1 rounded-lg shrink-0">
                    {prod.currency} {((prod.priceMinorUnits || 0) / 100).toFixed(2)}
                  </span>
                </div>
                <p className="text-sm text-gray-600 mb-4 h-10 overflow-hidden line-clamp-2 mt-2">{prod.description}</p>
                <div className="flex justify-between items-center pt-3 border-t border-gray-100">
                  <span className={`text-xs font-medium ${prod.isActive ? 'text-green-600' : 'text-red-500'}`}>
                    {prod.isActive ? 'Disponible' : 'Agotado / Inactivo'}
                  </span>
                  
                  {/* 🔥 Modal de eliminación integrado */}
                  <div className="relative">
                    <button 
                      onClick={() => setDeleteConfirmId(prod.id!)} 
                      className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-full transition-colors opacity-0 group-hover:opacity-100 focus:opacity-100"
                      title="Eliminar"
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>

                    {deleteConfirmId === prod.id && (
                      <div className="absolute right-0 bottom-full mb-2 bg-white border border-red-200 shadow-xl p-3 rounded-lg z-10 w-48">
                        <p className="text-xs text-red-600 font-medium mb-2">¿Eliminar producto?</p>
                        <div className="flex justify-between gap-2">
                          <button onClick={() => setDeleteConfirmId(null)} className="flex-1 text-xs bg-gray-100 py-1 rounded">No</button>
                          <button onClick={() => deleteMutation.mutate(prod.id!)} disabled={deleteMutation.isPending} className="flex-1 text-xs bg-red-600 text-white py-1 rounded">Sí, borrar</button>
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

      {showModal && (
        <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4">
          <div className="bg-white rounded-xl w-full max-w-md shadow-2xl animate-in zoom-in-95">
            <div className="p-6 border-b border-gray-100">
              <h3 className="text-xl font-bold text-gray-900">Nuevo Producto</h3>
            </div>
            
            <form onSubmit={handleSave} className="p-6 space-y-4">
              <div>
                <label className="block text-sm font-medium mb-1 text-gray-700">Nombre</label>
                <input type="text" value={newProduct.name || ''} onChange={e => setNewProduct({...newProduct, name: e.target.value})} className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-blue-500 outline-none" required placeholder="Ej. Zapatillas Deportivas" />
              </div>
              
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-700">Categoría</label>
                  <select value={newProduct.categoryId || ''} onChange={e => setNewProduct({...newProduct, categoryId: e.target.value})} className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-blue-500 outline-none bg-white" required>
                    <option value="" disabled>Selecciona...</option>
                    {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-700">Precio</label>
                  <div className="flex items-center">
                    <span className="mr-2 text-gray-500 font-medium">{newProduct.currency}</span>
                    <input type="number" step="0.10" value={(newProduct.priceMinorUnits || 0) / 100} onChange={e => setNewProduct({...newProduct, priceMinorUnits: Math.round(parseFloat(e.target.value || '0') * 100)})} className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-blue-500 outline-none" required />
                  </div>
                </div>
              </div>

              <div>
                <ImageUploader 
                  value={newProduct.imageUrl} 
                  onChange={(url) => setNewProduct({ ...newProduct, imageUrl: url })} 
                  label="Foto del Producto"
                />
              </div>

              <div>
                <label className="block text-sm font-medium mb-1 text-gray-700">Descripción breve</label>
                <textarea rows={3} value={newProduct.description || ''} onChange={e => setNewProduct({...newProduct, description: e.target.value})} className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-blue-500 outline-none resize-none" placeholder="Detalles, marca o características principales..." />
              </div>
              
              <div className="flex justify-end gap-3 pt-6">
                <button type="button" onClick={() => setShowModal(false)} className="px-4 py-2 text-gray-600 bg-gray-100 hover:bg-gray-200 transition-colors font-medium rounded-lg">Cancelar</button>
                <button type="submit" disabled={saveMutation.isPending || categories.length === 0} className="px-5 py-2 bg-blue-600 text-white font-medium hover:bg-blue-700 transition-colors rounded-lg disabled:opacity-50 shadow-sm">
                  {saveMutation.isPending ? 'Guardando...' : 'Guardar Producto'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};