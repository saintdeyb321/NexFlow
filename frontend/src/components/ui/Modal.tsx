import { X } from 'lucide-react';
import { createContext, useContext, useId, useLayoutEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import type { ReactNode } from 'react';
import { IconButton } from './Button';

interface ModalProps {
  isOpen: boolean;
  onClose: () => void;
  title: ReactNode;
  children: ReactNode;
  maxWidth?: string;
  size?: 'sm' | 'md' | 'lg' | 'xl';
  closeDisabled?: boolean;
  closeOnEscape?: boolean;
  closeOnBackdrop?: boolean;
}
const sizes = { sm: 'max-w-sm', md: 'max-w-md', lg: 'max-w-lg', xl: 'max-w-2xl' };
const ModalDepth = createContext(0);
const openDialogs: { element: HTMLElement; depth: number }[] = [];
let previousOverflow = '';
let initialFocus: HTMLElement | null = null;
const focusableSelector = 'button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])';

export const Modal = ({ isOpen, onClose, title, children, maxWidth, size = 'md', closeDisabled = false,
  closeOnEscape = true, closeOnBackdrop = true }: ModalProps) => {
  const titleId = useId();
  const depth = useContext(ModalDepth);
  const dialog = useRef<HTMLDivElement>(null);
  const closing = useRef({ onClose, closeDisabled, closeOnEscape });
  useLayoutEffect(() => { closing.current = { onClose, closeDisabled, closeOnEscape }; }, [onClose, closeDisabled, closeOnEscape]);
  useLayoutEffect(() => {
    const element = dialog.current;
    if (!isOpen || !element) return;
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    if (openDialogs.length === 0) {
      previousOverflow = document.body.style.overflow;
      initialFocus = previousFocus;
      document.body.style.overflow = 'hidden';
    }
    const entry = { element, depth };
    openDialogs.push(entry);
    openDialogs.sort((a, b) => a.depth - b.depth);
    const isTop = () => openDialogs[openDialogs.length - 1]?.element === element;
    const focusables = () => Array.from(element.querySelectorAll<HTMLElement>(focusableSelector))
      .filter(control => control.tabIndex >= 0 && !control.matches(':disabled') && !control.closest('[hidden], [inert], [aria-hidden="true"]') && control.getClientRects().length > 0);
    const focusFirst = () => {
      const controls = focusables();
      (controls.find(control => control.hasAttribute('data-autofocus')) ?? controls[0] ?? element).focus();
    };
    if (isTop()) focusFirst();
    const handleKey = (event: KeyboardEvent) => {
      if (!isTop()) return;
      if (event.key === 'Escape') {
        event.preventDefault(); event.stopImmediatePropagation();
        if (!closing.current.closeDisabled && closing.current.closeOnEscape) closing.current.onClose();
      }
      if (event.key !== 'Tab') return;
      const controls = focusables();
      const first = controls[0], last = controls[controls.length - 1];
      if (!first) { event.preventDefault(); element.focus(); return; }
      if (!element.contains(document.activeElement) || document.activeElement === element) {
        event.preventDefault(); (event.shiftKey ? last : first).focus();
      } else if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    const handleFocus = (event: FocusEvent) => { if (isTop() && event.target instanceof Node && !element.contains(event.target)) focusFirst(); };
    document.addEventListener('keydown', handleKey, true);
    document.addEventListener('focusin', handleFocus);
    return () => {
      document.removeEventListener('keydown', handleKey, true);
      document.removeEventListener('focusin', handleFocus);
      const index = openDialogs.indexOf(entry);
      if (index >= 0) openDialogs.splice(index, 1);
      const top = openDialogs[openDialogs.length - 1]?.element;
      if (!top) {
        document.body.style.overflow = previousOverflow;
        if (initialFocus?.isConnected) initialFocus.focus();
        initialFocus = null;
      } else if (previousFocus?.isConnected && top.contains(previousFocus)) previousFocus.focus();
      else top?.focus();
    };
  }, [isOpen, depth]);
  if (!isOpen) return null;
  return createPortal(
    <ModalDepth.Provider value={depth + 1}>
    <div style={{ zIndex: 50 + depth }} className="fixed inset-0 flex items-center justify-center bg-gray-900/40 backdrop-blur-sm p-4"
      onClick={event => { if (event.target === event.currentTarget && closeOnBackdrop && !closeDisabled) onClose(); }}>
      <div ref={dialog} role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}
        className={`bg-white rounded-2xl shadow-2xl w-full ${maxWidth ?? sizes[size]} animate-in fade-in zoom-in-95 duration-200 flex flex-col max-h-[90vh]`}>
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-100">
          <h2 id={titleId} className="text-xl font-bold text-gray-800">{title}</h2>
          <IconButton label="Cerrar diálogo" disabled={closeDisabled} onClick={onClose}
            className="text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-full"><X className="w-5 h-5" /></IconButton>
        </div>
        <div className="p-6 overflow-y-auto">{children}</div>
      </div>
    </div></ModalDepth.Provider>, document.body,
  );
};
