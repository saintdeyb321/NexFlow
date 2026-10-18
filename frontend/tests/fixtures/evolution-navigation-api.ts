import axios from 'axios';
import { useAuthStore } from './evolution-navigation-store';

export class ApiError extends Error { constructor(public status: number, message: string) { super(message); } }
export const getApiErrorPresentation = (error: unknown) => error instanceof Error ? error.message : 'Error local';
export const fixture = { actionUrl: '/conversations?conversation=chat-a#latest' };
export const calls: { method: string; path: string; workspace: string }[] = [];
export const axiosClient = axios.create({ adapter: async config => {
  calls.push({ method: config.method!, path: config.url!, workspace: useAuthStore.getState().me.workspace.id });
  return { data: config.method === 'get' ? [{ id: 'notice-a', moduleCode: 'CONVERSATIONS', title: 'Revisa el mensaje', message: 'Entrega sin confirmar', isRead: false, actionUrl: fixture.actionUrl, createdAt: '2026-10-18T10:00:00Z' }] : null, status: 200, statusText: 'OK', config, headers: {} };
} });
