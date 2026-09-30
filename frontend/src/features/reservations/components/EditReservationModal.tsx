import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { toBusinessLocalInput } from '../../../core/utils/dateTime';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { useState, useEffect } from 'react';
import { editReservation } from '../services/reservation.service';
import type { ReservationDto } from '../types/reservation.types';

interface EditReservationModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (reservation: ReservationDto) => void;
  reservation: ReservationDto | null;
  timeZone: string; 
}

export const EditReservationModal = ({ isOpen, onClose, onSuccess, reservation, timeZone }: EditReservationModalProps) => {
  const [editDate, setEditDate] = useState('');
  const [editTime, setEditTime] = useState('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    if (reservation) {
      // 🔥 SPRINT 11: Se eliminaron los "as any"
      const local = toBusinessLocalInput(reservation.dateTime, timeZone);
      setEditDate(local.split('T')[0] || '');
      setEditTime(local.split('T')[1] || '');
      setErrorMessage(null);
    }
  }, [reservation, timeZone]);

  const saveMutation = useSessionMutation({
    mutationFn: ({ id, dateTime }: { id: string; dateTime: string }) => editReservation(id, dateTime),
    onSuccess: result => { onSuccess(result); onClose(); },
    onError: error => setErrorMessage(getApiErrorPresentation(error)),
  });
  const isSaving = saveMutation.isPending;

  if (!isOpen || !reservation) return null;

  const handleSaveEdit = async () => {
    if (!editDate || !editTime) return;
    saveMutation.mutate({ id: reservation.id, dateTime: `${editDate}T${editTime}:00` });
  };

  return (
    <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4">
      <div className="bg-white rounded-xl shadow-2xl p-6 max-w-sm w-full animate-in fade-in zoom-in-95">
        <h3 className="text-lg font-bold text-gray-900 mb-1">Reagendar Cita</h3>
        <p className="text-sm text-gray-500 mb-5">
          Cliente: <span className="font-semibold text-gray-700">{reservation.customerName}</span>
        </p>
        
        {errorMessage && <p role="alert">{errorMessage}</p>}
        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Nueva Fecha</label>
            <input 
              type="date" 
              value={editDate} 
              onChange={(e) => setEditDate(e.target.value)}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 outline-none"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Nueva Hora (HH:mm)</label>
            <input 
              type="time" 
              value={editTime} 
              onChange={(e) => setEditTime(e.target.value)}
              className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-blue-500 outline-none"
            />
          </div>
        </div>

        <div className="mt-8 flex justify-end gap-3">
          <button onClick={onClose} className="px-4 py-2 text-sm font-medium text-gray-600 bg-gray-100 rounded-lg hover:bg-gray-200 transition-colors">
            Cancelar
          </button>
          <button onClick={handleSaveEdit} disabled={isSaving} className="px-4 py-2 text-sm font-medium text-white bg-blue-600 rounded-lg hover:bg-blue-700 transition-colors disabled:opacity-50">
            {isSaving ? 'Guardando...' : 'Confirmar Cambio'}
          </button>
        </div>
      </div>
    </div>
  );
};