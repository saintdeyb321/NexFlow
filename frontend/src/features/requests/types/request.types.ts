export interface RequestRecord {
  id: string;
  consumerPhone: string;
  title: string;
  description: string;
  status: string; // PENDING, IN_PROGRESS, COMPLETED, CANCELLED
  createdAt: string;
  updatedAt: string;
}