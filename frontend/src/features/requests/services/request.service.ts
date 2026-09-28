import { axiosClient } from '../../../core/api/axiosClient';
import type { RequestRecord, RequestStatus, CreateRequestDto } from '../types/request.types';

// 🔥 SPRINT 11: Se añade limit y status para paginación/filtrado nativo
export const getRequests = async (limit: number = 50, status?: string): Promise<RequestRecord[]> => {
  const params = new URLSearchParams();
  params.append('limit', limit.toString());
  if (status) params.append('status', status);

  const { data } = await axiosClient.get<RequestRecord[]>(`/requests?${params.toString()}`);
  return data;
};

export const createRequest = async (payload: CreateRequestDto): Promise<{id: string, message: string}> => {
  const { data } = await axiosClient.post<{id: string, message: string}>('/requests', payload);
  return data;
};

export const updateRequestStatus = async (requestId: string, status: RequestStatus): Promise<void> => {
  await axiosClient.put(`/requests/${requestId}/status`, { status });
};

// 🔥 SPRINT 11: Nueva función para asignar responsable
export const assignRequest = async (requestId: string, assignedTo: string): Promise<void> => {
  await axiosClient.put(`/requests/${requestId}/assign`, { assignedTo });
};