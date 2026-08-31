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

  // Carga inicial (Con Spinner)
  useEffect(() => {
    loadInitialData();
  }, []);

  const loadInitialData = async () => {
    try {
      const data = await getConversations();
      setConversations(data);
      if (data.length > 0) setSelectedChat(data[0]);
    } catch (error) {
      console.error('Error cargando conversaciones', error);
    } finally {
      setIsLoading(false);
    }
  };

  // 🔥 SPRINT 2.3: POLLING DINÁMICO (Actualización Silenciosa cada 12 segundos)
  useEffect(() => {
    if (selectedChat) {
      // Cargar mensajes inmediatamente al cambiar de chat
      getMessages(selectedChat.id).then(setMessages).catch(console.error);
    }

    const interval = setInterval(async () => {
      try {
        const freshConvs = await getConversations();
        setConversations(freshConvs);

        if (selectedChat) {
          const freshMsgs = await getMessages(selectedChat.id);
          setMessages(freshMsgs);

          // Si la IA o el backend cambió el modo de la conversación en segundo plano, lo reflejamos
          const updatedChat = freshConvs.find(c => c.id === selectedChat.id);
          if (updatedChat && updatedChat.mode !== selectedChat.mode) {
            setSelectedChat(updatedChat);
          }
        } else if (freshConvs.length > 0) {
          setSelectedChat(freshConvs[0]);
        }
      } catch (error) {
        console.error('Error en polling de Inbox', error);
      }
    }, 12000); // 12 segundos

    return () => clearInterval(interval);
  }, [selectedChat]); // Reiniciar el timer cada vez que el usuario selecciona un chat distinto

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