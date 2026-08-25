export interface CreateReservationRequest {
  locationId: string;
  serviceId: string;
  customerName: string; // 🔥 CORRECCIÓN: Ahora es obligatorio
  customerIdentifier: string;
  dateTime: string;
}

export interface ReservationDto {
  id: string;
  locationId: string;
  serviceId: string;
  customerName: string;
  customerIdentifier: string;
  dateTime: string;
  status: string; // Ej: 'Pending', 'Confirmed', 'Cancelled'
}