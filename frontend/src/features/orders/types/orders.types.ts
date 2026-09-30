export type OrderStatus = 
  | 'PendingReview' 
  | 'Approved' 
  | 'Processing' 
  | 'Completed' 
  | 'Rejected' 
  | 'Cancelled';

export interface OrderItemRecord {
  productId: string;
  productName: string;
  quantity: number;
  unitPriceMinorUnits: number;
  subtotalMinorUnits: number;
  currency: string | null;
}

export interface OrderRecord {
  id: string;
  conversationId: string;
  consumerPhone: string;
  consumerName: string;
  status: OrderStatus;
  currency: string | null;
  totalAmountMinorUnits: number | null;
  items: OrderItemRecord[];
  notes: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface UpdateOrderStatusRequest {
  status: OrderStatus;
}

export interface UpdateOrderAmountRequest {
  totalAmountMinorUnits: number;
}

export const orderTransitions: Record<OrderStatus, readonly OrderStatus[]> = {
  PendingReview: ['Approved', 'Rejected', 'Cancelled'],
  Approved: ['Processing', 'Cancelled'],
  Processing: ['Completed', 'Cancelled'],
  Completed: [], Rejected: [], Cancelled: [],
};
