import { axiosClient } from '../../../core/api/axiosClient';
import type { OrderRecord, OrderStatus, UpdateOrderStatusRequest } from '../types/orders.types';

export const getOrders = async (status?: OrderStatus | 'ALL', signal?: AbortSignal): Promise<OrderRecord[]> => {
  const params = status && status !== 'ALL' ? { status } : {};
  const { data } = await axiosClient.get<OrderRecord[]>('/orders', { params, signal });
  return data;
};

export const getOrderById = async (id: string, signal?: AbortSignal): Promise<OrderRecord> => {
  const { data } = await axiosClient.get<OrderRecord>(`/orders/${id}`, { signal });
  return data;
};

export const updateOrderStatus = async (id: string, status: OrderStatus): Promise<void> => {
  const payload: UpdateOrderStatusRequest = { status };
  await axiosClient.put(`/orders/${id}/status`, payload);
};

export const updateOrderAmount = async (id: string, totalAmountMinorUnits: number): Promise<void> => {
  const payload = { totalAmountMinorUnits };
  await axiosClient.put(`/orders/${id}/amount`, payload);
};