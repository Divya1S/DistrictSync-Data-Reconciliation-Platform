import { forwardRef, useId, type InputHTMLAttributes, type SelectHTMLAttributes, type TextareaHTMLAttributes } from 'react';
import { cn } from '@/lib/cn';

export interface FieldProps {
  label: React.ReactNode;
  hint?: React.ReactNode;
  error?: string | null;
  required?: boolean;
  className?: string;
  /** Render prop receives the generated id and describedBy ids for the control. */
  children: (props: { id: string; 'aria-describedby'?: string; 'aria-invalid'?: boolean }) => React.ReactNode;
}

export function Field({ label, hint, error, required, className, children }: FieldProps) {
  const id = useId();
  const hintId = hint ? `${id}-hint` : undefined;
  const errorId = error ? `${id}-error` : undefined;
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined;
  return (
    <div className={cn('flex flex-col gap-1', className)}>
      <label htmlFor={id} className="text-xs font-medium text-ink-2">
        {label}
        {required && <span className="text-crit ml-0.5" aria-hidden="true">*</span>}
      </label>
      {children({ id, 'aria-describedby': describedBy, 'aria-invalid': error ? true : undefined })}
      {hint && !error && <p id={hintId} className="text-[11px] text-muted">{hint}</p>}
      {error && <p id={errorId} className="text-[11px] text-crit-ink" role="alert">{error}</p>}
    </div>
  );
}

export const Input = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement> & { invalid?: boolean; mono?: boolean }>(function Input(
  { className, invalid, mono, ...rest },
  ref,
) {
  return <input ref={ref} className={cn('field', invalid && 'field-invalid', mono && 'font-mono', className)} {...rest} />;
});

export const Select = forwardRef<HTMLSelectElement, SelectHTMLAttributes<HTMLSelectElement> & { invalid?: boolean }>(function Select(
  { className, invalid, children, ...rest },
  ref,
) {
  return (
    <select ref={ref} className={cn('field pr-7 appearance-none bg-no-repeat bg-position-[right_0.5rem_center] bg-size-[14px]', invalid && 'field-invalid', className)}
      style={{ backgroundImage: "url(\"data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 20 20' fill='%23898781'><path d='M5.5 7.5 10 12l4.5-4.5'/></svg>\")" }}
      {...rest}
    >
      {children}
    </select>
  );
});

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaHTMLAttributes<HTMLTextAreaElement> & { invalid?: boolean; mono?: boolean }>(function Textarea(
  { className, invalid, mono, ...rest },
  ref,
) {
  return <textarea ref={ref} className={cn('field min-h-20 resize-y', invalid && 'field-invalid', mono && 'font-mono text-xs', className)} {...rest} />;
});

export function Checkbox({ label, className, ...rest }: InputHTMLAttributes<HTMLInputElement> & { label: React.ReactNode }) {
  const id = useId();
  return (
    <label htmlFor={id} className={cn('inline-flex items-center gap-2 text-sm text-ink cursor-pointer select-none', className)}>
      <input id={id} type="checkbox" className="size-4 rounded border-line-2 accent-[var(--accent)]" {...rest} />
      {label}
    </label>
  );
}
