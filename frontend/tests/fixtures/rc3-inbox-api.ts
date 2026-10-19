import axios from 'axios';
import type { Conversation, Message } from '../../src/features/conversations/types/conversation.types';
import { useAuthStore } from './rc3-inbox-store';

export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string) { super(message); }
}
export const getApiErrorPresentation = (error: unknown) => error instanceof Error ? error.message : 'Error simulado';
export const fixture: { fail: 'none' | 'messages' | 'conversations' | 'both'; status: number; extraChat: boolean; showStatuses: boolean; hold: string | null; release?: () => void } = {
  fail: new URLSearchParams(location.search).has('emptyFailure') ? 'both' : 'none', status: 503, extraChat: false, showStatuses: false, hold: null,
};
export const calls: { path: string; status: number }[] = [];
const conversation: Conversation = { id: 'chat-rc3', consumerPhone: '+51999999999', channel: 'WhatsApp', mode: 'Automatic', status: 'open',
  handoffReason: 'None', startedAt: '2026-10-19T04:07:00Z', lastMessageAt: '2026-10-19T04:08:00Z' };
const message: Message = { id: 'fictitious-message', direction: 'inbound', sender: 'Consumer', content: 'Historial ficticio conservado',
  status: 'Sent', timestamp: '2026-10-19T04:08:00Z', externalMessageId: null };
export const axiosClient = axios.create({ adapter: async config => {
  const path = config.url!;
  const panel = path === '/conversations' ? 'conversations' : /^\/conversations\/(chat-rc3|chat-second)\/messages$/.test(path) ? 'messages' : null;
  if (config.method !== 'get' || panel === null) throw new Error('Unexpected local fixture request');
  const workspace = useAuthStore.getState().me.workspace!.id;
  const failed = fixture.fail === panel || fixture.fail === 'both';
  calls.push({ path, status: failed ? fixture.status : 200 });
  if (fixture.hold === panel) await new Promise<void>(resolve => { fixture.release = resolve; });
  if (failed) throw new ApiError(fixture.status, fixture.status === 403 ? 'Security.Forbidden' : 'Dependency.Unavailable', 'Simulated inbox outage');
  const conversations = fixture.extraChat ? [conversation, { ...conversation, id: 'chat-second', consumerPhone: '+51888888888' }] : [conversation];
  const statuses: Message[] = (['Pending', 'Attempting', 'Sent', 'Failed', 'UnknownDelivery'] as const).map(status => ({ ...message,
    id: `outbound-${status}`, direction: 'outbound', sender: 'BusinessUser', status, content: `Estado ${status}`, transportLeaseUntil: '2099-01-01T00:00:00Z' }));
  const messages = fixture.showStatuses ? [message, ...statuses] : workspace !== 'workspace-a' ? [{ ...message, content: 'Historial exclusivo workspace B' }]
    : path.includes('chat-second') ? [{ ...message, id: 'second-message', content: 'Historial segundo chat' }] : [message];
  return { data: panel === 'conversations' ? conversations : messages, status: 200, statusText: 'OK', config, headers: {} };
} });
