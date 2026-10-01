import { useState, useEffect, useRef } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { queryPolicies, usePageVisible } from '../../../core/query/queryPolicies';
import { compareTimestamps, fetchIncremental, mergeById } from '../../../core/query/incremental';
import { useSearchParams } from 'react-router-dom';
import { getConversations, getMessages, getMessagesByIds, MESSAGE_STATUS_BATCH_LIMIT, takeOverConversation, releaseConversation, sendManualMessage, deleteConversation } from '../services/conversation.service';
import type { Conversation, Message } from '../types/conversation.types';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { usePermissions } from '../../../core/auth/permissions';
import { ApiError, getApiErrorPresentation } from '../../../core/api/axiosClient';

export const useConversations = () => {
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const { can } = usePermissions();
  const visible = usePageVisible();
  const requireCapability = (capability: string) => {
    if (!can('CONVERSATIONS', capability)) throw new ApiError(403, 'Security.CapabilityDenied', 'No tienes permiso para esta operación.');
  };
  
  // 🔥 SPRINT 05: Deep Links (Leemos el ID desde la URL)
  const [searchParams, setSearchParams] = useSearchParams();
  const urlConversationId = searchParams.get('conversation');

  // 🔥 SPRINT 05: Guardamos solo el ID, no el objeto entero, para evitar el "Stale State"
  const [selectedConversationId, setSelectedConversationId] = useState<string | null>(urlConversationId);
  const conversationsKey = queryKeys.conversations.list(workspaceId);
  const messagesKey = queryKeys.messages.list(workspaceId, selectedConversationId);
  const reconciliationCursor = useRef<{ conversationId: string | null; lastId?: string }>({ conversationId: null });
  const compareConversations = (a: Conversation, b: Conversation) => compareTimestamps(b.lastMessageAt, a.lastMessageAt) || a.id.localeCompare(b.id);
  const compareMessages = (a: Message, b: Message) => compareTimestamps(a.timestamp, b.timestamp) || a.id.localeCompare(b.id);

  const { data: conversations = [], isLoading: isLoadingConversations, isError: isErrorConversations } = useQuery({
    ...queryPolicies.inbox,
    queryKey: conversationsKey,
    queryFn: async ({ signal }) => {
      const current = queryClient.getQueryData<Conversation[]>(conversationsKey);
      const after = !queryClient.getQueryState(conversationsKey)?.isInvalidated && current?.[0]?.lastMessageAt || undefined;
      const delta = await fetchIncremental((cursor, afterId) => getConversations(50, cursor, signal, afterId), record => record.lastMessageAt, after);
      return mergeById(after ? queryClient.getQueryData<Conversation[]>(conversationsKey) ?? [] : [], delta, compareConversations).slice(0, 50);
    },
    enabled: !!workspaceId && can('CONVERSATIONS', 'READ'),
    refetchInterval: visible ? 15000 : false,
  });

  // Derivamos el objeto seleccionado en tiempo real desde el array que se actualiza cada 15s
  const selectedChat = conversations.find(c => c.id === selectedConversationId) || null;

  const { data: messages = [], isLoading: isLoadingMessages } = useQuery({
    ...queryPolicies.inbox,
    queryKey: messagesKey,
    queryFn: async ({ signal }) => {
      const current = queryClient.getQueryData<Message[]>(messagesKey);
      const newest = current?.[current.length - 1]?.timestamp;
      const after = !queryClient.getQueryState(messagesKey)?.isInvalidated && newest || undefined;
      const delta = await fetchIncremental((cursor, afterId) => getMessages(selectedConversationId!, 50, cursor, signal, afterId), record => record.timestamp, after);
      const merged = mergeById(after ? queryClient.getQueryData<Message[]>(messagesKey) ?? [] : [], delta, compareMessages);
      if (!after) return merged;

      // Status writes already replace the mirrored message. Read at most 20 old unresolved IDs per poll,
      // rotating through them; timeline reads always advance from the newest timestamp.
      const fetchedIds = new Set(delta.map(message => message.id));
      const unresolvedIds = merged.filter(message => message.direction === 'outbound'
        && ['Pending', 'Attempting', 'UnknownDelivery'].includes(message.status) && !fetchedIds.has(message.id))
        .map(message => message.id).sort();
      if (unresolvedIds.length === 0) return merged;
      const lastId = reconciliationCursor.current.conversationId === selectedConversationId ? reconciliationCursor.current.lastId : undefined;
      const next = lastId ? unresolvedIds.findIndex(id => id > lastId) : 0;
      const start = next < 0 ? 0 : next;
      const ids = [...unresolvedIds.slice(start), ...unresolvedIds.slice(0, start)].slice(0, MESSAGE_STATUS_BATCH_LIMIT);
      const statuses = await getMessagesByIds(selectedConversationId!, ids, signal);
      reconciliationCursor.current = { conversationId: selectedConversationId, lastId: ids[ids.length - 1] };
      return mergeById(merged, statuses, compareMessages);
    },
    enabled: !!workspaceId && !!selectedConversationId && can('CONVERSATIONS', 'READ'),
    refetchInterval: visible ? 15000 : false,
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

  type ConversationTarget = { workspaceId: string; conversationId: string };
  const changeMode = (target: ConversationTarget, mode: Conversation['mode'], handoffReason: Conversation['handoffReason']) => {
    void queryClient.cancelQueries({ queryKey: queryKeys.conversations.lists(target.workspaceId) });
    queryClient.setQueriesData<Conversation[]>({ queryKey: queryKeys.conversations.lists(target.workspaceId) }, current =>
      current?.map(conversation => conversation.id === target.conversationId ? { ...conversation, mode, handoffReason } : conversation));
  };
  const takeOverMutation = useSessionMutation({
    mutationFn: async (target: ConversationTarget) => {
      requireCapability('TAKEOVER');
      return await takeOverConversation(target.conversationId);
    },
    onSuccess: (result, target) => {
      changeMode(target, result.mode, 'ManualIntervention');
    }
  });

  const releaseMutation = useSessionMutation({
    mutationFn: async (target: ConversationTarget) => {
      requireCapability('RELEASE');
      return await releaseConversation(target.conversationId);
    },
    onSuccess: (result, target) => {
      changeMode(target, result.mode, 'None');
    }
  });

  const sendMessageMutation = useSessionMutation({
    mutationFn: async ({ conversationId, content }: { workspaceId: string; conversationId: string; content: string }) => {
      requireCapability('SEND_MESSAGE');
      return await sendManualMessage(conversationId, content);
    },
    onSuccess: (result, variables) => {
      const messageFamily = queryKeys.messages.conversation(variables.workspaceId, variables.conversationId);
      void queryClient.cancelQueries({ queryKey: messageFamily });
      void queryClient.cancelQueries({ queryKey: queryKeys.conversations.lists(variables.workspaceId) });
      queryClient.setQueryData(
        queryKeys.messages.list(variables.workspaceId, variables.conversationId),
        (old: Message[] | undefined) => old ? mergeById(old, [result.message], compareMessages) : undefined
      );
      if (!queryClient.getQueryData(queryKeys.messages.list(variables.workspaceId, variables.conversationId)))
        void queryClient.invalidateQueries({ queryKey: messageFamily });
      queryClient.setQueriesData<Conversation[]>({ queryKey: queryKeys.conversations.lists(variables.workspaceId) }, current =>
        current?.map<Conversation>(conversation => conversation.id === variables.conversationId
          ? { ...conversation, lastMessageAt: result.message.timestamp, mode: 'Human', handoffReason: 'ManualIntervention' } : conversation).sort(compareConversations));
    }
  });

  const deleteMutation = useSessionMutation({
    mutationFn: async (target: ConversationTarget) => {
      requireCapability('DELETE');
      await deleteConversation(target.conversationId);
    },
    onSuccess: (_, target) => {
      void queryClient.cancelQueries({ queryKey: queryKeys.conversations.lists(target.workspaceId) });
      if (selectedConversationId === target.conversationId) { setSelectedConversationId(null); setSearchParams({}); }
      queryClient.setQueriesData<Conversation[]>({ queryKey: queryKeys.conversations.lists(target.workspaceId) }, current =>
        current?.filter(conversation => conversation.id !== target.conversationId));
      queryClient.removeQueries({ queryKey: queryKeys.messages.conversation(target.workspaceId, target.conversationId) });
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
    handleTakeOver: () => { if (workspaceId && selectedConversationId) takeOverMutation.mutate({ workspaceId, conversationId: selectedConversationId }); },
    handleRelease: () => { if (workspaceId && selectedConversationId) releaseMutation.mutate({ workspaceId, conversationId: selectedConversationId }); },
    handleSendMessage: (content: string) => {
      if (!workspaceId || !selectedConversationId) return Promise.reject(new Error('No chat selected'));
      return sendMessageMutation.mutateAsync({ workspaceId, conversationId: selectedConversationId, content });
    },
    handleDelete: () => { if (workspaceId && selectedConversationId) deleteMutation.mutate({ workspaceId, conversationId: selectedConversationId }); }
  };
};
