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
  placement?: 'center' | 'drawer';
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
  closeOnEscape = true, closeOnBackdrop = true, placement = 'center' }: ModalProps) => {
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
    <div style={{ zIndex: 50 + depth }} className={`fixed inset-0 flex bg-sidebar/55 backdrop-blur-sm ${placement === 'drawer' ? 'items-stretch justify-start' : 'items-center justify-center p-3 sm:p-6'}`}
      onClick={event => { if (event.target === event.currentTarget && closeOnBackdrop && !closeDisabled) onClose(); }}>
      <div ref={dialog} role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}
        className={`shadow-2xl min-w-0 flex flex-col ${placement === 'drawer' ? 'w-72 max-w-[85vw] h-dvh bg-sidebar text-white' : `bg-surface border border-line rounded-2xl w-full ${maxWidth ?? sizes[size]} max-h-[calc(100dvh-1.5rem)] sm:max-h-[90dvh]`}`}>
        <div className={`flex items-center justify-between px-5 sm:px-6 py-4 border-b shrink-0 gap-3 ${placement === 'drawer' ? 'border-white/10' : 'border-line'}`}>
          <h2 id={titleId} className={`text-lg font-semibold ${placement === 'drawer' ? 'text-white' : 'text-foreground'}`}>{title}</h2>
          <IconButton label="Cerrar diálogo" disabled={closeDisabled} onClick={onClose}
            className={placement === 'drawer' ? 'text-slate-300 hover:text-white hover:bg-sidebar-hover' : 'text-muted'}><X aria-hidden="true" className="w-5 h-5" /></IconButton>
        </div>
        <div className={`overflow-y-auto min-h-0 ${placement === 'drawer' ? 'flex-1' : 'p-5 sm:p-6'}`}>{children}</div>
      </div>
    </div></ModalDepth.Provider>, document.body,
  );
};
