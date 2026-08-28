import { useState, useEffect, useCallback } from 'react';
import { getConversations, getMessages, takeOverConversation, releaseConversation, sendManualMessage } from '../services/conversation.service';
import type { Conversation, Message } from '../types/conversation.types';

export const useConversations = () => {
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [selectedChat, setSelectedChat] = useState<Conversation | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isChangingMode, setIsChangingMode] = useState(false);
  const [isSending, setIsSending] = useState(false);

  const loadConversations = useCallback(async () => {
    try {
      const data = await getConversations();
      setConversations(data);
      if (data.length > 0 && !selectedChat) setSelectedChat(data[0]);
    } catch (error) {
      console.error('Error cargando conversaciones', error);
    } finally {
      setIsLoading(false);
    }
  }, [selectedChat]);

  const loadMessages = useCallback(async (conversationId: string) => {
    try {
      const data = await getMessages(conversationId);
      setMessages(data);
    } catch (error) {
      console.error('Error cargando mensajes', error);
    }
  }, []);

  const handleTakeOver = useCallback(async () => {
    if (!selectedChat) return;
    setIsChangingMode(true);
    try {
      await takeOverConversation(selectedChat.id);
      const updatedChat = { ...selectedChat, mode: 'Human' as const };
      setSelectedChat(updatedChat);
      setConversations(prev => prev.map(c => c.id === selectedChat.id ? updatedChat : c));
    } finally {
      setIsChangingMode(false);
    }
  }, [selectedChat]);

  const handleRelease = useCallback(async () => {
    if (!selectedChat) return;
    setIsChangingMode(true);
    try {
      await releaseConversation(selectedChat.id);
      const updatedChat = { ...selectedChat, mode: 'Automatic' as const };
      setSelectedChat(updatedChat);
      setConversations(prev => prev.map(c => c.id === selectedChat.id ? updatedChat : c));
    } finally {
      setIsChangingMode(false);
    }
  }, [selectedChat]);

  const handleSendMessage = useCallback(async (content: string) => {
    if (!selectedChat || !content.trim()) return;
    
    setIsSending(true);
    try {
      const sentMessage = await sendManualMessage(selectedChat.id, content.trim());
      setMessages(prev => [...prev, sentMessage]);
      return sentMessage;
    } catch (error) {
      console.error('Error enviando mensaje', error);
      throw error;
    } finally {
      setIsSending(false);
    }
  }, [selectedChat]);

  useEffect(() => {
    loadConversations();
  }, [loadConversations]);

  useEffect(() => {
    if (selectedChat) {
      loadMessages(selectedChat.id);
    }
  }, [selectedChat?.id, loadMessages]);

  return {
    conversations,
    selectedChat,
    messages,
    isLoading,
    isChangingMode,
    isSending,
    setSelectedChat,
    handleTakeOver,
    handleRelease,
    handleSendMessage,
  };
};