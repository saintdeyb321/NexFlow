import type { ComponentProps, ReactNode } from 'react';
import { Loader2 } from 'lucide-react';

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type ButtonProps = ComponentProps<'button'> & {
  variant?: Variant;
  size?: 'sm' | 'md';
  isLoading?: boolean;
};
export const Button = ({ variant = 'primary', size = 'md', isLoading = false, disabled,
  type = 'button', className = '', children, ...props }: ButtonProps) => (
  <button {...props} type={type} disabled={disabled || isLoading} aria-busy={isLoading || undefined}
    className={`nf-button nf-button-${variant} ${size === 'sm' ? 'text-xs px-3' : ''} ${className}`}>
    {isLoading && <Loader2 aria-hidden="true" className="w-4 h-4 animate-spin shrink-0" />}
    {children}
  </button>
);
type IconButtonProps = Omit<ButtonProps, 'children' | 'aria-label'> & { label: string; children: ReactNode };
export const IconButton = ({ label, children, className = '', variant = 'ghost', isLoading, ...props }: IconButtonProps) => (
  <Button {...props} variant={variant} isLoading={isLoading} aria-label={label} className={`min-w-11 shrink-0 px-2 ${className}`}>
    {!isLoading && <span aria-hidden="true" className="inline-flex">{children}</span>}
  </Button>
);
