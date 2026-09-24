import { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getConversations, getMessages, takeOverConversation, releaseConversation, sendManualMessage, deleteConversation } from '../services/conversation.service';
import type { Conversation, Message } from '../types/conversation.types';
import { useAuthStore } from '../../../core/store/useAuthStore';

export const useConversations = () => {
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  
  const [selectedChat, setSelectedChat] = useState<Conversation | null>(null);

  const { data: conversations = [], isLoading: isLoadingConversations, isError: isErrorConversations } = useQuery({
    queryKey: ['conversations', workspaceId],
    queryFn: () => getConversations(),
    enabled: !!workspaceId,
    refetchInterval: 15000, 
  });

  const { data: messages = [], isLoading: isLoadingMessages } = useQuery({
    queryKey: ['messages', workspaceId, selectedChat?.id],
    queryFn: () => getMessages(selectedChat!.id),
    enabled: !!workspaceId && !!selectedChat?.id,
    refetchInterval: 15000, 
  });

  useEffect(() => {
    if (conversations.length > 0 && !selectedChat) {
      setSelectedChat(conversations[0]);
    }
  }, [conversations, selectedChat]);

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

  const deleteMutation = useMutation({
    mutationFn: async () => {
      if (!selectedChat) throw new Error("No chat selected");
      await deleteConversation(selectedChat.id);
    },
    onSuccess: () => {
      setSelectedChat(null);
      queryClient.invalidateQueries({ queryKey: ['conversations', workspaceId] });
    }
  });

  return {
    conversations,
    selectedChat,
    messages,
    // 🔥 SOLUCIÓN: isLoadingConversations (con "s" al final) || isLoadingMessages
    isLoading: isLoadingConversations || isLoadingMessages,
    isError: isErrorConversations,
    isChangingMode: takeOverMutation.isPending || releaseMutation.isPending,
    isSending: sendMessageMutation.isPending,
    isDeleting: deleteMutation.isPending,
    setSelectedChat,
    handleTakeOver: () => takeOverMutation.mutateAsync(),
    handleRelease: () => releaseMutation.mutateAsync(),
    handleSendMessage: (content: string) => sendMessageMutation.mutateAsync(content),
    handleDelete: () => {
      if (window.confirm('¿Estás seguro de eliminar esta conversación y todos sus mensajes de la base de datos?')) {
        deleteMutation.mutateAsync();
      }
    }
  };
};