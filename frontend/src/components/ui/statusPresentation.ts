import type { Tone } from './Feedback';

// Presentation only: lifecycle transitions stay with their feature contracts.
export const getStatusTone = (status: string): Tone => {
  const value = status.toUpperCase();
  if (['ACTIVE', 'COMPLETED', 'SENT', 'CONNECTED', 'CONFIRMED'].includes(value)) return 'success';
  if (['PENDING', 'PENDINGREVIEW', 'INREVIEW', 'ATTEMPTING', 'PROCESSING', 'DELETING', 'UNKNOWNDELIVERY', 'CONNECTING', 'QR_AVAILABLE', 'QR_EXPIRED'].includes(value)) return 'warning';
  if (['FAILED', 'REJECTED', 'ERROR', 'SUSPENDED'].includes(value)) return 'error';
  if (['APPROVED', 'AUTOMATIC'].includes(value)) return 'info';
  return 'neutral';
};

export const getStatusLabel = (status: string): string => ({
  PENDING: 'Pendiente', PENDINGREVIEW: 'Por confirmar', INREVIEW: 'En revisión',
  APPROVED: 'Aprobado', PROCESSING: 'En proceso', COMPLETED: 'Completado',
  REJECTED: 'Rechazado', CANCELLED: 'Cancelado', UNKNOWNDELIVERY: 'Entrega sin confirmar',
  ATTEMPTING: 'Enviando', SENT: 'Enviado', FAILED: 'Error de envío',
} as Record<string, string>)[status.toUpperCase()] ?? status;
