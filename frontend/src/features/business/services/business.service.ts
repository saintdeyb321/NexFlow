import { axiosClient } from '../../../core/api/axiosClient';
import type { BusinessHoursDto, BusinessProfile, LocationDto, ServiceDto } from '../types/business.types';

// --- BUSINESS PROFILE ---
export const getBusinessProfile = async (workspaceId: string): Promise<BusinessProfile> => {
  const { data } = await axiosClient.get<BusinessProfile>(`/workspaces/${workspaceId}/business/profile`);
  return data;
};

export const updateBusinessProfile = async (workspaceId: string, profile: BusinessProfile): Promise<void> => {
  await axiosClient.put(`/workspaces/${workspaceId}/business/profile`, profile);
};

// --- SERVICES ---
export const getServices = async (workspaceId: string): Promise<ServiceDto[]> => {
  const { data } = await axiosClient.get<ServiceDto[]>(`/workspaces/${workspaceId}/business/services`);
  return data;
};

export const saveService = async (workspaceId: string, service: ServiceDto): Promise<void> => {
  await axiosClient.post(`/workspaces/${workspaceId}/business/services`, service);
};

export const deleteService = async (workspaceId: string, serviceId: string): Promise<void> => {
  await axiosClient.delete(`/workspaces/${workspaceId}/business/services/${serviceId}`);
};

// --- LOCATIONS ---
export const getLocations = async (workspaceId: string): Promise<LocationDto[]> => {
  const { data } = await axiosClient.get<LocationDto[]>(`/workspaces/${workspaceId}/business/locations`);
  return data;
};

export const saveLocation = async (workspaceId: string, location: LocationDto): Promise<void> => {
  await axiosClient.post(`/workspaces/${workspaceId}/business/locations`, location);
};

// --- HOURS ---
export const getBusinessHours = async (workspaceId: string): Promise<BusinessHoursDto[]> => {
  const { data } = await axiosClient.get<BusinessHoursDto[]>(`/workspaces/${workspaceId}/business/hours`);
  return data;
};

export const saveBusinessHours = async (workspaceId: string, hours: BusinessHoursDto[]): Promise<void> => {
  await axiosClient.put(`/workspaces/${workspaceId}/business/hours`, hours);
};