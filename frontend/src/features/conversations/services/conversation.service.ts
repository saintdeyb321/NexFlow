import { axiosClient } from '../../../core/api/axiosClient';
import type { Conversation, Message, ConversationMode } from '../types/conversation.types';

export const getConversations = async (limit = 50): Promise<Conversation[]> => {
  const { data } = await axiosClient.get<Conversation[]>('/conversations', { params: { limit } });
  return data;
};

export const getMessages = async (conversationId: string, limit = 50): Promise<Message[]> => {
  const { data } = await axiosClient.get<Message[]>(`/conversations/${conversationId}/messages`, { params: { limit } });
  return data;
};

export const takeOverConversation = async (conversationId: string): Promise<{ message: string, mode: ConversationMode }> => {
  const { data } = await axiosClient.post(`/conversations/${conversationId}/takeover`);
  return data;
};

export const releaseConversation = async (conversationId: string): Promise<{ message: string, mode: ConversationMode }> => {
  const { data } = await axiosClient.post(`/conversations/${conversationId}/release`);
  return data;
};

export const sendManualMessage = async (conversationId: string, content: string): Promise<Message> => {
  const { data } = await axiosClient.post<Message>(`/conversations/${conversationId}/messages`, { content });
  return data;
};

export const deleteConversation = async (conversationId: string): Promise<void> => {
  await axiosClient.delete(`/conversations/${conversationId}`);
};