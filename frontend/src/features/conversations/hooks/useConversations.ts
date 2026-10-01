import { useToast } from '../../../components/ui/useToast';
import { useRef } from 'react';
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
import { ApiError } from '../../../core/api/axiosClient';

// Backend echoes can reconcile for 10 minutes; allow 2 minutes for transport and observation delay.
const RECONCILIATION_WINDOW_MS = 12 * 60 * 1000;

export const useConversations = () => {
  const queryClient = useQueryClient();
  const toast = useToast();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const { can } = usePermissions();
  const visible = usePageVisible();
  const requireCapability = (capability: string) => {
    if (!can('CONVERSATIONS', capability)) throw new ApiError(403, 'Security.CapabilityDenied', 'No tienes permiso para esta operación.');
  };
  const [searchParams, setSearchParams] = useSearchParams();
  const urlConversationId = searchParams.get('conversation');
  const conversationsKey = queryKeys.conversations.list(workspaceId);
  const reconciliationCursor = useRef<{ conversationId: string | null; lastId?: string }>({ conversationId: null });
  const compareConversations = (a: Conversation, b: Conversation) => compareTimestamps(b.lastMessageAt, a.lastMessageAt) || a.id.localeCompare(b.id);
  const compareMessages = (a: Message, b: Message) => compareTimestamps(a.timestamp, b.timestamp) || a.id.localeCompare(b.id);

  const { data: conversations = [], isLoading: isLoadingConversations, isError: isErrorConversations, refetch: refetchConversations } = useQuery({
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

  // URL owns explicit selection, including browser history and external navigation.
  const selectedConversationId = urlConversationId || conversations[0]?.id || null;
  const messagesKey = queryKeys.messages.list(workspaceId, selectedConversationId);
  const selectedChat = conversations.find(c => c.id === selectedConversationId) || null;

  const { data: messages = [], isLoading: isLoadingMessages, isError: isErrorMessages, refetch: refetchMessages } = useQuery({
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
      const now = Date.now();
      const unresolvedIds = merged.filter(message => message.direction === 'outbound'
        && ['Pending', 'Attempting', 'UnknownDelivery'].includes(message.status) && !fetchedIds.has(message.id)
        && Number.isFinite(Date.parse(message.transportStartedAt ?? message.timestamp))
        && now - Date.parse(message.transportStartedAt ?? message.timestamp) <= RECONCILIATION_WINDOW_MS
        && Date.parse(message.transportStartedAt ?? message.timestamp) <= now + 60 * 1000)
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

  const handleSelectChat = (id: string | null) => setSearchParams(previous => {
    const next = new URLSearchParams(previous);
    if (id) next.set('conversation', id);
    else next.delete('conversation');
    return next;
  });

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
      toast.success('Control manual activado.');
    },
    onError: error => toast.toastApiError(error)
  });

  const releaseMutation = useSessionMutation({
    mutationFn: async (target: ConversationTarget) => {
      requireCapability('RELEASE');
      return await releaseConversation(target.conversationId);
    },
    onSuccess: (result, target) => {
      changeMode(target, result.mode, 'None');
      toast.success('Atención automática activada.');
    },
    onError: error => toast.toastApiError(error)
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
      if (selectedConversationId === target.conversationId) handleSelectChat(null);
      queryClient.setQueriesData<Conversation[]>({ queryKey: queryKeys.conversations.lists(target.workspaceId) }, current =>
        current?.filter(conversation => conversation.id !== target.conversationId));
      queryClient.removeQueries({ queryKey: queryKeys.messages.conversation(target.workspaceId, target.conversationId) });
      toast.success('Conversación eliminada.');
    },
    onError: error => toast.toastApiError(error)
  });

  return {
    conversations,
    selectedChat,
    messages,
    isLoading: isLoadingConversations || isLoadingMessages,
    isError: isErrorConversations || isErrorMessages,
    retry: () => { void refetchConversations(); if (selectedConversationId) void refetchMessages(); },
    isChangingMode: takeOverMutation.isPending || releaseMutation.isPending,
    isSending: sendMessageMutation.isPending,
    isDeleting: deleteMutation.isPending,
    setSelectedChat: (chat: Conversation | null) => handleSelectChat(chat?.id ?? null),
    handleTakeOver: () => { if (workspaceId && selectedConversationId) takeOverMutation.mutate({ workspaceId, conversationId: selectedConversationId }); },
    handleRelease: () => { if (workspaceId && selectedConversationId) releaseMutation.mutate({ workspaceId, conversationId: selectedConversationId }); },
    handleSendMessage: (content: string) => {
      if (!workspaceId || !selectedConversationId) return Promise.reject(new Error('No chat selected'));
      return sendMessageMutation.mutateAsync({ workspaceId, conversationId: selectedConversationId, content });
    },
    handleDelete: () => { if (workspaceId && selectedConversationId) deleteMutation.mutate({ workspaceId, conversationId: selectedConversationId }); }
  };
};
