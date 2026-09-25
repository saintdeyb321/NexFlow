import { axiosClient } from '../../../core/api/axiosClient';
import type { RequestRecord, RequestStatus, CreateRequestDto } from '../types/request.types';

export const getRequests = async (): Promise<RequestRecord[]> => {
  const { data } = await axiosClient.get<RequestRecord[]>('/requests');
  return data;
};

// 🔥 Nuevo endpoint para creación manual
export const createRequest = async (payload: CreateRequestDto): Promise<{id: string, message: string}> => {
  const { data } = await axiosClient.post<{id: string, message: string}>('/requests', payload);
  return data;
};

export const updateRequestStatus = async (requestId: string, status: RequestStatus): Promise<void> => {
  await axiosClient.put(`/requests/${requestId}/status`, { status });
};