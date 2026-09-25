export type ConversationMode = 'Automatic' | 'Human';
export type SenderType = 'Consumer' | 'AI' | 'BusinessUser' | 'System';
export type HandoffReason = 'None' | 'AiEscalation' | 'ManualIntervention';

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
  externalMessageId?: string;
  timestamp: string;
}