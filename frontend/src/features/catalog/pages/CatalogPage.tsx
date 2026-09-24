import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Package, Plus, Trash2, FolderPlus } from 'lucide-react';
import { getProducts, saveProduct, deleteProduct, getCategories, saveCategory } from '../services/catalog.service';
import type { ProductDto, CatalogCategoryDto } from '../types/catalog.types';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { ImageUploader } from '../../../components/ui/ImageUploader';
import { ArtifactGenerator } from '../components/ArtifactGenerator';

export const CatalogPage = () => {
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const selectedLocationId = useAuthStore((state) => state.selectedLocationId);

  const [showModal, setShowModal] = useState(false);
  
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
    
    const productToSave: ProductDto = {
        ...(newProduct as ProductDto),
        locationScope: newProduct.locationScope || 'ALL',
        locationIds: newProduct.locationScope === 'ALL' ? [] : (newProduct.locationIds || [])
    };
    saveMutation.mutate(productToSave);
  };

  const handleQuickAddCategory = () => {
    const catName = window.prompt("Nombre de la nueva categoría (Ej: Bebidas, Postres, Herramientas):");
    if (catName && catName.trim()) {
      createCategoryMutation.mutate({ 
        name: catName, 
        isActive: true, 
        displayOrder: 0, 
        description: null, 
        scope: 'PRODUCT' 
      } as CatalogCategoryDto);
    }
  };

  if (isLoading) return <div className="p-8 text-center text-gray-500 animate-pulse">Cargando catálogo...</div>;

  return (
    <div className="max-w-6xl mx-auto animate-in fade-in">
      <div className="mb-6 flex justify-between items-end">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center">
            <Package className="w-6 h-6 mr-3 text-blue-600" /> Catálogo de Productos
          </h1>
          <p className="text-sm text-gray-500 mt-1">Administra tu inventario y genera folletos comerciales PDF.</p>
        </div>
        <div className="flex gap-2">
          <button onClick={handleQuickAddCategory} className="flex items-center px-4 py-2 bg-gray-100 text-gray-700 font-medium rounded-lg hover:bg-gray-200 transition-colors">
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
              <div key={prod.id} className="bg-white border border-gray-200 rounded-xl p-5 shadow-sm hover:border-blue-300 transition-colors group">
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
                  <button 
                    onClick={() => window.confirm("¿Estás seguro de eliminar este producto?") && deleteMutation.mutate(prod.id!)} 
                    className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-full transition-colors opacity-0 group-hover:opacity-100 focus:opacity-100"
                    title="Eliminar"
                  >
                    <Trash2 className="w-4 h-4" />
                  </button>
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