import { IconButton } from './Button';
import { Badge, LoadingState, EmptyState, ErrorState } from './Feedback';
import { queryPolicies, usePageVisible } from '../../core/query/queryPolicies';
import { useSessionMutation } from '../../core/query/useSessionMutation';
import { queryKeys } from '../../core/query/queryKeys';
import { useState, useRef, useEffect, useId } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Bell, Calendar, MessageCircle, ClipboardList, AlertTriangle, X } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { getNotifications, markNotificationAsRead, type NotificationDto } from '../../features/notifications/services/notification.service';
import { useAuthStore } from '../../core/store/useAuthStore';
import { usePermissions } from '../../core/auth/permissions';

export const NotificationBell = () => {
  const isPageVisible = usePageVisible();
  const [isOpen, setIsOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const dropdownId = useId();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const me = useAuthStore(state => state.me);
  const { can } = usePermissions();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data: notifications = [], isLoading, isError, refetch } = useQuery({
    ...queryPolicies.dynamic,
    queryKey: queryKeys.notifications.list(workspaceId),
    queryFn: ({ signal }) => getNotifications(signal),
    enabled: !!workspaceId && Object.values(me?.capabilities ?? {}).some(capabilities => capabilities.includes('READ')),
    refetchInterval: isPageVisible ? 30000 : false,
  });

  const readMutation = useSessionMutation({
    mutationFn: markNotificationAsRead,
    onSuccess: (_, notificationId) => {
      queryClient.setQueryData<NotificationDto[]>(queryKeys.notifications.list(workspaceId), (old) => {
        if (!old) return [];
        return old.filter(n => n.id !== notificationId);
      });
    },
  });

  const unreadCount = notifications.filter(n => !n.isRead).length;

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
        setIsOpen(false);
      }
    };
    const handleEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && dropdownRef.current?.contains(document.activeElement)) {
        setIsOpen(false); triggerRef.current?.focus();
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('keydown', handleEscape);
    return () => { document.removeEventListener('mousedown', handleClickOutside); document.removeEventListener('keydown', handleEscape); };
  }, []);

  const getIcon = (moduleCode: string) => {
    switch (moduleCode) {
      case 'RESERVATIONS': return <Calendar aria-hidden="true" className="w-5 h-5 text-primary" />;
      case 'CONVERSATIONS': return <MessageCircle aria-hidden="true" className="w-5 h-5 text-orange-500" />;
      case 'REQUESTS': return <ClipboardList aria-hidden="true" className="w-5 h-5 text-purple-500" />;
      default: return <AlertTriangle aria-hidden="true" className="w-5 h-5 text-yellow-500" />;
    }
  };

  const handleNotificationClick = (n: NotificationDto) => {
    if (!can(n.moduleCode.toUpperCase(), 'READ')) return;
    if (!n.isRead) {
      readMutation.mutate(n.id);
    }
    setIsOpen(false);

    // 🔥 SPRINT 08: Prevención de redirecciones maliciosas y SPA router.
    if (n.actionUrl?.startsWith('/') && !n.actionUrl.startsWith('//') && !n.actionUrl.includes('\\')) {
      const destination = new URL(n.actionUrl, window.location.origin);
      if (destination.origin === window.location.origin) {
        navigate(`${destination.pathname}${destination.search}${destination.hash}`);
      }
    }
  };

  return (
    <div className="relative" ref={dropdownRef}>
      <IconButton ref={triggerRef} label={unreadCount > 0 ? `Notificaciones, ${unreadCount} sin leer` : 'Notificaciones'} aria-expanded={isOpen} aria-controls={isOpen ? dropdownId : undefined} onClick={() => setIsOpen(!isOpen)} className="relative">
        <Bell aria-hidden="true" className="w-5 h-5" />
        {unreadCount > 0 && <span className="absolute -top-0.5 -right-0.5 min-w-4 h-4 px-1 flex items-center justify-center rounded-full bg-danger text-[9px] font-semibold text-white border-2 border-surface">{unreadCount > 9 ? '9+' : unreadCount}</span>}
      </IconButton>

      {isOpen && (
        <div id={dropdownId} role="region" aria-label="Notificaciones" className="fixed inset-x-4 top-20 sm:absolute sm:inset-x-auto sm:top-full sm:right-0 sm:mt-3 sm:w-96 max-w-[calc(100vw-2rem)] nf-panel shadow-xl z-40 overflow-hidden">
          <div className="flex justify-between items-center px-4 py-3 border-b border-line bg-surface-soft gap-3">
            <h3 className="font-semibold text-foreground">Notificaciones</h3>
            <div className="flex items-center gap-2">{unreadCount > 0 && <Badge tone="info">{unreadCount} nuevas</Badge>}<IconButton label="Cerrar notificaciones" onClick={() => { setIsOpen(false); triggerRef.current?.focus(); }}><X aria-hidden="true" className="w-4 h-4" /></IconButton></div>
          </div>

          <div className="max-h-[min(28rem,calc(100dvh-10rem))] overflow-y-auto">
            {isLoading ? <LoadingState title="Cargando notificaciones..." />
              : isError ? <ErrorState title="No pudimos cargar las notificaciones" onRetry={() => void refetch()} />
              : notifications.length === 0 ? <EmptyState title="No tienes notificaciones" icon={<Bell aria-hidden="true" className="w-6 h-6" />} /> : (
              <div className="divide-y divide-gray-100">
                {notifications.map(n => (
                  <button type="button"
                    key={n.id}
                    onClick={() => handleNotificationClick(n)}
                    className={`w-full text-left flex items-start gap-3 p-4 hover:bg-surface-soft transition-colors ${!n.isRead ? 'bg-blue-50/30' : ''}`}
                  >
                    <div className="flex-shrink-0 mt-1 rounded-lg p-2 bg-surface-soft">
                      {getIcon(n.moduleCode)}
                    </div>
                    <div className="flex-1 min-w-0">
                      <p className={`text-sm ${!n.isRead ? 'font-bold text-gray-900' : 'font-medium text-gray-700'}`}>
                        {n.title}
                      </p>
                      <p className="text-sm text-muted line-clamp-2 mt-0.5">{n.message}</p>
                      <p className="text-xs text-gray-400 mt-1">
                        {new Date(n.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                      </p>
                    </div>
                    {!n.isRead && (
                      <div className="flex-shrink-0 ml-2 mt-1">
                        <span className="sr-only">Sin leer</span><span aria-hidden="true" className="flex h-2 w-2 rounded-full bg-primary"></span>
                      </div>
                    )}
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
};
