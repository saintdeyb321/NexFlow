import { useEffect, useState } from 'react';
import { getConversations, getMessages, takeOverConversation, releaseConversation, sendManualMessage } from '../services/conversation.service';
import type { Conversation, Message } from '../types/conversation.types';
import { ConversationSidebar } from '../components/ConversationSidebar';
import { ConversationThread } from '../components/ConversationThread';

export const InboxPage = () => {
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [selectedChat, setSelectedChat] = useState<Conversation | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  
  const [isChangingMode, setIsChangingMode] = useState(false);
  const [isSending, setIsSending] = useState(false);

  useEffect(() => {
    loadConversations();
  }, []);

  useEffect(() => {
    if (selectedChat) {
      loadMessages(selectedChat.id);
    }
  }, [selectedChat?.id]);

  const loadConversations = async () => {
    try {
      const data = await getConversations();
      setConversations(data);
      if (data.length > 0 && !selectedChat) setSelectedChat(data[0]);
    } catch (error) {
      console.error('Error cargando conversaciones', error);
    } finally {
      setIsLoading(false);
    }
  };

  const loadMessages = async (conversationId: string) => {
    try {
      const data = await getMessages(conversationId);
      setMessages(data);
    } catch (error) {
      console.error('Error cargando mensajes', error);
    }
  };

  const updateChatMode = (mode: 'Human' | 'Automatic') => {
    if (!selectedChat) return;
    setSelectedChat({ ...selectedChat, mode });
    setConversations(prev => prev.map(c => c.id === selectedChat.id ? { ...c, mode } : c));
  };

  const handleTakeOver = async () => {
    if (!selectedChat) return;
    setIsChangingMode(true);
    try {
      await takeOverConversation(selectedChat.id);
      updateChatMode('Human');
    } finally {
      setIsChangingMode(false);
    }
  };

  const handleRelease = async () => {
    if (!selectedChat) return;
    setIsChangingMode(true);
    try {
      await releaseConversation(selectedChat.id);
      updateChatMode('Automatic');
    } finally {
      setIsChangingMode(false);
    }
  };

  const handleSendMessage = async (content: string) => {
    if (!selectedChat) return;
    setIsSending(true);
    try {
      const sentMessage = await sendManualMessage(selectedChat.id, content);
      setMessages(prev => [...prev, sentMessage]);
    } catch (error) {
      console.error('Error enviando mensaje', error);
    } finally {
      setIsSending(false);
    }
  };

  if (isLoading) return <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando bandeja...</div>;

  return (
    <div className="flex h-[calc(100vh-8rem)] bg-white border border-gray-200 rounded-xl overflow-hidden shadow-sm">
      <ConversationSidebar 
        conversations={conversations} 
        selectedChat={selectedChat} 
        onSelectChat={setSelectedChat} 
      />
      
      {selectedChat ? (
        <ConversationThread
          chat={selectedChat}
          messages={messages}
          isChangingMode={isChangingMode}
          isSending={isSending}
          onTakeOver={handleTakeOver}
          onRelease={handleRelease}
          onSendMessage={handleSendMessage}
        />
      ) : (
        <div className="w-2/3 flex items-center justify-center text-gray-500 bg-gray-50/50">
          Selecciona una conversación para ver el historial
        </div>
      )}
    </div>
  );
};