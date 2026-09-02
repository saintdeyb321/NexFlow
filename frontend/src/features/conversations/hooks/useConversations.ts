import { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getConversations, getMessages, takeOverConversation, releaseConversation, sendManualMessage } from '../services/conversation.service';
import type { Conversation, Message } from '../types/conversation.types';
import { useAuthStore } from '../../../core/store/useAuthStore';

export const useConversations = () => {
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  
  // Estado local (UI State)
  const [selectedChat, setSelectedChat] = useState<Conversation | null>(null);

  // 🔥 Auditoría (Sprint 5.1): Aislamiento Multi-Tenant en TanStack Query
  // Server State: Conversaciones
  const { data: conversations = [], isLoading: isLoadingConversations } = useQuery({
    queryKey: ['conversations', workspaceId],
    queryFn: () => getConversations(),
    enabled: !!workspaceId, // Nunca se ejecuta si no hay un workspace activo
    staleTime: 1000 * 60 * 2, // 2 minutos de frescura
  });

  // Server State: Mensajes
  const { data: messages = [], isLoading: isLoadingMessages } = useQuery({
    queryKey: ['messages', workspaceId, selectedChat?.id],
    queryFn: () => getMessages(selectedChat!.id),
    enabled: !!workspaceId && !!selectedChat?.id,
    staleTime: 1000 * 15, // Refresco rápido para chat
  });

  // Auto-selección del primer chat al cargar
  useEffect(() => {
    if (conversations.length > 0 && !selectedChat) {
      setSelectedChat(conversations[0]);
    }
  }, [conversations, selectedChat]);

  // Mutaciones con invalidación de caché
  const takeOverMutation = useMutation({
    mutationFn: async () => {
      if (!selectedChat) throw new Error("No chat selected");
      await takeOverConversation(selectedChat.id);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['conversations', workspaceId] });
      if (selectedChat) setSelectedChat({ ...selectedChat, mode: 'Human' });
    }
  });

  const releaseMutation = useMutation({
    mutationFn: async () => {
      if (!selectedChat) throw new Error("No chat selected");
      await releaseConversation(selectedChat.id);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['conversations', workspaceId] });
      if (selectedChat) setSelectedChat({ ...selectedChat, mode: 'Automatic' });
    }
  });

  const sendMessageMutation = useMutation({
    mutationFn: async (content: string) => {
      if (!selectedChat) throw new Error("No chat selected");
      return await sendManualMessage(selectedChat.id, content);
    },
    onSuccess: (newMessage) => {
      // 🔥 Actualización optimista: inyectamos el mensaje a la caché instantáneamente
      queryClient.setQueryData(
        ['messages', workspaceId, selectedChat?.id],
        (old: Message[] | undefined) => [...(old || []), newMessage]
      );
      
      queryClient.invalidateQueries({ queryKey: ['conversations', workspaceId] });
      if (selectedChat?.mode !== 'Human') {
        setSelectedChat(prev => prev ? { ...prev, mode: 'Human' } : null);
      }
    }
  });

  return {
    conversations,
    selectedChat,
    messages,
    isLoading: isLoadingConversations || isLoadingMessages,
    isChangingMode: takeOverMutation.isPending || releaseMutation.isPending,
    isSending: sendMessageMutation.isPending,
    setSelectedChat,
    handleTakeOver: () => takeOverMutation.mutateAsync(),
    handleRelease: () => releaseMutation.mutateAsync(),
    handleSendMessage: (content: string) => sendMessageMutation.mutateAsync(content),
  };
};