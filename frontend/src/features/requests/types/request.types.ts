export type RequestStatus = 'Pending' | 'InReview' | 'Approved' | 'Rejected' | 'Completed' | 'Cancelled';

// 🔥 Nuevos tipos específicos
export type RequestType = 'Tramite' | 'CommercialInquiry' | 'Support' | 'HumanHandoff' | 'Other';

export interface RequestRecord {
  id: string;
  conversationId: string;
  consumerPhone: string;
  type: RequestType;
  title: string;
  description: string;
  status: RequestStatus;
  assignedTo: string | null;
  sourceMessageId: string | null;
  metadata: Record<string, unknown>;
  createdAt: string;
  updatedAt: string;
}

export interface CreateRequestDto {
  consumerPhone: string;
  conversationId?: string;
  type: RequestType;
  title: string;
  description: string;
  metadata?: Record<string, unknown>;
  sourceMessageId?: string | null;
}

export interface WorkspaceMember {
  userId: string;
  workspaceId: string;
  role: 'Owner' | 'Admin' | 'Member';
  firstName: string;
  lastName: string;
}

export const requestTransitions: Record<RequestStatus, readonly RequestStatus[]> = {
  Pending: ['InReview', 'Approved', 'Rejected', 'Cancelled'],
  InReview: ['Approved', 'Rejected', 'Completed', 'Cancelled'],
  Approved: ['Completed', 'Cancelled'],
  Completed: [], Rejected: [], Cancelled: [],
};
