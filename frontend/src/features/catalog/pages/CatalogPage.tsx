import { useState, useEffect } from 'react';
import { Package, Plus, Trash2 } from 'lucide-react';
import { getProducts, saveProduct, deleteProduct } from '../services/catalog.service';
import type { ProductDto } from '../types/catalog.types';

export const CatalogPage = () => {
  const [products, setProducts] = useState<ProductDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [showModal, setShowModal] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  
  const [newProduct, setNewProduct] = useState<ProductDto>({ 
    name: '', 
    description: '', 
    category: 'General',
    price: 0, 
    currency: 'PEN',
    isActive: true 
  });

  useEffect(() => {
    loadProducts();
  }, []);

  const loadProducts = async () => {
    try {
      const data = await getProducts();
      setProducts(data);
    } catch (error: any) {
      console.error("Error al cargar productos", error);
    } finally {
      setIsLoading(false);
    }
  };

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSaving(true);
    try {
      await saveProduct(newProduct);
      setShowModal(false);
      setNewProduct({ name: '', description: '', category: 'General', price: 0, currency: 'PEN', isActive: true });
      loadProducts();
    } catch (error: any) {
      alert(`Error al guardar: ${error.message || 'Error desconocido'}`);
    } finally {
      setIsSaving(false);
    }
  };

  const handleDelete = async (id: string) => {
    if (!confirm("¿Eliminar este producto?")) return;
    try {
      await deleteProduct(id);
      setProducts(products.filter(p => p.id !== id));
    } catch (error: any) {
      alert(`Error al eliminar: ${error.message || 'El producto no pudo ser eliminado'}`);
    }
  };

  if (isLoading) return <div className="animate-pulse p-8 text-center text-gray-500">Cargando catálogo...</div>;

  return (
    <div className="max-w-6xl mx-auto animate-in fade-in slide-in-from-bottom-2">
      <div className="mb-6 flex justify-between items-end">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center">
            <Package className="w-6 h-6 mr-3 text-blue-600" /> Catálogo de Productos
          </h1>
          <p className="mt-1 text-sm text-gray-500">Administra los productos físicos que la IA ofrecerá a tus clientes.</p>
        </div>
        <button onClick={() => setShowModal(true)} className="flex items-center px-4 py-2 bg-blue-600 text-white font-medium rounded-lg hover:bg-blue-700 transition-colors">
          <Plus className="w-4 h-4 mr-2" /> Agregar Producto
        </button>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {products.length === 0 ? (
          <div className="col-span-full p-8 text-center text-gray-500 bg-white border border-gray-200 rounded-xl">
            Tu catálogo está vacío. Comienza agregando tu primer producto.
          </div>
        ) : (
          products.map((prod) => {
            // 🔥 SOLUCIÓN BLINDADA: Calcula el precio sin importar si el backend manda price o priceMinorUnits
            const displayPrice = (prod as any).priceMinorUnits ? ((prod as any).priceMinorUnits / 100) : (prod.price || 0);
            
            return (
              <div key={prod.id} className="bg-white border border-gray-200 rounded-xl p-5 shadow-sm hover:shadow-md transition-shadow relative">
                <div className="flex justify-between items-start mb-2">
                  <div>
                    <h3 className="font-bold text-gray-900 text-lg leading-tight">{prod.name}</h3>
                    <span className="text-xs text-gray-500">{prod.category}</span>
                  </div>
                  <span className="bg-green-100 text-green-800 text-xs font-bold px-2.5 py-1 rounded-lg">
                    {prod.currency} {displayPrice.toFixed(2)}
                  </span>
                </div>
                <p className="text-sm text-gray-600 mb-4 h-10 overflow-hidden text-ellipsis line-clamp-2">{prod.description}</p>
                <div className="flex justify-between items-center pt-3 border-t border-gray-100">
                  <span className={`text-xs font-medium ${prod.isActive ? 'text-green-600' : 'text-red-500'}`}>
                    {prod.isActive ? 'Disponible' : 'Agotado'}
                  </span>
                  <button onClick={() => handleDelete(prod.id!)} className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-lg transition-colors">
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
              </div>
            );
          })
        )}
      </div>

      {showModal && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 animate-in fade-in">
          <div className="bg-white rounded-xl p-6 w-full max-w-md shadow-2xl">
            <h3 className="text-xl font-bold text-gray-900 mb-4">Nuevo Producto</h3>
            <form onSubmit={handleSave} className="space-y-4">
              <div>
                <label className="block text-sm font-medium mb-1">Nombre</label>
                <input type="text" value={newProduct.name} onChange={e => setNewProduct({...newProduct, name: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" required />
              </div>
              
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium mb-1">Categoría</label>
                  <input type="text" placeholder="Ej: Bebidas" value={newProduct.category} onChange={e => setNewProduct({...newProduct, category: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" required />
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1">Precio</label>
                  <div className="flex items-center">
                    <span className="text-gray-500 mr-2 text-sm">{newProduct.currency}</span>
                    <input 
                      type="number" 
                      step="0.10" 
                      value={(newProduct as any).priceMinorUnits ? (newProduct as any).priceMinorUnits / 100 : (newProduct.price || 0)} 
                      onChange={e => {
                        const val = parseFloat(e.target.value) || 0;
                        setNewProduct({...newProduct, price: val, priceMinorUnits: Math.round(val * 100)} as any);
                      }} 
                      className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" 
                      required 
                    />
                  </div>
                </div>
              </div>

              <div>
                <label className="block text-sm font-medium mb-1">Descripción</label>
                <textarea rows={3} value={newProduct.description} onChange={e => setNewProduct({...newProduct, description: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" required />
              </div>
              
              <div className="flex justify-end space-x-3 pt-4 border-t">
                <button type="button" onClick={() => setShowModal(false)} className="px-4 py-2 text-gray-600 hover:bg-gray-100 rounded-lg transition-colors font-medium">Cancelar</button>
                <button type="submit" disabled={isSaving} className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors disabled:opacity-50 font-medium">
                  {isSaving ? 'Guardando...' : 'Guardar'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};