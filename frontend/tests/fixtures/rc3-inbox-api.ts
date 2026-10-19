import axios from 'axios';
import type { Conversation, Message } from '../../src/features/conversations/types/conversation.types';

export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string) { super(message); }
}
export const getApiErrorPresentation = (error: unknown) => error instanceof Error ? error.message : 'Error simulado';
export const fixture: { fail: 'none' | 'messages' | 'conversations' } = { fail: 'none' };
export const calls: { path: string; status: number }[] = [];
const conversation: Conversation = { id: 'chat-rc3', consumerPhone: '+51999999999', channel: 'WhatsApp', mode: 'Automatic', status: 'open',
  handoffReason: 'None', startedAt: '2026-10-19T04:07:00Z', lastMessageAt: '2026-10-19T04:08:00Z' };
const message: Message = { id: 'fictitious-message', direction: 'inbound', sender: 'Consumer', content: 'Historial ficticio conservado',
  status: 'Sent', timestamp: '2026-10-19T04:08:00Z', externalMessageId: null };
export const axiosClient = axios.create({ adapter: async config => {
  const path = config.url!;
  const panel = path === '/conversations' ? 'conversations' : path === '/conversations/chat-rc3/messages' ? 'messages' : null;
  if (config.method !== 'get' || panel === null) throw new Error('Unexpected local fixture request');
  const failed = fixture.fail === panel; calls.push({ path, status: failed ? 503 : 200 });
  if (failed) throw new ApiError(503, 'Dependency.Unavailable', 'Simulated partial inbox outage');
  return { data: panel === 'conversations' ? [conversation] : [message], status: 200, statusText: 'OK', config, headers: {} };
} });
