import { axiosClient } from '../../../core/api/axiosClient';
import type { OrderRecord, OrderStatus, UpdateOrderStatusRequest } from '../types/orders.types';

export const getOrders = async (status?: OrderStatus | 'ALL'): Promise<OrderRecord[]> => {
  const params = status && status !== 'ALL' ? { status } : {};
  const { data } = await axiosClient.get<OrderRecord[]>('/orders', { params });
  return data;
};

export const getOrderById = async (id: string): Promise<OrderRecord> => {
  const { data } = await axiosClient.get<OrderRecord>(`/orders/${id}`);
  return data;
};

export const updateOrderStatus = async (id: string, status: OrderStatus): Promise<void> => {
  const payload: UpdateOrderStatusRequest = { status };
  await axiosClient.put(`/orders/${id}/status`, payload);
};