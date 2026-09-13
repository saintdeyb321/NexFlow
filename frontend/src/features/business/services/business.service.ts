import { axiosClient } from '../../../core/api/axiosClient';
import type { BusinessHoursDto, BusinessProfile, LocationDto } from '../types/business.types';
// 🔥 SPRINT 6: Importamos los tipos desde catalog
import type { CatalogCategoryDto, CatalogItemDto } from '../../catalog/types/catalog.types';

export const getBusinessProfile = async (): Promise<BusinessProfile> => {
  const { data } = await axiosClient.get<BusinessProfile>('/business/profile');
  return data;
};

export const updateBusinessProfile = async (profile: BusinessProfile): Promise<void> => {
  await axiosClient.put('/business/profile', profile);
};

export const getCategories = async (): Promise<CatalogCategoryDto[]> => {
  const { data } = await axiosClient.get<CatalogCategoryDto[]>('/catalog/categories');
  return data;
};

// --- SERVICES ---
export const getServices = async (): Promise<CatalogItemDto[]> => {
  // 🔥 Asumiendo que tu endpoint de servicios sigue aquí, o usa /catalog filtrado
  const { data } = await axiosClient.get<CatalogItemDto[]>('/business/services');
  return data;
};

export const saveService = async (service: CatalogItemDto): Promise<CatalogItemDto> => {
  const { data } = await axiosClient.post<CatalogItemDto>('/business/services', service);
  return data;
};

export const deleteService = async (serviceId: string): Promise<void> => {
  await axiosClient.delete(`/business/services/${serviceId}`);
};

// --- LOCATIONS & ONBOARDING (Intactos) ---
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

// WHATSAPP (Evolution API) - SPRINT 5
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