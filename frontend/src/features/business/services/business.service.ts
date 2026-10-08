import { axiosClient } from '../../../core/api/axiosClient';
import type { BusinessHoursDto, BusinessProfile, LocationDto } from '../types/business.types';

export const getBusinessProfile = async (signal?: AbortSignal): Promise<BusinessProfile> => {
  const { data } = await axiosClient.get<BusinessProfile>('/business/profile', { signal });
  return data;
};

export const updateBusinessProfile = async (profile: BusinessProfile): Promise<void> => {
  await axiosClient.put('/business/profile', profile);
};

// --- LOCATIONS ---
export const getLocations = async (signal?: AbortSignal): Promise<LocationDto[]> => {
  const { data } = await axiosClient.get<LocationDto[]>('/business/locations', { signal });
  return data;
};

// 🔥 SPRINT 06: Separamos estrictamente la Creación de la Edición (POST vs PUT)
export const createLocation = async (location: LocationDto): Promise<void> => {
  await axiosClient.post('/business/locations', location);
};

export const updateLocation = async (locationId: string, location: LocationDto): Promise<void> => {
  await axiosClient.put(`/business/locations/${locationId}`, location);
};

export const deleteLocation = async (locationId: string): Promise<void> => {
  await axiosClient.delete(`/business/locations/${locationId}`);
};

// --- HOURS ---
export const getBusinessHours = async (locationId: string, signal?: AbortSignal): Promise<BusinessHoursDto[]> => {
  const { data } = await axiosClient.get<BusinessHoursDto[]>(`/business/locations/${locationId}/hours`, { signal });
  return data;
};

export const saveBusinessHours = async (locationId: string, hours: BusinessHoursDto[]): Promise<void> => {
  await axiosClient.put(`/business/locations/${locationId}/hours`, hours);
};

// --- WHATSAPP (Evolution API) ---
import type { WhatsAppStatusResponse, WhatsAppConnectResponse } from '../types/business.types';

export const getWhatsAppStatus = async (signal?: AbortSignal, refresh = false): Promise<WhatsAppStatusResponse> => {
  const { data } = await axiosClient.get<WhatsAppStatusResponse>('/business/whatsapp/status', { signal, params: { refresh } });
  return data;
};

export const connectWhatsApp = async (): Promise<WhatsAppConnectResponse> => {
  const { data } = await axiosClient.post<WhatsAppConnectResponse>('/business/whatsapp/connect');
  return data;
};

export const disconnectWhatsApp = async (): Promise<WhatsAppStatusResponse> => {
  const { data } = await axiosClient.post<WhatsAppStatusResponse>('/business/whatsapp/disconnect', { confirmed: true });
  return data;
};
