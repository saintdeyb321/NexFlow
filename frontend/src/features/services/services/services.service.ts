import { axiosClient } from '../../../core/api/axiosClient';
import type { ServiceDto } from '../types/services.types';

export const getServices = async (locationId?: string): Promise<ServiceDto[]> => {
  const params = locationId && locationId !== 'all' ? { locationId } : {};
  // 🔥 Apunta directamente al nuevo endpoint del backend
  const { data } = await axiosClient.get<ServiceDto[]>('/services', { params });
  return data;
};

export const saveService = async (service: ServiceDto): Promise<ServiceDto> => {
  const { data } = await axiosClient.post<ServiceDto>('/services', service);
  return data;
};

export const deleteService = async (serviceId: string): Promise<void> => {
  await axiosClient.delete(`/services/${serviceId}`);
};