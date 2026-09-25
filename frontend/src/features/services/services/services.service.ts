import { axiosClient } from '../../../core/api/axiosClient';
import type { ServiceDto, ServiceCategoryDto } from '../types/services.types';

export const getServices = async (locationId?: string): Promise<ServiceDto[]> => {
  const params = locationId && locationId !== 'all' ? { locationId } : {};
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

export const getServiceCategories = async (): Promise<ServiceCategoryDto[]> => {
  const { data } = await axiosClient.get<ServiceCategoryDto[]>('/catalog/categories', { params: { scope: 'SERVICE' } });
  return data;
};

export const saveCategory = async (category: ServiceCategoryDto): Promise<ServiceCategoryDto> => {
  const { data } = await axiosClient.post<ServiceCategoryDto>('/catalog/categories', category);
  return data;
}