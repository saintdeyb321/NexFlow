export type ConversationMode = 'Automatic' | 'Human' | 'Paused';
export type SenderType = 'Consumer' | 'AI' | 'BusinessUser' | 'System';

export interface Conversation {
  id: string;
  consumerPhone: string;
  channel: string;
  mode: ConversationMode;
  status: string;
  startedAt: string;
  lastMessageAt: string;
}

export interface Message {
  id: string;
  direction: string;
  sender: SenderType;
  content: string;
  externalMessageId?: string;
  timestamp: string;
}