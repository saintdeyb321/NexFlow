import { axiosClient } from '../../../core/api/axiosClient';
import type { BusinessHoursDto, BusinessProfile, LocationDto, ServiceDto } from '../types/business.types';

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

// 🔥 CORRECCIÓN: El backend ahora retorna la entidad, la atrapamos.
export const saveService = async (service: ServiceDto): Promise<ServiceDto> => {
  const { data } = await axiosClient.post<ServiceDto>('/business/services', service);
  return data;
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

// --- HOURS & ONBOARDING ---
export const getBusinessHours = async (locationId: string): Promise<BusinessHoursDto[]> => {
  const { data } = await axiosClient.get(`/business/locations/${locationId}/hours`);
  return data;
};

export const saveBusinessHours = async (locationId: string, hours: BusinessHoursDto[]): Promise<void> => {
  await axiosClient.put(`/business/locations/${locationId}/hours`, hours);
};

export const completeBusinessOnboarding = async (): Promise<void> => {
  await axiosClient.post('/business/complete-onboarding');
};