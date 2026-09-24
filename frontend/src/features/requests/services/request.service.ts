import { axiosClient } from '../../../core/api/axiosClient';
import type { RequestRecord, RequestStatus } from '../types/request.types';

export const getRequests = async (): Promise<RequestRecord[]> => {
  const { data } = await axiosClient.get<RequestRecord[]>('/requests');
  return data;
};

export const updateRequestStatus = async (requestId: string, status: RequestStatus): Promise<void> => {
  await axiosClient.put(`/requests/${requestId}/status`, { status });
};