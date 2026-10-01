import { Input } from '../../../components/ui/Form';
import { Badge } from '../../../components/ui/Feedback';
import { Button, IconButton } from '../../../components/ui/Button';
import { useToast } from '../../../components/ui/useToast';
import { EmptyState } from '../../../components/ui/Feedback';
import { useState, useRef, useEffect } from 'react';
import { Bot, User, Send, Clock, AlertTriangle, AlertOctagon, Image as ImageIcon } from 'lucide-react';
import type { Conversation, Message, ManualMessageResult } from '../types/conversation.types';
import { usePermissions } from '../../../core/auth/permissions';

interface ConversationThreadProps {
  chat: Conversation;
  messages: Message[];
  isChangingMode: boolean;
  isSending: boolean;
  onTakeOver: () => void;
  onRelease: () => void;
  onSendMessage: (content: string) => Promise<ManualMessageResult>;
}

export const ConversationThread = ({
  chat, messages, isChangingMode, isSending, onTakeOver, onRelease, onSendMessage
}: ConversationThreadProps) => {
  const { can } = usePermissions();
  const toast = useToast();
  const [newMessage, setNewMessage] = useState('');
  const [leaseObservation, setLeaseObservation] = useState(() => Date.now());
  const messagesEndRef = useRef<HTMLDivElement>(null);

  // A cached attempt can outlive its lease while the tab is inactive. Observe only
  // the real lease deadline for presentation; never mutate its status or resend it.
  useEffect(() => {
    const now = Date.now();
    const nextLease = Math.min(...messages.filter(message => message.status === 'Attempting')
      .map(message => Date.parse(message.transportLeaseUntil ?? '')).filter(deadline => Number.isFinite(deadline) && deadline > leaseObservation));
    if (!Number.isFinite(nextLease)) return;
    const timer = setTimeout(() => setLeaseObservation(Date.now()), Math.max(0, nextLease - now + 1));
    return () => clearTimeout(timer);
  }, [messages, leaseObservation]);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth' });
  }, [messages]);
  const handleSend = async () => {
    if (!newMessage.trim()) return;
    try {
      const result = await onSendMessage(newMessage.trim());
      setNewMessage('');
      if (result.message.status === 'UnknownDelivery') toast.warning('Entrega sin confirmar. Revisa el estado antes de reenviar.');
      else if (result.accepted) toast.info('Envío aceptado; la entrega aún no está confirmada.');
    } catch (error: unknown) {
      toast.toastApiError(error);
    }
  };

  return (
    <div className="w-full min-w-0 flex flex-col flex-1 min-h-0 bg-background relative">
      <div className="p-3 sm:p-4 border-b border-line bg-surface flex flex-wrap justify-between items-center gap-3 shrink-0">
        <div>
          <h3 className="font-semibold text-foreground text-base break-all">{chat.consumerPhone}</h3>
          <p className="text-xs text-muted flex items-center mt-0.5">
            <Clock aria-hidden="true" className="w-3 h-3 mr-1" /> Inicio: {new Date(chat.startedAt).toLocaleDateString()}
          </p>
        </div>
        <div>
          {chat.mode === 'Automatic' ? (
            <Button variant="secondary" isLoading={isChangingMode} onClick={onTakeOver} disabled={isChangingMode || !can('CONVERSATIONS', 'TAKEOVER')} className="shrink-0">
              {!isChangingMode && <User aria-hidden="true" className="w-4 h-4 mr-2" />}
              {isChangingMode ? 'Procesando...' : 'Asumir Control'}
            </Button>
          ) : (
            <Button variant="secondary" isLoading={isChangingMode} onClick={onRelease} disabled={isChangingMode || !can('CONVERSATIONS', 'RELEASE')} className="shrink-0">
              {!isChangingMode && <Bot aria-hidden="true" className="w-4 h-4 mr-2" />}
              {isChangingMode ? 'Procesando...' : 'Reactivar IA'}
            </Button>
          )}
        </div>
      </div>

      {chat.mode === 'Automatic' ? (
        <div className="bg-indigo-50 border-b border-indigo-100 px-4 py-2.5 flex items-center text-indigo-700 text-xs leading-relaxed shrink-0">
          <Bot aria-hidden="true" className="w-4 h-4 mr-2 animate-pulse" />
          IA en Piloto Automático. El asistente virtual está gestionando al cliente.
        </div>
      ) : (
        <div className={`border-b px-4 py-2.5 flex items-center text-xs leading-relaxed shrink-0 ${
          chat.handoffReason === 'AiEscalation' || chat.handoffReason === 'SystemError'
            ? 'bg-red-50 border-red-200 text-red-700'
            : 'bg-orange-50 border-orange-200 text-orange-700'
        }`}>
          {chat.handoffReason === 'SystemError' ? (
             <>
               <AlertOctagon aria-hidden="true" className="w-4 h-4 mr-2" />
               Error del Sistema: La IA falló o se desconectó. Asume el control para continuar.
             </>
          ) : chat.handoffReason === 'AiEscalation' ? (
            <>
              <AlertOctagon aria-hidden="true" className="w-4 h-4 mr-2" />
              Alerta de la IA: El bot necesita tu asistencia para resolver esta solicitud.
            </>
          ) : (
            <>
              <AlertTriangle aria-hidden="true" className="w-4 h-4 mr-2" />
              Modo Manual Activo. Estás chateando directamente; la IA está en pausa.
            </>
          )}
        </div>
      )}

      <div className="flex-1 min-h-0 overflow-y-auto p-3 sm:p-6 space-y-4">
        {messages.length === 0 ? (
          <EmptyState title="Sin mensajes en el historial." />
        ) : (
          messages.map(msg => (
            <div key={msg.id} className={`flex ${msg.direction === 'inbound' ? 'justify-start' : 'justify-end'}`}>
              <div className={`nf-message ${msg.direction === 'inbound' ? 'nf-message-consumer' : msg.sender === 'AI' ? 'nf-message-ai' : 'nf-message-human'}`}>
                <p className="text-[10px] font-semibold mb-1 opacity-70">{msg.direction === 'inbound' ? 'Cliente' : msg.sender === 'AI' ? 'Asistente IA' : 'Equipo'}</p>
                <p className="text-sm leading-relaxed whitespace-pre-wrap break-words">
                  {msg.content ? msg.content : (
                    <span className="italic flex items-center opacity-80">
                      <ImageIcon aria-hidden="true" className="w-4 h-4 mr-1" /> [Contenido Multimedia]
                    </span>
                  )}
                </p>
                <div className="text-[10px] mt-2 flex flex-wrap gap-y-1 items-center justify-end text-muted">
                  {msg.sender === 'AI' && <Bot aria-hidden="true" className="w-3 h-3 mr-1" />}
                  {new Date(msg.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}

                  {msg.status === 'Failed' && <Badge tone="error" className="ml-2 text-[10px]">Error de envío</Badge>}
                  {msg.direction === 'outbound' && msg.status !== 'Failed' && <Badge tone={msg.status === 'Sent' ? 'success' : 'warning'} className="ml-2 text-[10px]">{
                    msg.status === 'Attempting' && (!msg.transportLeaseUntil || !Number.isFinite(Date.parse(msg.transportLeaseUntil)) || Date.parse(msg.transportLeaseUntil) <= leaseObservation)
                      ? 'Entrega sin confirmar'
                      : { Pending: 'Pendiente', Attempting: 'Enviando', Sent: 'Enviado', UnknownDelivery: 'Entrega sin confirmar' }[msg.status]
                  }</Badge>}
                </div>
              </div>
            </div>
          ))
        )}
        <div ref={messagesEndRef} />
      </div>

      <div className="p-3 sm:p-4 bg-surface border-t border-line shrink-0">
        <div className="flex items-center gap-2">
          <Input aria-label="Mensaje al cliente"
            type="text"
            value={newMessage}
            onChange={(e) => setNewMessage(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleSend()}
            placeholder={chat.mode === 'Automatic' ? 'Bloqueado. Asume el control manual para enviar un mensaje...' : 'Escribe un mensaje al cliente...'}
            disabled={chat.mode === 'Automatic' || isSending || isChangingMode || !can('CONVERSATIONS', 'SEND_MESSAGE')}
            className="flex-1 min-w-0"
          />
          <IconButton variant="primary" label="Enviar mensaje"
            isLoading={isSending} onClick={handleSend}
            disabled={chat.mode === 'Automatic' || isSending || isChangingMode || !newMessage.trim() || !can('CONVERSATIONS', 'SEND_MESSAGE')}
            className="shrink-0"
          >
            <Send aria-hidden="true" className="w-5 h-5" />
          </IconButton>
        </div>
      </div>
    </div>
  );
};
