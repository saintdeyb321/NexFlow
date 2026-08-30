import { axiosClient } from '../../../core/api/axiosClient';
import type { CreateReservationRequest, ReservationDto } from '../types/reservation.types';

export interface TimeSlotDto {
  startTime: string;
  endTime: string;
  isAvailable: boolean;
}

export const getReservations = async (locationId: string, date: string): Promise<ReservationDto[]> => {
  const { data } = await axiosClient.get<ReservationDto[]>(`/reservations?locationId=${locationId}&date=${date}`);
  return data;
};

// 🔥 CORRECCIÓN: Conectamos la lectura de disponibilidad al backend
export const getAvailability = async (locationId: string, serviceId: string, date: string): Promise<TimeSlotDto[]> => {
  const { data } = await axiosClient.get<TimeSlotDto[]>(`/reservations/availability?locationId=${locationId}&serviceId=${serviceId}&date=${date}`);
  return data;
};

export const createReservation = async (request: CreateReservationRequest): Promise<ReservationDto> => {
  const { data } = await axiosClient.post<ReservationDto>('/reservations', request);
  return data;
};

// 🔥 CORRECCIÓN (Fallo #45): Conectamos la función para reagendar
export const editReservation = async (reservationId: string, newDateTime: string): Promise<ReservationDto> => {
  const { data } = await axiosClient.put<ReservationDto>(`/reservations/${reservationId}`, { newDateTime });
  return data;
};

export const cancelReservation = async (reservationId: string): Promise<void> => {
  await axiosClient.delete(`/reservations/${reservationId}`);
};

export const completeReservation = async (reservationId: string): Promise<void> => {
  // Ajusta la ruta si tu backend tiene un endpoint diferente (ej: /reservations/{id}/complete)
  await axiosClient.put(`/reservations/${reservationId}/status`, { status: 'Completed' });
};