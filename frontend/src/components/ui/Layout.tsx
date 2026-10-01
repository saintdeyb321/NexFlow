import type { ComponentProps, ReactNode } from 'react';

export const PageHeader = ({ title, description, icon, actions }: { title: string; description?: ReactNode; icon?: ReactNode; actions?: ReactNode }) => (
  <div className="mb-6 flex flex-col sm:flex-row sm:justify-between sm:items-center gap-4">
    <div><h1 className="text-2xl font-bold text-gray-900 flex items-center gap-3">{icon && <span aria-hidden="true">{icon}</span>}{title}</h1>
      {description && <p className="mt-1 text-sm text-gray-500">{description}</p>}</div>
    {actions && <div className="flex items-center gap-3">{actions}</div>}
  </div>
);
export const Card = ({ header, padding = 'md', className = '', children, ...props }: Omit<ComponentProps<'section'>, 'title'> & { header?: ReactNode; padding?: 'none' | 'sm' | 'md' }) => (
  <section {...props} className={`bg-white border border-gray-200 rounded-xl shadow-sm ${className}`}>
    {header && <div className="px-6 py-4 border-b border-gray-100">{header}</div>}
    <div className={padding === 'none' ? '' : padding === 'sm' ? 'p-4' : 'p-6'}>{children}</div>
  </section>
);
