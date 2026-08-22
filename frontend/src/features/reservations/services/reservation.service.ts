import { axiosClient } from '../../../core/api/axiosClient';
import type { ReservationDto } from '../types/reservation.types';

// Ahora pasamos la sede y la fecha para consultar el calendario
export const getReservations = async (locationId: string, date: string): Promise<ReservationDto[]> => {
  const { data } = await axiosClient.get<ReservationDto[]>(`/reservations?locationId=${locationId}&date=${date}`);
  return data;
};

export const createReservation = async (payload: { locationId: string, serviceId: string, customerIdentifier: string, dateTime: string }): Promise<ReservationDto> => {
  const { data } = await axiosClient.post<ReservationDto>(`/reservations`, payload);
  return data;
};

export const cancelReservation = async (reservationId: string): Promise<void> => {
  await axiosClient.delete(`/reservations/${reservationId}`);
};