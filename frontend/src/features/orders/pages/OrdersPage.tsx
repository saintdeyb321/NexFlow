import { IconButton } from '../../../components/ui/Button';
import { LoadingState, EmptyState, ErrorState, Badge } from '../../../components/ui/Feedback';
import { useToast } from '../../../components/ui/Toast';
import { queryPolicies, usePageVisible } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { orderTransitions } from '../types/orders.types';
import { usePermissions } from '../../../core/auth/permissions';

import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ShoppingBag, Clock, CheckCircle, Package, XCircle, Eye, ShoppingCart } from 'lucide-react';
import { getOrders, updateOrderStatus } from '../services/orders.service';
import { OrderDetailModal } from '../components/OrderDetailModal';
import type { OrderStatus } from '../types/orders.types';
import { useAuthStore } from '../../../core/store/useAuthStore';

export const OrdersPage = () => {
  const isPageVisible = usePageVisible();
  const queryClient = useQueryClient();
  const toast = useToast();
  const { can } = usePermissions();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);

  const [filterStatus, setFilterStatus] = useState<OrderStatus | 'ALL'>('ALL');
  const [selectedOrderId, setSelectedOrder] = useState<string | null>(null);

  const { data: orders = [], isLoading, isError, refetch } = useQuery({
    ...queryPolicies.dynamic,
    queryKey: queryKeys.orders.list(workspaceId, filterStatus),
    queryFn: ({ signal }) => getOrders(filterStatus, signal),
    enabled: !!workspaceId && can('ORDERS', 'READ'),
    refetchInterval: isPageVisible ? 30000 : false,
  });

  const selectedOrder = orders.find(order => order.id === selectedOrderId) ?? null;
  const updateMutation = useSessionMutation({
    mutationFn: ({ id, status }: { id: string; status: OrderStatus }) => updateOrderStatus(id, status),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.orders.lists(workspaceId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.orders.detail(workspaceId, variables.id) });
      toast.success('Estado de la cotización actualizado.');
    },
    onError: (error: unknown) => {
      toast.toastApiError(error);
    }
  });

  const getStatusBadge = (status: OrderStatus) => {
    switch (status) {
      case 'PendingReview': return <Badge className="flex items-center px-2.5 py-1 text-xs font-medium bg-yellow-100 text-yellow-800 rounded-full w-fit"><Clock className="w-3 h-3 mr-1" /> Por Confirmar</Badge>;
      case 'Approved': return <Badge className="flex items-center px-2.5 py-1 text-xs font-medium bg-blue-100 text-blue-800 rounded-full w-fit"><CheckCircle className="w-3 h-3 mr-1" /> Aprobado</Badge>;
      case 'Processing': return <Badge className="flex items-center px-2.5 py-1 text-xs font-medium bg-purple-100 text-purple-800 rounded-full w-fit"><Package className="w-3 h-3 mr-1" /> En Revisión</Badge>;
      case 'Completed': return <Badge className="flex items-center px-2.5 py-1 text-xs font-medium bg-emerald-100 text-emerald-800 rounded-full w-fit"><CheckCircle className="w-3 h-3 mr-1" /> Resuelto</Badge>;
      case 'Rejected': return <Badge className="flex items-center px-2.5 py-1 text-xs font-medium bg-red-100 text-red-800 rounded-full w-fit"><XCircle className="w-3 h-3 mr-1" /> Rechazado</Badge>;
      case 'Cancelled': return <Badge className="flex items-center px-2.5 py-1 text-xs font-medium bg-gray-100 text-gray-800 rounded-full w-fit"><XCircle className="w-3 h-3 mr-1" /> Cancelado</Badge>;
      default: return <Badge className="bg-gray-100 text-gray-800 text-xs px-2.5 py-1 rounded-full w-fit">{status}</Badge>;
    }
  };

  const handleStatusChange = (id: string, newStatus: OrderStatus) => {
    updateMutation.mutate({ id, status: newStatus });
  };

  if (isError) return <ErrorState title="Error al cargar las cotizaciones." onRetry={() => void refetch()} />;

  return (
    <div className="max-w-7xl mx-auto animate-in fade-in">

      <div className="mb-8 flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center">
            <ShoppingBag className="w-6 h-6 mr-3 text-blue-600" /> Solicitudes y Cotizaciones
          </h1>
          <p className="mt-1 text-sm text-gray-500">
            Revisa las listas de compra o consultas que la IA capturó y envía los precios a tus clientes.
          </p>
        </div>

        <select
          value={filterStatus}
          onChange={(e) => setFilterStatus(e.target.value as OrderStatus | 'ALL')}
          className="border border-gray-200 rounded-lg px-4 py-2.5 text-sm bg-white focus:ring-2 focus:ring-blue-500 outline-none shadow-sm"
        >
          <option value="ALL">Todas las Solicitudes</option>
          <option value="PendingReview">Por Confirmar</option>
          <option value="Approved">Aprobados</option>
          <option value="Processing">En Revisión</option>
          <option value="Completed">Resueltos</option>
        </select>
      </div>

      <div className="bg-white border border-gray-200 rounded-xl shadow-sm overflow-hidden min-h-[400px]">
        {isLoading ? (
          <LoadingState className="h-64" title="Cargando cotizaciones..." />
        ) : orders.length === 0 ? (
          <EmptyState className="py-20" icon={<ShoppingCart className="w-16 h-16 text-gray-200" />} title="No hay solicitudes pendientes" description="Las listas capturadas por el asistente aparecerán aquí." />
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
                      {order.totalAmountMinorUnits === null || order.currency === null
                        ? <span className="text-gray-400 italic font-normal text-sm">Por definir</span>
                        : `${order.currency} ${(order.totalAmountMinorUnits / 100).toFixed(2)}`}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      {getStatusBadge(order.status)}
                    </td>
                    <td className="px-6 py-4 text-right flex items-center justify-end gap-2">
                      <IconButton variant="ghost" label="Ver Detalles"
                        onClick={() => setSelectedOrder(order.id)}
                        className="p-2 text-gray-500 hover:text-blue-600 hover:bg-blue-50 rounded-lg transition-colors flex items-center"
                        title="Ver Detalles"
                      >
                        <Eye className="w-4 h-4" />
                      </IconButton>

                      <select
                        disabled={updateMutation.isPending || !can('ORDERS', 'UPDATE_STATUS') || orderTransitions[order.status].length === 0}
                        value={order.status}
                        onChange={(e) => handleStatusChange(order.id, e.target.value as OrderStatus)}
                        className="text-sm border border-gray-200 rounded-lg px-2 py-1.5 bg-white hover:bg-gray-50 outline-none font-medium text-gray-700 cursor-pointer"
                      >
                        <option value={order.status}>{order.status}</option>
                        {orderTransitions[order.status].map(status => <option key={status} value={status}>{status}</option>)}
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
