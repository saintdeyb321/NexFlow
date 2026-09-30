export interface CreateReservationRequest {
  locationId: string;
  serviceId: string;
  customerName: string; 
  customerIdentifier: string;
  dateTime: string;
}

export interface ReservationDto {
  id: string;
  workspaceId: string; 
  locationId: string;
  serviceId: string;
  customerName: string;
  customerIdentifier: string;
  dateTime: string;
  status: 'Confirmed' | 'Cancelled' | 'Completed' | 'Pending';
}
