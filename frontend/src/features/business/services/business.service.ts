import { axiosClient } from '../../../core/api/axiosClient';
import type { BusinessHoursDto, BusinessProfile, LocationDto } from '../types/business.types';
import type { CatalogItemDto } from '../../catalog/types/catalog.types';

export const getBusinessProfile = async (): Promise<BusinessProfile> => {
  const { data } = await axiosClient.get<BusinessProfile>('/business/profile');
  return data;
};

export const updateBusinessProfile = async (profile: BusinessProfile): Promise<void> => {
  await axiosClient.put('/business/profile', profile);
};

// --- SERVICES ---
// 🔥 SPRINT 04: Inyectamos locationId para aislar la data por sede
export const getServices = async (locationId?: string): Promise<CatalogItemDto[]> => {
  const params = locationId && locationId !== 'all' ? { locationId } : {};
  const { data } = await axiosClient.get<CatalogItemDto[]>('/business/services', { params });
  return data;
};

export const saveService = async (service: CatalogItemDto): Promise<CatalogItemDto> => {
  if (service.id) {
    const { data } = await axiosClient.put<CatalogItemDto>(`/business/services/${service.id}`, service);
    return data;
  }
  const { data } = await axiosClient.post<CatalogItemDto>('/business/services', service);
  return data;
};

export const deleteService = async (serviceId: string): Promise<void> => {
  await axiosClient.delete(`/business/services/${serviceId}`);
};

// --- LOCATIONS & ONBOARDING ---
export const getLocations = async (): Promise<LocationDto[]> => {
  const { data } = await axiosClient.get<LocationDto[]>('/business/locations');
  return data;
};

export const saveLocation = async (location: LocationDto): Promise<LocationDto> => {
  const { data } = await axiosClient.post<LocationDto>('/business/locations', location);
  return data;
};

export const deleteLocation = async (locationId: string): Promise<void> => {
  await axiosClient.delete(`/business/locations/${locationId}`);
};

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

// --- WHATSAPP (Evolution API) ---
import type { WhatsAppStatusResponse, WhatsAppConnectResponse } from '../types/business.types';

export const getWhatsAppStatus = async (): Promise<WhatsAppStatusResponse> => {
  const { data } = await axiosClient.get<WhatsAppStatusResponse>('/business/whatsapp/status');
  return data;
};

export const connectWhatsApp = async (): Promise<WhatsAppConnectResponse> => {
  const { data } = await axiosClient.post<WhatsAppConnectResponse>('/business/whatsapp/connect');
  return data;
};

export const disconnectWhatsApp = async (): Promise<void> => {
  await axiosClient.post('/business/whatsapp/disconnect');
};