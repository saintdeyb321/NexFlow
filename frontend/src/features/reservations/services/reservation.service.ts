import { axiosClient } from '../../../core/api/axiosClient';
import type { CreateReservationRequest, ReservationContextDto, ReservationDto } from '../types/reservation.types';

export const getReservationContext = async (signal?: AbortSignal): Promise<ReservationContextDto> => {
  const { data } = await axiosClient.get<ReservationContextDto>('/reservations/context', { signal });
  // Do not turn a failed or incompatible metadata read into a guessed business zone.
  const zone = data?.timeZone;
  if (typeof zone !== 'string' || !zone || /^[+-]/.test(zone))
    throw new Error('La zona horaria de la agenda no es compatible con este navegador.');
  try { new Intl.DateTimeFormat('es-PE', { timeZone: zone }).format(0); }
  catch { throw new Error('La zona horaria de la agenda no es compatible con este navegador.'); }
  return { timeZone: zone };
};

export interface TimeSlotDto {
  startTime: string;
  endTime: string;
  isAvailable: boolean;
}

export const getReservations = async (locationId: string, date: string, signal?: AbortSignal): Promise<ReservationDto[]> => {
  const { data } = await axiosClient.get<ReservationDto[]>(`/reservations?locationId=${locationId}&date=${date}`, { signal });
  return data;
};

export const getReservationsForWeek = async (locationId: string, from: string, to: string, signal?: AbortSignal, timeZone?: string): Promise<ReservationDto[]> => {
  const { data } = await axiosClient.get<ReservationDto[]>('/reservations', { params: { locationId, from, to, ...(timeZone ? { timeZone } : {}) }, signal });
  return data;
};

export const getAvailability = async (locationId: string, serviceId: string, date: string, signal?: AbortSignal): Promise<TimeSlotDto[]> => {
  const { data } = await axiosClient.get<TimeSlotDto[]>(`/reservations/availability?locationId=${locationId}&serviceId=${serviceId}&date=${date}`, { signal });
  return data;
};

export const createReservation = async (request: CreateReservationRequest): Promise<ReservationDto> => {
  const { data } = await axiosClient.post<ReservationDto>('/reservations', request);
  return data;
};

export const editReservation = async (reservationId: string, newDateTime: string): Promise<ReservationDto> => {
  const { data } = await axiosClient.put<ReservationDto>(`/reservations/${reservationId}`, { newDateTime });
  return data;
};

export const cancelReservation = async (reservationId: string): Promise<void> => {
  await axiosClient.delete(`/reservations/${reservationId}`);
};

export const completeReservation = async (reservationId: string): Promise<void> => {
  await axiosClient.put(`/reservations/${reservationId}/status`, { status: 'Completed' });
};
