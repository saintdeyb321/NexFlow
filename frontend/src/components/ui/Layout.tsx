import type { ComponentProps, ReactNode } from 'react';

export const PageHeader = ({ title, description, icon, actions }: { title: string; description?: ReactNode; icon?: ReactNode; actions?: ReactNode }) => (
  <div className="mb-7 flex flex-col xl:flex-row xl:justify-between xl:items-center gap-5 min-w-0">
    <div className="min-w-0 flex items-start gap-3">
      {icon && <span aria-hidden="true" className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-indigo-100 text-primary">{icon}</span>}
      <div className="min-w-0"><h1 className="text-2xl sm:text-3xl font-semibold tracking-tight text-foreground break-words">{title}</h1>
        {description && <p className="mt-1.5 text-sm leading-relaxed text-muted max-w-2xl">{description}</p>}</div>
    </div>
    {actions && <div className="flex flex-wrap items-center gap-2 shrink-0">{actions}</div>}
  </div>
);
export const Card = ({ header, padding = 'md', className = '', children, ...props }: Omit<ComponentProps<'section'>, 'title'> & { header?: ReactNode; padding?: 'none' | 'sm' | 'md' }) => (
  <section {...props} className={`nf-panel min-w-0 ${className}`}>
    {header && <div className="px-5 sm:px-6 py-4 border-b border-line">{header}</div>}
    <div className={padding === 'none' ? '' : padding === 'sm' ? 'p-4' : 'p-5 sm:p-6'}>{children}</div>
  </section>
);
