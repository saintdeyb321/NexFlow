import { getStatusTone, getStatusLabel } from '../../../components/ui/statusPresentation';
import { PageHeader } from '../../../components/ui/Layout';
import { IconButton } from '../../../components/ui/Button';
import { LoadingState, EmptyState, ErrorState, StatusBadge } from '../../../components/ui/Feedback';
import { useToast } from '../../../components/ui/useToast';
import { queryPolicies, usePageVisible } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { orderTransitions } from '../types/orders.types';
import { usePermissions } from '../../../core/auth/permissions';

import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ShoppingBag, Eye, ShoppingCart } from 'lucide-react';
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

  const getStatusBadge = (status: OrderStatus) => <StatusBadge tone={getStatusTone(status)} label={{ PendingReview: 'Por confirmar', Approved: 'Aprobado', Processing: 'En proceso', Completed: 'Completado', Rejected: 'Rechazado', Cancelled: 'Cancelado' }[status]} />;
  const handleStatusChange = (id: string, newStatus: OrderStatus) => {
    updateMutation.mutate({ id, status: newStatus });
  };

  if (isError) return <ErrorState title="Error al cargar las pedidos." onRetry={() => void refetch()} />;

  return (
    <div className="nf-page">

      <PageHeader title="Pedidos comerciales" description="Revisa los productos solicitados por tus clientes y su estado de atención." icon={<ShoppingBag aria-hidden="true" className="w-5 h-5" />} actions={<><select aria-label="Filtrar pedidos por estado"
          value={filterStatus}
          onChange={(e) => setFilterStatus(e.target.value as OrderStatus | 'ALL')}
          className="nf-control max-w-full"
        >
          <option value="ALL">Todas las Solicitudes</option>
          <option value="PendingReview">Por Confirmar</option>
          <option value="Approved">Aprobados</option>
          <option value="Processing">En Revisión</option>
          <option value="Completed">Resueltos</option>
        </select></>} />

      <div className="bg-surface border border-line rounded-xl shadow-sm overflow-hidden min-h-[400px]">
        {isLoading ? (
          <LoadingState className="h-64" title="Cargando pedidos..." />
        ) : orders.length === 0 ? (
          <EmptyState className="py-20" icon={<ShoppingCart aria-hidden="true" className="w-16 h-16 text-gray-200" />} title="No hay solicitudes pendientes" description="Las listas capturadas por el asistente aparecerán aquí." />
        ) : (
          <div className="overflow-x-auto">
            <table className="nf-table nf-responsive-table">
              <thead>
                <tr className="bg-surface-soft border-b border-line text-xs font-bold text-muted uppercase tracking-wider">
                  <th className="px-6 py-4">Fecha</th>
                  <th className="px-6 py-4">Cliente</th>
                  <th className="px-6 py-4">Artículos</th>
                  <th className="px-6 py-4">Total calculado</th>
                  <th className="px-6 py-4">Estado</th>
                  <th className="px-6 py-4 text-right">Acciones</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {orders.map((order) => (
                  <tr key={order.id} className="hover:bg-surface-soft/80 transition-colors">
                    <td data-label="Fecha" className="px-6 py-4 text-sm text-muted">
                      <span className="font-medium text-foreground block">{new Date(order.createdAt).toLocaleDateString()}</span>
                      <span className="text-xs text-gray-400">{new Date(order.createdAt).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}</span>
                    </td>
                    <td data-label="Cliente" className="px-6 py-4">
                      <span className="font-semibold text-foreground block">{order.consumerName || 'Cliente'}</span>
                      <span className="text-sm text-muted">{order.consumerPhone}</span>
                    </td>
                    <td data-label="Artículos" className="px-6 py-4 text-sm text-muted">
                      <span className="font-medium text-primary bg-blue-50 px-2 py-0.5 rounded-full inline-block mb-1">
                        {order.items.reduce((acc, item) => acc + item.quantity, 0)} ítems
                      </span>
                      <p className="text-xs text-muted truncate max-w-[200px]">
                        {order.items.map(i => i.productName).join(', ')}
                      </p>
                    </td>
                    <td data-label="Total" className="px-6 py-4 font-bold text-foreground">
                      {order.totalAmountMinorUnits === null || order.currency === null
                        ? <span className="text-gray-400 italic font-normal text-sm">Total no disponible</span>
                        : `${order.currency} ${(order.totalAmountMinorUnits / 100).toFixed(2)}`}
                    </td>
                    <td data-label="Estado" className="px-6 py-4">
                      {getStatusBadge(order.status)}
                    </td>
                    <td data-label="Acciones" className="px-6 py-4 text-right flex items-center justify-end gap-2">
                      <IconButton variant="ghost" label="Ver Detalles"
                        onClick={() => setSelectedOrder(order.id)}
                        className="transition-colors flex items-center"
                        title="Ver Detalles"
                      >
                        <Eye aria-hidden="true" className="w-4 h-4" />
                      </IconButton>

                      <select aria-label="Cambiar estado de pedido"
                        disabled={updateMutation.isPending || !can('ORDERS', 'UPDATE_STATUS') || orderTransitions[order.status].length === 0}
                        value={order.status}
                        onChange={(e) => handleStatusChange(order.id, e.target.value as OrderStatus)}
                        className="nf-control max-w-full"
                      >
                        <option value={order.status}>{getStatusLabel(order.status)}</option>
                        {orderTransitions[order.status].map(status => <option key={status} value={status}>{getStatusLabel(status)}</option>)}
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
