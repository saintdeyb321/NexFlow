import { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { getConversations, getMessages, takeOverConversation, releaseConversation, sendManualMessage, deleteConversation } from '../services/conversation.service';
import type { Conversation, Message } from '../types/conversation.types';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { usePermissions } from '../../../core/auth/permissions';
import { ApiError, getApiErrorPresentation } from '../../../core/api/axiosClient';

export const useConversations = () => {
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const { can } = usePermissions();
  const requireCapability = (capability: string) => {
    if (!can('CONVERSATIONS', capability)) throw new ApiError(403, 'Security.CapabilityDenied', 'No tienes permiso para esta operación.');
  };
  
  // 🔥 SPRINT 05: Deep Links (Leemos el ID desde la URL)
  const [searchParams, setSearchParams] = useSearchParams();
  const urlConversationId = searchParams.get('conversation');

  // 🔥 SPRINT 05: Guardamos solo el ID, no el objeto entero, para evitar el "Stale State"
  const [selectedConversationId, setSelectedConversationId] = useState<string | null>(urlConversationId);

  const { data: conversations = [], isLoading: isLoadingConversations, isError: isErrorConversations } = useQuery({
    queryKey: ['conversations', workspaceId],
    queryFn: () => getConversations(),
    enabled: !!workspaceId && can('CONVERSATIONS', 'READ'),
    refetchInterval: 15000, 
  });

  // Derivamos el objeto seleccionado en tiempo real desde el array que se actualiza cada 15s
  const selectedChat = conversations.find(c => c.id === selectedConversationId) || null;

  const { data: messages = [], isLoading: isLoadingMessages } = useQuery({
    queryKey: ['messages', workspaceId, selectedConversationId],
    queryFn: () => getMessages(selectedConversationId!),
    enabled: !!workspaceId && !!selectedConversationId && can('CONVERSATIONS', 'READ'),
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
      requireCapability('TAKEOVER');
      if (!selectedConversationId) throw new Error("No chat selected");
      return await takeOverConversation(selectedConversationId);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['conversations', workspaceId] });
    }
  });

  const releaseMutation = useMutation({
    mutationFn: async () => {
      requireCapability('RELEASE');
      if (!selectedConversationId) throw new Error("No chat selected");
      return await releaseConversation(selectedConversationId);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['conversations', workspaceId] });
    }
  });

  const sendMessageMutation = useMutation({
    mutationFn: async ({ conversationId, content }: { workspaceId: string; conversationId: string; content: string }) => {
      requireCapability('SEND_MESSAGE');
      return await sendManualMessage(conversationId, content);
    },
    onSuccess: (result, variables) => {
      queryClient.setQueryData(
        ['messages', variables.workspaceId, variables.conversationId],
        (old: Message[] | undefined) => [...(old || []).filter(message => message.id !== result.message.id), result.message]
      );
      queryClient.invalidateQueries({ queryKey: ['conversations', variables.workspaceId] });
      queryClient.invalidateQueries({ queryKey: ['messages', variables.workspaceId, variables.conversationId] });
    }
  });

  const deleteMutation = useMutation({
    mutationFn: async () => {
      requireCapability('DELETE');
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
    actionError: takeOverMutation.error || releaseMutation.error || deleteMutation.error
      ? getApiErrorPresentation(takeOverMutation.error || releaseMutation.error || deleteMutation.error) : null,
    isChangingMode: takeOverMutation.isPending || releaseMutation.isPending,
    isSending: sendMessageMutation.isPending,
    isDeleting: deleteMutation.isPending,
    setSelectedChat: (chat: Conversation | null) => {
      if (chat) handleSelectChat(chat.id);
      else { setSelectedConversationId(null); setSearchParams({}); }
    },
    handleTakeOver: () => takeOverMutation.mutate(),
    handleRelease: () => releaseMutation.mutate(),
    handleSendMessage: (content: string) => {
      if (!workspaceId || !selectedConversationId) return Promise.reject(new Error('No chat selected'));
      return sendMessageMutation.mutateAsync({ workspaceId, conversationId: selectedConversationId, content });
    },
    handleDelete: () => deleteMutation.mutate()
  };
};
