export type RequestStatus = 'Pending' | 'InReview' | 'Approved' | 'Rejected' | 'Completed' | 'Cancelled';

export interface RequestRecord {
  id: string;
  consumerPhone: string;
  title: string;
  description: string;
  status: RequestStatus; 
  createdAt: string;
  updatedAt: string;
}