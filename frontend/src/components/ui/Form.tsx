import { cloneElement, isValidElement, useId } from 'react';
import type { ComponentProps, ReactNode } from 'react';

const controlClass = 'w-full border border-gray-300 rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm disabled:opacity-50';
export const Input = ({ className = '', type = 'text', ...props }: ComponentProps<'input'>) => (
  <input {...props} type={type} className={`${type === 'checkbox' || type === 'radio' ? 'w-4 h-4 text-blue-600' : controlClass} ${className}`} />
);
export const Select = ({ className = '', ...props }: ComponentProps<'select'>) => (
  <select {...props} className={`${controlClass} bg-white ${className}`} />
);
export const Textarea = ({ className = '', ...props }: ComponentProps<'textarea'>) => (
  <textarea {...props} className={`${controlClass} resize-none ${className}`} />
);

type ControlProps = Pick<ComponentProps<'input'>, 'id' | 'required' | 'aria-describedby' | 'aria-invalid'>;
interface FormFieldProps {
  label: ReactNode;
  id?: string;
  required?: boolean;
  helperText?: ReactNode;
  error?: ReactNode;
  className?: string;
  children: ReactNode | ((props: ControlProps) => ReactNode);
}
export const FormField = ({ label, id, required, helperText, error, className = '', children }: FormFieldProps) => {
  const generatedId = useId();
  const childProps = isValidElement<ControlProps>(children) ? children.props : undefined;
  const controlId = id ?? childProps?.id ?? generatedId;
  const helperId = `${controlId}-helper`;
  const errorId = `${controlId}-error`;
  const props: ControlProps = {
    id: controlId,
    required: required ?? childProps?.required,
    'aria-invalid': error ? true : childProps?.['aria-invalid'],
    'aria-describedby': [childProps?.['aria-describedby'], helperText ? helperId : '', error ? errorId : ''].filter(Boolean).join(' ') || undefined,
  };
  return <div className={className}>
    <label htmlFor={controlId} className="block text-sm font-medium text-gray-700 mb-1">{label}{props.required && <span aria-hidden="true"> *</span>}</label>
    {typeof children === 'function' ? children(props) : isValidElement<ControlProps>(children) ? cloneElement(children, props) : children}
    {helperText && <p id={helperId} className="mt-1 text-xs text-gray-500">{helperText}</p>}
    {error && <p id={errorId} role="alert" className="mt-1 text-sm text-red-600">{error}</p>}
  </div>;
};
