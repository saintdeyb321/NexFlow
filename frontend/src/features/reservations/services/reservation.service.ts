import { axiosClient } from '../../../core/api/axiosClient';
import type { ReservationDto } from '../types/reservation.types';

export const getReservations = async (workspaceId: string): Promise<ReservationDto[]> => {
  // Asumimos que tu controlador de C# escuchará en esta ruta
  const { data } = await axiosClient.get<ReservationDto[]>(`/workspaces/${workspaceId}/reservations`);
  return data;
};

// Función para cancelar o confirmar (opcional para el MVP, pero buena práctica)
export const updateReservationStatus = async (workspaceId: string, reservationId: string, status: string): Promise<void> => {
  await axiosClient.patch(`/workspaces/${workspaceId}/reservations/${reservationId}/status`, { status });
};