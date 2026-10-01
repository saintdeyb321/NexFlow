import { Button } from '../../../components/ui/Button';
import { Input, FormField } from '../../../components/ui/Form';
import { Modal } from '../../../components/ui/Modal';
import { useToast } from '../../../components/ui/Toast';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { toBusinessLocalInput } from '../../../core/utils/dateTime';
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
  const toast = useToast();
  const [editDate, setEditDate] = useState('');
  const [editTime, setEditTime] = useState('');

  useEffect(() => {
    if (reservation) {
      const local = toBusinessLocalInput(reservation.dateTime, timeZone);
      setEditDate(local.split('T')[0] || '');
      setEditTime(local.split('T')[1] || '');
    }
  }, [reservation, timeZone]);

  const saveMutation = useSessionMutation({
    mutationFn: ({ id, dateTime }: { id: string; dateTime: string }) => editReservation(id, dateTime),
    onSuccess: result => { onSuccess(result); onClose(); },
    onError: error => toast.toastApiError(error),
  });
  const isSaving = saveMutation.isPending;

  if (!isOpen || !reservation) return null;

  const handleSaveEdit = async () => {
    if (!editDate || !editTime) return;
    saveMutation.mutate({ id: reservation.id, dateTime: `${editDate}T${editTime}:00` });
  };

  return (
    <Modal isOpen={isOpen} onClose={onClose} title="Reagendar Cita" size="lg" closeDisabled={isSaving}>
        <p className="text-sm text-muted mb-5">
          Cliente: <span className="font-semibold text-gray-700">{reservation.customerName}</span>
        </p>

        <div className="space-y-4">
          <FormField label="Nueva Fecha">
            <Input
              type="date"
              value={editDate}
              onChange={(e) => setEditDate(e.target.value)}
              className="w-full border text-sm focus:ring-primary"
            />
          </FormField>
          <FormField label="Nueva Hora (HH:mm)">
            <Input
              type="time"
              value={editTime}
              onChange={(e) => setEditTime(e.target.value)}
              className="w-full border text-sm focus:ring-primary"
            />
          </FormField>
        </div>

        <div className="mt-8 flex justify-end gap-3">
          <Button variant="secondary"  disabled={isSaving} onClick={onClose} className="text-sm font-medium transition-colors">
            Cancelar
          </Button>
          <Button variant="primary" isLoading={isSaving} onClick={handleSaveEdit} disabled={isSaving} className="text-sm font-medium transition-colors disabled:opacity-50">
            {isSaving ? 'Guardando...' : 'Confirmar Cambio'}
          </Button>
        </div>
    </Modal>
  );
};
