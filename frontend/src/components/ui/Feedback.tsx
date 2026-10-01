import type { ComponentProps, ReactNode } from 'react';
import { AlertTriangle, Loader2 } from 'lucide-react';
import { Button } from './Button';

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'error';
const tones: Record<Tone, string> = {
  neutral: 'bg-surface-soft text-muted border-line',
  info: 'bg-indigo-50 text-indigo-700 border-indigo-100',
  success: 'bg-emerald-50 text-emerald-800 border-emerald-200',
  warning: 'bg-amber-50 text-amber-800 border-amber-200',
  error: 'bg-red-50 text-red-800 border-red-200',
};
const toneStyles = (tone: Tone, className: string) => {
  const custom = className.split(/\s+/);
  return tones[tone].split(' ').filter(token => !custom.some(value =>
    token.startsWith('bg-') ? value.startsWith('bg-')
      : token.startsWith('border-') ? /^border-[a-z]+-\d/.test(value)
        : /^text-[a-z]+-\d/.test(value) || value === 'text-white')).join(' ');
};
export const Alert = ({ tone = 'info', className = '', ...props }: ComponentProps<'div'> & { tone?: Tone }) => (
  <div role={tone === 'error' ? 'alert' : 'status'} {...props} className={`px-4 py-3 rounded-xl border text-sm leading-relaxed ${toneStyles(tone, className)} ${className}`} />
);
export const Badge = ({ tone = 'neutral', className = '', ...props }: ComponentProps<'span'> & { tone?: Tone }) => (
  <span {...props} className={`inline-flex items-center rounded-full px-2.5 py-1 text-xs font-semibold whitespace-nowrap ${toneStyles(tone, className)} ${className}`} />
);
export const StatusBadge = ({ label, tone = 'neutral', className = '' }: { label: string; tone?: Tone; className?: string }) => (
  <Badge tone={tone} className={className}>{label}</Badge>
);

interface StateProps { icon?: ReactNode; title?: ReactNode; description?: ReactNode; action?: ReactNode; className?: string }
export const EmptyState = ({ icon, title = 'No hay datos', description, action, className = '' }: StateProps) => (
  <div className={`px-5 py-10 text-center flex flex-col items-center justify-center text-muted ${className}`}>
    {icon && <div aria-hidden="true" className="mb-4 flex h-14 w-14 items-center justify-center rounded-2xl bg-surface-soft text-muted">{icon}</div>}
    <p className="font-semibold text-foreground">{title}</p>
    {description && <p className="mt-2 text-sm">{description}</p>}
    {action && <div className="mt-4">{action}</div>}
  </div>
);
export const LoadingState = ({ icon = <Loader2 aria-hidden="true" className="w-6 h-6 animate-spin" />, title = 'Cargando...', ...props }: StateProps) => (
  <div role="status" aria-live="polite"><EmptyState {...props} icon={icon} title={title} /></div>
);
export const ErrorState = ({ title = 'No se pudo cargar la información', icon = <AlertTriangle aria-hidden="true" className="w-8 h-8 text-red-500" />, onRetry, ...props }: StateProps & { onRetry?: () => void }) => (
  <div role="alert"><EmptyState {...props} title={title} icon={icon}
    action={props.action ?? (onRetry ? <Button onClick={onRetry} variant="secondary">Reintentar</Button> : undefined)} /></div>
);
export const Skeleton = ({ className = '', ...props }: ComponentProps<'div'>) => (
  <div {...props} aria-hidden="true" className={`animate-pulse bg-line/70 rounded-xl ${className}`} />
);
