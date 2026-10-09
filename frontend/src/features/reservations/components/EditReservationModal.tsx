import { Button } from '../../../components/ui/Button';
import { Input, FormField } from '../../../components/ui/Form';
import { Modal } from '../../../components/ui/Modal';
import { Alert } from '../../../components/ui/Feedback';
import { useToast } from '../../../components/ui/useToast';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { toBusinessLocalInput } from '../../../core/utils/dateTime';
import { useState } from 'react';
import type { ReactNode } from 'react';
import { editReservation } from '../services/reservation.service';
import type { ReservationDto } from '../types/reservation.types';

interface EditReservationModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (reservation: ReservationDto) => void;
  reservation: ReservationDto | null;
  timeZone: string;
  timeZoneBlocked: boolean;
  timeZoneStatus: ReactNode;
}

export const EditReservationModal = (props: EditReservationModalProps) => {
  if (!props.isOpen || !props.reservation) return null;
  return <EditReservationForm key={`${props.reservation.id}:${props.reservation.dateTime}`} {...props} reservation={props.reservation} />;
};

const EditReservationForm = ({ isOpen, onClose, onSuccess, reservation, timeZone, timeZoneBlocked, timeZoneStatus }: EditReservationModalProps & { reservation: ReservationDto }) => {
  const toast = useToast();
  const local = toBusinessLocalInput(reservation.dateTime, timeZone);
  const [editDate, setEditDate] = useState(local.split('T')[0] || '');
  const [editTime, setEditTime] = useState(local.split('T')[1] || '');
  const [formTimeZone, setFormTimeZone] = useState(timeZone);
  const [zoneChanged, setZoneChanged] = useState(false);
  if (formTimeZone !== timeZone) {
    setFormTimeZone(timeZone);
    setZoneChanged(true);
    setEditDate(local.split('T')[0] || '');
    setEditTime('');
  }

  const saveMutation = useSessionMutation({
    mutationFn: ({ id, dateTime }: { id: string; dateTime: string }) => editReservation(id, dateTime),
    onSuccess: result => { onSuccess(result); onClose(); },
    onError: error => toast.toastApiError(error),
  });
  const isSaving = saveMutation.isPending;

  if (!isOpen || !reservation) return null;

  const handleSaveEdit = async () => {
    if (timeZoneBlocked || isSaving || !editDate || !editTime) return;
    saveMutation.mutate({ id: reservation.id, dateTime: `${editDate}T${editTime}:00` });
  };

  return (
    <Modal isOpen={isOpen} onClose={onClose} title="Reagendar Cita" size="lg" closeDisabled={isSaving}>
        {timeZoneBlocked && timeZoneStatus}
        {!timeZoneBlocked && zoneChanged && <Alert tone="warning" className="mb-4">La zona horaria cambió. Revisa la fecha de la reserva y selecciona una hora nueva.</Alert>}
        <p className="text-sm text-muted mb-5">
          Cliente: <span className="font-semibold text-gray-700">{reservation.customerName}</span>
        </p>

        <fieldset className="space-y-4" disabled={timeZoneBlocked || isSaving} hidden={timeZoneBlocked}>
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
        </fieldset>

        <div className="mt-8 flex justify-end gap-3">
          <Button variant="secondary"  disabled={isSaving} onClick={onClose} className="text-sm font-medium transition-colors">
            Cancelar
          </Button>
          <Button variant="primary" isLoading={isSaving} onClick={handleSaveEdit} disabled={timeZoneBlocked || isSaving || !editDate || !editTime} className="text-sm font-medium transition-colors disabled:opacity-50">
            {isSaving ? 'Guardando...' : 'Confirmar Cambio'}
          </Button>
        </div>
    </Modal>
  );
};
