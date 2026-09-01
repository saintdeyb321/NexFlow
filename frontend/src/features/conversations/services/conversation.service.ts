import { axiosClient } from '../../../core/api/axiosClient';
import type { Conversation, Message } from '../types/conversation.types';

export const getConversations = async (limit = 50): Promise<Conversation[]> => {
  const { data } = await axiosClient.get<Conversation[]>('/conversations', { params: { limit } });
  return data;
};

export const getMessages = async (conversationId: string, limit = 50): Promise<Message[]> => {
  const { data } = await axiosClient.get<Message[]>(`/conversations/${conversationId}/messages`, { params: { limit } });
  return data;
};

export const takeOverConversation = async (conversationId: string): Promise<void> => {
  await axiosClient.post(`/conversations/${conversationId}/takeover`);
};

export const releaseConversation = async (conversationId: string): Promise<void> => {
  await axiosClient.post(`/conversations/${conversationId}/release`);
};

export const sendManualMessage = async (conversationId: string, content: string): Promise<Message> => {
  const { data } = await axiosClient.post<Message>(`/conversations/${conversationId}/messages`, {
    content
  });
  return data;
};

// 🔥 Auditoría (Fase 5): Nuevo método para ejecutar el borrado real del documento y subcolecciones.
export const deleteConversation = async (conversationId: string): Promise<void> => {
  await axiosClient.delete(`/conversations/${conversationId}`);
};