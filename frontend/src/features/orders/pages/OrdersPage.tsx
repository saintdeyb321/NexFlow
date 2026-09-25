import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { ShoppingBag, Clock, CheckCircle, Package, XCircle, Eye, ShoppingCart } from 'lucide-react'; // 🔥 ShoppingCart agregado
import { getOrders, updateOrderStatus } from '../services/orders.service';
import { OrderDetailModal } from '../components/OrderDetailModal';
import type { OrderStatus, OrderRecord } from '../types/orders.types';
import { useAuthStore } from '../../../core/store/useAuthStore';

export const OrdersPage = () => {
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  
  const [filterStatus, setFilterStatus] = useState<OrderStatus | 'ALL'>('ALL');
  const [selectedOrder, setSelectedOrder] = useState<OrderRecord | null>(null);

  const { data: orders = [], isLoading, isError } = useQuery({
    queryKey: ['orders', workspaceId, filterStatus],
    queryFn: () => getOrders(filterStatus),
    enabled: !!workspaceId,
    refetchInterval: 30000,
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, status }: { id: string; status: OrderStatus }) => updateOrderStatus(id, status),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['orders', workspaceId] });
    },
  });

  const getStatusBadge = (status: OrderStatus) => {
    switch (status) {
      case 'PendingReview': return <span className="flex items-center px-2.5 py-1 text-xs font-medium bg-yellow-100 text-yellow-800 rounded-full w-fit"><Clock className="w-3 h-3 mr-1" /> Por Confirmar</span>;
      case 'Approved': return <span className="flex items-center px-2.5 py-1 text-xs font-medium bg-blue-100 text-blue-800 rounded-full w-fit"><CheckCircle className="w-3 h-3 mr-1" /> Confirmado</span>;
      case 'Processing': return <span className="flex items-center px-2.5 py-1 text-xs font-medium bg-purple-100 text-purple-800 rounded-full w-fit"><Package className="w-3 h-3 mr-1" /> Separando Stock</span>;
      case 'Completed': return <span className="flex items-center px-2.5 py-1 text-xs font-medium bg-emerald-100 text-emerald-800 rounded-full w-fit"><CheckCircle className="w-3 h-3 mr-1" /> Entregado / Pagado</span>;
      case 'Rejected': return <span className="flex items-center px-2.5 py-1 text-xs font-medium bg-red-100 text-red-800 rounded-full w-fit"><XCircle className="w-3 h-3 mr-1" /> Rechazado</span>;
      case 'Cancelled': return <span className="flex items-center px-2.5 py-1 text-xs font-medium bg-gray-100 text-gray-800 rounded-full w-fit"><XCircle className="w-3 h-3 mr-1" /> Cancelado</span>;
      default: return <span className="bg-gray-100 text-gray-800 text-xs px-2.5 py-1 rounded-full w-fit">{status}</span>;
    }
  };

  const handleStatusChange = (id: string, newStatus: OrderStatus) => {
    updateMutation.mutate({ id, status: newStatus });
  };

  if (isError) return <div className="p-8 text-center text-red-500">Error al cargar los pedidos.</div>;

  return (
    <div className="max-w-7xl mx-auto animate-in fade-in">
      <div className="mb-8 flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center">
            <ShoppingBag className="w-6 h-6 mr-3 text-blue-600" /> Pedidos por Entregar
          </h1>
          <p className="mt-1 text-sm text-gray-500">
            Revisa las listas de compra que la IA capturó y coordina el recojo o envío con tus clientes.
          </p>
        </div>
        
        <select 
          value={filterStatus} 
          onChange={(e) => setFilterStatus(e.target.value as OrderStatus | 'ALL')}
          className="border border-gray-200 rounded-lg px-4 py-2.5 text-sm bg-white focus:ring-2 focus:ring-blue-500 outline-none shadow-sm"
        >
          <option value="ALL">Todos los Pedidos</option>
          <option value="PendingReview">Por Confirmar</option>
          <option value="Approved">Confirmados</option>
          <option value="Processing">Separando Stock</option>
          <option value="Completed">Entregados / Pagados</option>
        </select>
      </div>

      <div className="bg-white border border-gray-200 rounded-xl shadow-sm overflow-hidden min-h-[400px]">
        {isLoading ? (
          <div className="flex items-center justify-center h-64 text-gray-400 animate-pulse">Cargando pedidos...</div>
        ) : orders.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-20 text-gray-400">
            <ShoppingCart className="w-16 h-16 mb-4 text-gray-200" />
            <p className="text-lg font-medium text-gray-600">No hay pedidos pendientes</p>
            <p className="text-sm">Las listas de compra solicitadas por tus clientes aparecerán aquí.</p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left">
              <thead>
                <tr className="bg-gray-50 border-b border-gray-200 text-xs font-bold text-gray-500 uppercase tracking-wider">
                  <th className="px-6 py-4">Fecha</th>
                  <th className="px-6 py-4">Cliente</th>
                  <th className="px-6 py-4">Artículos</th>
                  <th className="px-6 py-4">Total Aprox.</th>
                  <th className="px-6 py-4">Estado</th>
                  <th className="px-6 py-4 text-right">Acciones</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {orders.map((order) => (
                  <tr key={order.id} className="hover:bg-gray-50/80 transition-colors">
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-600">
                      <span className="font-medium text-gray-900 block">{new Date(order.createdAt).toLocaleDateString()}</span>
                      <span className="text-xs text-gray-400">{new Date(order.createdAt).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}</span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <span className="font-semibold text-gray-900 block">{order.consumerName || 'Cliente'}</span>
                      <span className="text-sm text-gray-500">{order.consumerPhone}</span>
                    </td>
                    <td className="px-6 py-4 text-sm text-gray-600">
                      <span className="font-medium text-blue-600 bg-blue-50 px-2 py-0.5 rounded-full inline-block mb-1">
                        {order.items.reduce((acc, item) => acc + item.quantity, 0)} ítems
                      </span>
                      <p className="text-xs text-gray-500 truncate max-w-[200px]">
                        {order.items.map(i => i.productName).join(', ')}
                      </p>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap font-bold text-gray-900">
                      {order.currency} {(order.totalAmountMinorUnits / 100).toFixed(2)}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      {getStatusBadge(order.status)}
                    </td>
                    <td className="px-6 py-4 text-right flex items-center justify-end gap-2">
                      <button 
                        onClick={() => setSelectedOrder(order)}
                        className="p-2 text-gray-500 hover:text-blue-600 hover:bg-blue-50 rounded-lg transition-colors flex items-center"
                        title="Ver Detalles"
                      >
                        <Eye className="w-4 h-4" />
                      </button>
                      
                      <select
                        disabled={updateMutation.isPending}
                        value={order.status}
                        onChange={(e) => handleStatusChange(order.id, e.target.value as OrderStatus)}
                        className="text-sm border border-gray-200 rounded-lg px-2 py-1.5 bg-white hover:bg-gray-50 outline-none font-medium text-gray-700 cursor-pointer"
                      >
                        <option value="PendingReview">Por Confirmar</option>
                        <option value="Approved">Confirmar Pedido</option>
                        <option value="Processing">Separando Stock</option>
                        <option value="Completed">Entregado / Pagado</option>
                        <option value="Rejected">Rechazar Pedido</option>
                        <option value="Cancelled">Cancelar</option>
                      </select>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {selectedOrder && (
        <OrderDetailModal 
          order={selectedOrder} 
          onClose={() => setSelectedOrder(null)} 
        />
      )}
    </div>
  );
};