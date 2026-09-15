import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Package, Plus, Trash2, FolderPlus } from 'lucide-react';
import { getProducts, saveProduct, deleteProduct, getCategories, saveCategory } from '../services/catalog.service';
import type { CatalogItemDto, CatalogCategoryDto } from '../types/catalog.types';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { ImageUploader } from '../../../components/ui/ImageUploader';
import { ArtifactGenerator } from '../components/ArtifactGenerator';

export const CatalogPage = () => {
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const selectedLocationId = useAuthStore((state) => state.selectedLocationId); // 🔥 Contexto global de Sede

  const [showModal, setShowModal] = useState(false);
  
  // 🔥 Inyectamos selectedLocationId en la key y en la función
  const { data: products = [] as CatalogItemDto[], isLoading } = useQuery({ 
    queryKey: ['catalog', workspaceId, selectedLocationId], 
    queryFn: () => getProducts(selectedLocationId), 
    enabled: !!workspaceId 
  });
  
  // 🔥 CORRECCIÓN TYPESCRIPT: Forzamos el tipo y pasamos el scope explícitamente
  const { data: categories = [] as CatalogCategoryDto[] } = useQuery({ 
    queryKey: ['catalogCategories', workspaceId, 'PRODUCT'], 
    queryFn: () => getCategories('PRODUCT'), 
    enabled: !!workspaceId 
  });

  const [newProduct, setNewProduct] = useState<Partial<CatalogItemDto>>({ name: '', description: '', categoryId: '', priceMinorUnits: 0, currency: 'PEN', isActive: true, type: 'PRODUCT' });

  const saveMutation = useMutation({
    mutationFn: saveProduct,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['catalog', workspaceId] });
      setShowModal(false);
      setNewProduct({ name: '', description: '', categoryId: categories[0]?.id || '', priceMinorUnits: 0, currency: 'PEN', isActive: true, type: 'PRODUCT' });
    }
  });

  const deleteMutation = useMutation({
    mutationFn: deleteProduct,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['catalog', workspaceId] })
  });

  const createCategoryMutation = useMutation({
    mutationFn: saveCategory,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['catalogCategories', workspaceId] })
  });

  const handleSave = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newProduct.categoryId) return alert("Debes seleccionar una categoría.");
    saveMutation.mutate(newProduct as CatalogItemDto);
  };

  const handleQuickAddCategory = () => {
    const catName = window.prompt("Nombre de la nueva categoría (Ej: Bebidas, Postres):");
    if (catName && catName.trim()) {
      createCategoryMutation.mutate({ name: catName, isActive: true, displayOrder: 0, description: null, scope: 'PRODUCT' } as CatalogCategoryDto);
    }
  };

  if (isLoading) return <div className="p-8 text-center text-gray-500">Cargando catálogo...</div>;

  return (
    <div className="max-w-6xl mx-auto animate-in fade-in">
      <div className="mb-6 flex justify-between items-end">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center">
            <Package className="w-6 h-6 mr-3 text-blue-600" /> Catálogo de Productos
          </h1>
        </div>
        <div className="flex gap-2">
          <button onClick={handleQuickAddCategory} className="flex items-center px-4 py-2 bg-gray-100 text-gray-700 font-medium rounded-lg hover:bg-gray-200">
            <FolderPlus className="w-4 h-4 mr-2" /> Categoría
          </button>
          <button onClick={() => { setNewProduct({ ...newProduct, categoryId: categories[0]?.id || '' }); setShowModal(true); }} className="flex items-center px-4 py-2 bg-blue-600 text-white font-medium rounded-lg hover:bg-blue-700">
            <Plus className="w-4 h-4 mr-2" /> Producto
          </button>
        </div>
      </div>
      
      <ArtifactGenerator scope="PRODUCT" title="Catálogo de Productos (PDF y WebP)" />

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {products.length === 0 ? (
          <div className="col-span-full p-8 text-center text-gray-500 bg-white border border-gray-200 rounded-xl">No hay productos para esta sede.</div>
        ) : (
          products.map((prod) => {
            const catName = categories.find((c: CatalogCategoryDto) => c.id === prod.categoryId)?.name || 'Sin Categoría';
            return (
              <div key={prod.id} className="bg-white border border-gray-200 rounded-xl p-5 shadow-sm">
                <div className="flex justify-between items-start mb-2">
                  <div>
                    <h3 className="font-bold text-gray-900 text-lg">{prod.name}</h3>
                    <span className="text-xs text-gray-500">{catName}</span>
                  </div>
                  <span className="bg-green-100 text-green-800 text-xs font-bold px-2 py-1 rounded-lg">
                    {prod.currency} {((prod.priceMinorUnits || 0) / 100).toFixed(2)}
                  </span>
                </div>
                <p className="text-sm text-gray-600 mb-4 h-10 overflow-hidden line-clamp-2">{prod.description}</p>
                <div className="flex justify-between items-center pt-3 border-t border-gray-100">
                  <span className={`text-xs font-medium ${prod.isActive ? 'text-green-600' : 'text-red-500'}`}>{prod.isActive ? 'Disponible' : 'Agotado'}</span>
                  <button onClick={() => window.confirm("¿Eliminar?") && deleteMutation.mutate(prod.id!)} className="p-2 text-gray-400 hover:text-red-600">
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
              </div>
            );
          })
        )}
      </div>

      {showModal && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-white rounded-xl p-6 w-full max-w-md shadow-2xl">
            <h3 className="text-xl font-bold mb-4">Nuevo Producto</h3>
            <form onSubmit={handleSave} className="space-y-4">
              <div>
                <label className="block text-sm font-medium mb-1">Nombre</label>
                <input type="text" value={newProduct.name || ''} onChange={e => setNewProduct({...newProduct, name: e.target.value})} className="w-full border rounded-lg px-3 py-2" required />
              </div>
              
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium mb-1">Categoría</label>
                  <select value={newProduct.categoryId || ''} onChange={e => setNewProduct({...newProduct, categoryId: e.target.value})} className="w-full border rounded-lg px-3 py-2" required>
                    <option value="" disabled>Selecciona...</option>
                    {categories.map((c: CatalogCategoryDto) => <option key={c.id} value={c.id}>{c.name}</option>)}
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1">Precio</label>
                  <div className="flex items-center">
                    <span className="mr-2 text-gray-500">{newProduct.currency}</span>
                    <input type="number" step="0.10" value={(newProduct.priceMinorUnits || 0) / 100} onChange={e => setNewProduct({...newProduct, priceMinorUnits: Math.round(parseFloat(e.target.value || '0') * 100)})} className="w-full border rounded-lg px-3 py-2" required />
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
                <label className="block text-sm font-medium mb-1">Descripción</label>
                <textarea rows={2} value={newProduct.description || ''} onChange={e => setNewProduct({...newProduct, description: e.target.value})} className="w-full border rounded-lg px-3 py-2" />
              </div>
              
              <div className="flex justify-end gap-3 pt-4 border-t">
                <button type="button" onClick={() => setShowModal(false)} className="px-4 py-2 text-gray-600 bg-gray-100 rounded-lg">Cancelar</button>
                <button type="submit" disabled={saveMutation.isPending || categories.length === 0} className="px-4 py-2 bg-blue-600 text-white rounded-lg">Guardar</button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};