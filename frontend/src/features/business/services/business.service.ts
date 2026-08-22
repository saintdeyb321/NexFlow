import { axiosClient } from '../../../core/api/axiosClient';
import type { BusinessHoursDto, BusinessProfile, LocationDto, ServiceDto } from '../types/business.types';

// --- BUSINESS PROFILE ---
export const getBusinessProfile = async (): Promise<BusinessProfile> => {
  const { data } = await axiosClient.get<BusinessProfile>('/business/profile');
  return data;
};

export const updateBusinessProfile = async (profile: BusinessProfile): Promise<void> => {
  await axiosClient.put('/business/profile', profile);
};

// --- SERVICES ---
export const getServices = async (): Promise<ServiceDto[]> => {
  const { data } = await axiosClient.get<ServiceDto[]>('/business/services');
  return data;
};

export const saveService = async (service: ServiceDto): Promise<void> => {
  await axiosClient.post('/business/services', service);
};

export const deleteService = async (serviceId: string): Promise<void> => {
  await axiosClient.delete(`/business/services/${serviceId}`);
};

// --- LOCATIONS ---
export const getLocations = async (): Promise<LocationDto[]> => {
  const { data } = await axiosClient.get<LocationDto[]>('/business/locations');
  return data;
};

export const saveLocation = async (location: LocationDto): Promise<void> => {
  await axiosClient.post('/business/locations', location);
};

// --- HOURS ---
export const getBusinessHours = async (): Promise<BusinessHoursDto[]> => {
  const { data } = await axiosClient.get<BusinessHoursDto[]>('/business/hours');
  return data;
};

export const saveBusinessHours = async (hours: BusinessHoursDto[]): Promise<void> => {
  await axiosClient.put('/business/hours', hours);
};