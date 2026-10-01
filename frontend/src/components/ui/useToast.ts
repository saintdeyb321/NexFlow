import { createContext, useContext } from 'react';

export interface ToastApi {
  success: (message: string) => void;
  error: (message: string) => void;
  warning: (message: string) => void;
  info: (message: string) => void;
  toastApiError: (error: unknown) => void;
}

export const ToastContext = createContext<ToastApi | null>(null);

export const useToast = () => {
  const api = useContext(ToastContext);
  if (!api) throw new Error('useToast requiere ToastProvider.');
  return api;
};
