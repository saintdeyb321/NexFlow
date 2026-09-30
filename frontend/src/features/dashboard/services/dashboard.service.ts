import { axiosClient } from '../../../core/api/axiosClient';
import type { DashboardResponseDto } from '../types/dashboard.types';

export const getDashboardSummary = async (signal?: AbortSignal): Promise<DashboardResponseDto> => {
  const { data } = await axiosClient.get<DashboardResponseDto>('/dashboard', { signal });
  return data;
};