import { axiosClient } from '../../../core/api/axiosClient';
import type { DashboardResponseDto } from '../types/dashboard.types';

export const getDashboardSummary = async (): Promise<DashboardResponseDto> => {
  const { data } = await axiosClient.get<DashboardResponseDto>('/dashboard');
  return data;
};