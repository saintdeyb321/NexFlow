import type { ReactNode } from 'react';
import { Modal } from './Modal';
import { Button } from './Button';

interface ConfirmDialogProps {
  isOpen: boolean;
  title: string;
  description?: ReactNode;
  children?: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
  isLoading?: boolean;
  confirmDisabled?: boolean;
  onConfirm: () => void;
  onClose: () => void;
}
export const ConfirmDialog = ({ isOpen, title, description, children, confirmLabel = 'Confirmar', cancelLabel = 'Cancelar',
  destructive = false, isLoading = false, confirmDisabled = false, onConfirm, onClose }: ConfirmDialogProps) => (
  <Modal isOpen={isOpen} title={title} onClose={onClose} closeDisabled={isLoading}>
    {description && <div className="text-sm text-muted mb-4">{description}</div>}
    {children}
    <div className="flex flex-wrap justify-end gap-2 mt-6">
      <Button variant="secondary" disabled={isLoading} onClick={onClose} data-autofocus>{cancelLabel}</Button>
      <Button variant={destructive ? 'danger' : 'primary'} isLoading={isLoading} disabled={confirmDisabled} onClick={onConfirm}>{confirmLabel}</Button>
    </div>
  </Modal>
);
