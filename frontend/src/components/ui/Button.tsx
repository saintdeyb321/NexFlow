import type { ComponentProps, ReactNode } from 'react';
import { Loader2 } from 'lucide-react';

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type ButtonProps = ComponentProps<'button'> & {
  variant?: Variant;
  size?: 'sm' | 'md';
  isLoading?: boolean;
};
const variants: Record<Variant, string> = {
  primary: 'bg-blue-600 text-white hover:bg-blue-700',
  secondary: 'bg-gray-100 text-gray-700 hover:bg-gray-200',
  ghost: 'text-gray-600 hover:bg-gray-100',
  danger: 'bg-red-600 text-white hover:bg-red-700',
};

export const Button = ({ variant = 'primary', size = 'md', isLoading = false, disabled,
  type = 'button', className = '', children, ...props }: ButtonProps) => {
  // Retain explicit styles at migrated call sites while providing defaults for new buttons.
  const custom = className.split(/\s+/);
  const colors = variants[variant].split(' ').filter(token => {
    const prefix = token.startsWith('hover:') ? 'hover:' : '';
    return !custom.some(value => token.includes('bg-') ? value.startsWith(`${prefix}bg-`)
      : value.startsWith('text-') && !/^text-(xs|sm|base|lg|xl|\d)/.test(value));
  }).join(' ');
  const padding = [custom.some(value => /^(p|px)-/.test(value)) ? '' : size === 'sm' ? 'px-3' : 'px-4',
    custom.some(value => /^(p|py)-/.test(value)) ? '' : size === 'sm' ? 'py-1.5' : 'py-2'].join(' ');
  return (
  <button {...props} type={type} disabled={disabled || isLoading} aria-busy={isLoading || undefined}
    className={`inline-flex items-center justify-center gap-2 rounded-lg font-medium text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-2 disabled:opacity-50 disabled:cursor-not-allowed ${padding} ${colors} ${className}`}>
    {isLoading && <Loader2 aria-hidden="true" className="w-4 h-4 animate-spin" />}
    {children}
  </button>
  );
};

type IconButtonProps = Omit<ButtonProps, 'children' | 'aria-label'> & { label: string; children: ReactNode };
export const IconButton = ({ label, children, className = '', variant = 'ghost', isLoading, ...props }: IconButtonProps) => (
  <Button {...props} variant={variant} isLoading={isLoading} aria-label={label} className={`p-2 ${className}`}>
    {!isLoading && <span aria-hidden="true" className="inline-flex">{children}</span>}
  </Button>
);
