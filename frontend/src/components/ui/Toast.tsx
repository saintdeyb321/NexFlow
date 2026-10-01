import { createContext, useCallback, useContext, useEffect, useId, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { X } from 'lucide-react';
import { ApiError, getApiErrorPresentation } from '../../core/api/axiosClient';
import { Alert } from './Feedback';
import { IconButton } from './Button';

type ToastTone = 'success' | 'error' | 'warning' | 'info';
type ToastEntry = { id: string; message: string; tone: ToastTone; expiresAt: number; correlationId?: string };
interface ToastApi {
  success: (message: string) => void;
  error: (message: string) => void;
  warning: (message: string) => void;
  info: (message: string) => void;
  toastApiError: (error: unknown) => void;
}
const ToastContext = createContext<ToastApi | null>(null);
export const ToastProvider = ({ children }: { children: ReactNode }) => {
  const [entries, setEntries] = useState<ToastEntry[]>([]);
  const prefix = useId();
  const sequence = useRef(0);
  const dismiss = useCallback((id: string) => setEntries(current => current.filter(entry => entry.id !== id)), []);
  const show = useCallback((tone: ToastTone, message: string, correlationId?: string) => {
    const entry = { id: `${prefix}-${++sequence.current}`, tone, message, correlationId, expiresAt: Date.now() + (tone === 'error' ? 8000 : 5000) };
    setEntries(current => [...current, entry].slice(-5));
  }, [prefix]);
  useEffect(() => {
    const timers = entries.map(entry => setTimeout(() => dismiss(entry.id), Math.max(0, entry.expiresAt - Date.now())));
    return () => timers.forEach(clearTimeout);
  }, [entries, dismiss]);
  const api = useMemo<ToastApi>(() => ({
    success: message => show('success', message), error: message => show('error', message),
    warning: message => show('warning', message), info: message => show('info', message),
    toastApiError: error => show('error', getApiErrorPresentation(error), error instanceof ApiError ? error.correlationId : undefined),
  }), [show]);
  return <ToastContext.Provider value={api}>
    {children}
    <div aria-live="polite" aria-relevant="additions" className="fixed bottom-4 right-4 z-[100] w-[calc(100%-2rem)] max-w-sm space-y-2">
      {entries.map(entry => <Alert key={entry.id} tone={entry.tone} className="shadow-lg flex items-start justify-between gap-3">
        <div><p>{entry.message}</p>{entry.correlationId && <p className="mt-1 text-xs opacity-70">Seguimiento: {entry.correlationId}</p>}</div>
        <IconButton label="Cerrar notificación" size="sm" onClick={() => dismiss(entry.id)}><X className="w-4 h-4" /></IconButton>
      </Alert>)}
    </div>
  </ToastContext.Provider>;
};
export const useToast = () => {
  const api = useContext(ToastContext);
  if (!api) throw new Error('useToast requiere ToastProvider.');
  return api;
};
