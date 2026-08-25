import { axiosClient } from '../../../core/api/axiosClient';
import type { CreateReservationRequest, ReservationDto } from '../types/reservation.types';

// 🔥 CORRECCIÓN: Volvemos a pedir LocationId y Date porque la API los necesita para filtrar
export const getReservations = async (locationId: string, date: string): Promise<ReservationDto[]> => {
  // Ajusta la URL según cómo la espera tu backend (query params o path params)
  const { data } = await axiosClient.get<ReservationDto[]>(`/reservations?locationId=${locationId}&date=${date}`);
  return data;
};

export const createReservation = async (request: CreateReservationRequest): Promise<ReservationDto> => {
  const { data } = await axiosClient.post<ReservationDto>('/reservations', request);
  return data;
};

export const cancelReservation = async (reservationId: string): Promise<void> => {
  await axiosClient.delete(`/reservations/${reservationId}`);
};