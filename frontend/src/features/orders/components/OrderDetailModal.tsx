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
    minorUnits === null || currency === null ? '—' : `${currency} ${(minorUnits / 100).toFixed(2)}`;

  return (
    <Modal isOpen onClose={onClose} size="xl" title={<span className="flex items-center"><ShoppingCart className="w-5 h-5 mr-2 text-blue-600" />Detalle de la Cotización</span>}>
        <div>
          <div className="grid grid-cols-2 gap-6 mb-6 bg-gray-50 p-4 rounded-lg border border-gray-100">
            <div>
              <p className="text-xs text-gray-500 uppercase tracking-wide mb-1 flex items-center">
                <User className="w-3 h-3 mr-1" /> Cliente
              </p>
              <p className="font-semibold text-gray-900">{order.consumerName}</p>
              <p className="text-sm text-gray-600">{order.consumerPhone}</p>
            </div>
            <div>
              <p className="text-xs text-gray-500 uppercase tracking-wide mb-1 flex items-center">
                <Calendar className="w-3 h-3 mr-1" /> Fecha
              </p>
              <p className="font-semibold text-gray-900">
                {new Date(order.createdAt).toLocaleDateString()}
              </p>
              <p className="text-sm text-gray-600">
                {new Date(order.createdAt).toLocaleTimeString()}
              </p>
            </div>
          </div>

          <h3 className="font-bold text-gray-800 mb-3 border-b border-gray-100 pb-2">Artículos Cotizados</h3>
          <div className="max-h-60 overflow-y-auto mb-4">
            <table className="w-full text-left text-sm">
              <thead className="bg-gray-50 sticky top-0">
                <tr className="text-gray-500">
                  <th className="py-2 px-3 font-medium">Producto</th>
                  <th className="py-2 px-3 font-medium text-center">Cant.</th>
                  <th className="py-2 px-3 font-medium text-right">Precio Unit.</th>
                  <th className="py-2 px-3 font-medium text-right">Subtotal</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {order.items.map((item, idx) => (
                  <tr key={idx} className="hover:bg-gray-50">
                    <td className="py-3 px-3 font-medium text-gray-900">{item.productName}</td>
                    <td className="py-3 px-3 text-center text-gray-600">{item.quantity}</td>
                    <td className="py-3 px-3 text-right text-gray-600">
                      {formatCurrency(item.unitPriceMinorUnits, item.currency)}
                    </td>
                    <td className="py-3 px-3 text-right font-medium text-gray-900">
                      {formatCurrency(item.quantity * item.unitPriceMinorUnits, item.currency)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="flex justify-between items-start border-t border-gray-200 pt-4 mt-2">
            <div className="w-1/2">
              {order.notes && (
                <>
                  <p className="text-xs text-gray-500 uppercase font-medium">Notas del Sistema:</p>
                  <p className="text-sm text-gray-700 italic bg-yellow-50 p-2 rounded border border-yellow-100 mt-1">"{order.notes}"</p>
                </>
              )}
            </div>

            <div className="text-right flex flex-col items-end">
              <p className="text-sm text-gray-500 mb-1">Total de Cotización</p>

              <p className="text-2xl font-bold text-blue-600">{formatCurrency(order.totalAmountMinorUnits, order.currency)}</p>
            </div>
          </div>
        </div>

        <div className="p-4 border-t border-gray-100 bg-gray-50 flex justify-between items-center">
          {order.conversationId && order.conversationId !== 'MANUAL_ENTRY' ? (
            <Button variant="ghost"
              onClick={() => { onClose(); navigate(`/inbox?conversation=${order.conversationId}`); }}
              className="flex items-center text-sm text-blue-600 font-medium hover:text-blue-800 transition-colors"
            >
              <MessageSquare className="w-4 h-4 mr-2" /> Ir a la conversación
            </Button>
          ) : <div></div>}

          <Button variant="primary" onClick={onClose} className="px-5 py-2 bg-gray-800 text-white font-medium rounded-lg hover:bg-gray-900 transition-colors">
            Cerrar
          </Button>
        </div>

    </Modal>
  );
};
