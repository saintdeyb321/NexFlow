export type ConversationMode = 'Automatic' | 'Human' | 'Paused';
export type SenderType = 'Consumer' | 'AI' | 'BusinessUser' | 'System';
export type HandoffReason = 'None' | 'AiEscalation' | 'ManualIntervention' | 'SystemError';
export type MessageStatus = 'Pending' | 'Attempting' | 'Sent' | 'Failed' | 'UnknownDelivery';

export interface ManualMessageResult {
  message: Message;
  accepted: boolean;
}

export interface Conversation {
  id: string;
  consumerPhone: string;
  channel: string;
  mode: ConversationMode;
  status: string;
  handoffReason: HandoffReason;
  startedAt: string;
  lastMessageAt: string;
}

export interface Message {
  id: string;
  direction: string;
  sender: SenderType;
  content: string;
  externalMessageId: string | null;
  status: MessageStatus;
  timestamp: string;
  transportStartedAt?: string | null;
  transportLeaseUntil?: string | null;
}
