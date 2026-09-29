import { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { getConversations, getMessages, takeOverConversation, releaseConversation, sendManualMessage, deleteConversation } from '../services/conversation.service';
import type { Conversation, Message } from '../types/conversation.types';
import { useAuthStore } from '../../../core/store/useAuthStore';

export const useConversations = () => {
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  
  // 🔥 SPRINT 05: Deep Links (Leemos el ID desde la URL)
  const [searchParams, setSearchParams] = useSearchParams();
  const urlConversationId = searchParams.get('conversation');

  // 🔥 SPRINT 05: Guardamos solo el ID, no el objeto entero, para evitar el "Stale State"
  const [selectedConversationId, setSelectedConversationId] = useState<string | null>(urlConversationId);

  const { data: conversations = [], isLoading: isLoadingConversations, isError: isErrorConversations } = useQuery({
    queryKey: ['conversations', workspaceId],
    queryFn: () => getConversations(),
    enabled: !!workspaceId,
    refetchInterval: 15000, 
  });

  // Derivamos el objeto seleccionado en tiempo real desde el array que se actualiza cada 15s
  const selectedChat = conversations.find(c => c.id === selectedConversationId) || null;

  const { data: messages = [], isLoading: isLoadingMessages } = useQuery({
    queryKey: ['messages', workspaceId, selectedConversationId],
    queryFn: () => getMessages(selectedConversationId!),
    enabled: !!workspaceId && !!selectedConversationId,
    refetchInterval: 15000, 
  });

  useEffect(() => {
    // Si hay conversaciones y no hay ninguna seleccionada, seleccionamos la primera por defecto
    if (conversations.length > 0 && !selectedConversationId) {
      handleSelectChat(conversations[0].id);
    }
  }, [conversations, selectedConversationId]);

  const handleSelectChat = (id: string) => {
    setSelectedConversationId(id);
    setSearchParams({ conversation: id });
  };

  const takeOverMutation = useMutation({
    mutationFn: async () => {
      if (!selectedConversationId) throw new Error("No chat selected");
      return await takeOverConversation(selectedConversationId);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['conversations', workspaceId] });
    }
  });

  const releaseMutation = useMutation({
    mutationFn: async () => {
      if (!selectedConversationId) throw new Error("No chat selected");
      return await releaseConversation(selectedConversationId);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['conversations', workspaceId] });
    }
  });

  const sendMessageMutation = useMutation({
    mutationFn: async (content: string) => {
      if (!selectedConversationId) throw new Error("No chat selected");
      return await sendManualMessage(selectedConversationId, content);
    },
    onSuccess: (newMessage) => {
      queryClient.setQueryData(
        ['messages', workspaceId, selectedConversationId],
        (old: Message[] | undefined) => [...(old || []), newMessage]
      );
      queryClient.invalidateQueries({ queryKey: ['conversations', workspaceId] });
    }
  });

  const deleteMutation = useMutation({
    mutationFn: async () => {
      if (!selectedConversationId) throw new Error("No chat selected");
      await deleteConversation(selectedConversationId);
    },
    onSuccess: () => {
      setSelectedConversationId(null);
      searchParams.delete('conversation');
      setSearchParams(searchParams);
      queryClient.invalidateQueries({ queryKey: ['conversations', workspaceId] });
    }
  });

  return {
    conversations,
    selectedChat,
    messages,
    isLoading: isLoadingConversations || isLoadingMessages,
    isError: isErrorConversations,
    isChangingMode: takeOverMutation.isPending || releaseMutation.isPending,
    isSending: sendMessageMutation.isPending,
    isDeleting: deleteMutation.isPending,
    setSelectedChat: (chat: Conversation) => handleSelectChat(chat.id),
    handleTakeOver: () => takeOverMutation.mutateAsync(),
    handleRelease: () => releaseMutation.mutateAsync(),
    handleSendMessage: (content: string) => sendMessageMutation.mutateAsync(content), // Ahora devuelve una promesa
    handleDelete: () => deleteMutation.mutateAsync() 
  };
};