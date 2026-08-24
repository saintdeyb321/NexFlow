import { axiosClient } from '../../../core/api/axiosClient';
import type { RequestRecord } from '../types/request.types';

export const getRequests = async (): Promise<RequestRecord[]> => {
  const { data } = await axiosClient.get<RequestRecord[]>('/requests');
  return data;
};

export const updateRequestStatus = async (id: string, status: string): Promise<void> => {
  await axiosClient.put(`/requests/${id}/status`, { status });
};