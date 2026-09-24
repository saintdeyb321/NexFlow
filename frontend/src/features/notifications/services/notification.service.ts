import { axiosClient } from '../../../core/api/axiosClient';

export interface NotificationDto {
  id: string;
  moduleCode: string;
  type: string;
  title: string;
  message: string;
  isRead: boolean;
  actionUrl?: string;
  createdAt: string;
}

export const getNotifications = async (): Promise<NotificationDto[]> => {
  const { data } = await axiosClient.get<NotificationDto[]>('/notifications');
  return data;
};

export const markNotificationAsRead = async (id: string): Promise<void> => {
  await axiosClient.put(`/notifications/${id}/read`);
};