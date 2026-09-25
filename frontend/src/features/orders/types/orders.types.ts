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
  subtotalMinorUnits?: number; 
}

export interface OrderRecord {
  id: string;
  conversationId: string;
  consumerPhone: string;
  consumerName: string;
  status: OrderStatus;
  currency: string;
  totalAmountMinorUnits: number;
  items: OrderItemRecord[];
  notes?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface UpdateOrderStatusRequest {
  status: OrderStatus;
}