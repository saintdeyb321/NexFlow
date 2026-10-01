import { Button } from '../../../components/ui/Button';
import { Modal } from '../../../components/ui/Modal';
import { useNavigate } from 'react-router-dom';
import { ShoppingCart, MessageSquare, User, Calendar } from 'lucide-react';
import type { OrderRecord } from '../types/orders.types';

interface OrderDetailModalProps {
  order: OrderRecord;
  onClose: () => void;
}

export const OrderDetailModal = ({ order, onClose }: OrderDetailModalProps) => {
  const navigate = useNavigate();

  const formatCurrency = (minorUnits: number | null, currency: string | null) =>
    minorUnits === null || currency === null ? 'Total no disponible' : `${currency} ${(minorUnits / 100).toFixed(2)}`;

  return (
    <Modal isOpen onClose={onClose} size="xl" title={<span className="flex items-center"><ShoppingCart aria-hidden="true" className="w-5 h-5 mr-2 text-primary" />Detalle del pedido</span>}>
        <div>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mb-6 bg-surface-soft p-4 rounded-lg border border-line">
            <div>
              <p className="text-xs text-muted uppercase tracking-wide mb-1 flex items-center">
                <User aria-hidden="true" className="w-3 h-3 mr-1" /> Cliente
              </p>
              <p className="font-semibold text-foreground">{order.consumerName}</p>
              <p className="text-sm text-muted">{order.consumerPhone}</p>
            </div>
            <div>
              <p className="text-xs text-muted uppercase tracking-wide mb-1 flex items-center">
                <Calendar aria-hidden="true" className="w-3 h-3 mr-1" /> Fecha
              </p>
              <p className="font-semibold text-foreground">
                {new Date(order.createdAt).toLocaleDateString()}
              </p>
              <p className="text-sm text-muted">
                {new Date(order.createdAt).toLocaleTimeString()}
              </p>
            </div>
          </div>

          <h3 className="font-bold text-foreground mb-3 border-b border-line pb-2">Productos solicitados</h3>
          <div className="max-h-60 overflow-y-auto mb-4">
            <table className="nf-table nf-responsive-table">
              <thead className="bg-surface-soft sticky top-0">
                <tr className="text-muted">
                  <th className="py-2 px-3 font-medium">Producto</th>
                  <th className="py-2 px-3 font-medium text-center">Cant.</th>
                  <th className="py-2 px-3 font-medium text-right">Precio Unit.</th>
                  <th className="py-2 px-3 font-medium text-right">Subtotal</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {order.items.map((item, idx) => (
                  <tr key={idx} className="hover:bg-surface-soft">
                    <td data-label="Producto" className="py-3 px-3 font-medium text-foreground">{item.productName}</td>
                    <td data-label="Cantidad" className="py-3 px-3 text-center text-muted">{item.quantity}</td>
                    <td data-label="Precio unitario" className="py-3 px-3 text-right text-muted">
                      {formatCurrency(item.unitPriceMinorUnits, item.currency)}
                    </td>
                    <td data-label="Subtotal" className="py-3 px-3 text-right font-medium text-foreground">
                      {formatCurrency(item.quantity * item.unitPriceMinorUnits, item.currency)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="flex flex-col sm:flex-row gap-4 justify-between items-start border-t border-line pt-4 mt-2">
            <div className="w-full sm:w-1/2 min-w-0">
              {order.notes && (
                <>
                  <p className="text-xs text-muted uppercase font-medium">Notas del Sistema:</p>
                  <p className="text-sm text-gray-700 italic bg-yellow-50 p-2 rounded border border-yellow-100 mt-1">"{order.notes}"</p>
                </>
              )}
            </div>

            <div className="text-right flex flex-col items-end">
              <p className="text-sm text-muted mb-1">Total del pedido</p>

              <p className="text-2xl font-bold text-primary">{formatCurrency(order.totalAmountMinorUnits, order.currency)}</p>
            </div>
          </div>
        </div>

        <div className="p-4 border-t border-line bg-surface-soft flex flex-wrap gap-2 justify-between items-center">
          {order.conversationId && order.conversationId !== 'MANUAL_ENTRY' ? (
            <Button variant="ghost"
              onClick={() => { onClose(); navigate(`/inbox?conversation=${order.conversationId}`); }}
              className="flex items-center text-sm font-medium transition-colors"
            >
              <MessageSquare aria-hidden="true" className="w-4 h-4 mr-2" /> Ir a la conversación
            </Button>
          ) : <div></div>}

          <Button variant="primary" onClick={onClose} className="font-medium transition-colors">
            Cerrar
          </Button>
        </div>

    </Modal>
  );
};
