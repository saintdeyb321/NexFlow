import { axiosClient } from '../../../core/api/axiosClient';
import type { RequestRecord } from '../types/request.types';

// 🔥 SPRINT 08: Añadimos locationId para aislar la data por sede
export const getRequests = async (locationId?: string): Promise<RequestRecord[]> => {
  const params = locationId && locationId !== 'all' && locationId !== 'global' ? { locationId } : {};
  const { data } = await axiosClient.get<RequestRecord[]>('/requests', { params });
  return data;
};

export const updateRequestStatus = async (id: string, status: string): Promise<void> => {
  await axiosClient.put(`/requests/${id}/status`, { status });
};