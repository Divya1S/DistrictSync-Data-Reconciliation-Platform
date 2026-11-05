import { cn } from '@/lib/cn';
import { Skeleton } from './Skeleton';
import { EmptyState } from './EmptyState';

export interface Column<T> {
  key: string;
  header: React.ReactNode;
  cell: (row: T) => React.ReactNode;
  className?: string;
  headerClassName?: string;
  align?: 'left' | 'right' | 'center';
  width?: string;
}

export interface DataTableProps<T> {
  columns: Array<Column<T>>;
  rows: T[] | undefined;
  rowKey: (row: T) => string;
  isLoading?: boolean;
  emptyTitle?: string;
  emptyDescription?: string;
  emptyAction?: React.ReactNode;
  onRowClick?: (row: T) => void;
  rowClassName?: (row: T) => string | undefined;
  /** Height of the scroll area; headers stick to the top of it. */
  maxHeight?: string;
  className?: string;
  skeletonRows?: number;
  caption?: string;
}

const aligns = { left: 'text-left', right: 'text-right', center: 'text-center' };

export function DataTable<T>({
  columns, rows, rowKey, isLoading = false, emptyTitle = 'Nothing here yet', emptyDescription, emptyAction, onRowClick, rowClassName, maxHeight = 'max-h-[70vh]', className, skeletonRows = 6, caption,
}: DataTableProps<T>) {
  const showSkeleton = isLoading && (!rows || rows.length === 0);
  const showEmpty = !isLoading && (!rows || rows.length === 0);

  return (
    <div className={cn('table-wrap', maxHeight, className)} aria-busy={isLoading || undefined}>
      <table className="w-full text-sm border-collapse">
        {caption && <caption className="sr-only">{caption}</caption>}
        <thead>
          <tr>
            {columns.map((c) => (
              <th key={c.key} scope="col" style={c.width ? { width: c.width } : undefined} className={cn(aligns[c.align ?? 'left'], c.headerClassName)}>
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {showSkeleton &&
            Array.from({ length: skeletonRows }).map((_, i) => (
              <tr key={`sk-${i}`}>
                {columns.map((c) => (
                  <td key={c.key}>
                    <Skeleton className={cn('h-3.5', i % 3 === 0 ? 'w-3/4' : i % 3 === 1 ? 'w-1/2' : 'w-2/3')} />
                  </td>
                ))}
              </tr>
            ))}
          {showEmpty && (
            <tr>
              <td colSpan={columns.length} className="border-b-0!">
                <EmptyState title={emptyTitle} description={emptyDescription} action={emptyAction} compact />
              </td>
            </tr>
          )}
          {rows?.map((row) => {
            const clickable = Boolean(onRowClick);
            return (
              <tr
                key={rowKey(row)}
                className={cn(clickable && 'row-clickable', isLoading && 'opacity-60 transition-opacity', rowClassName?.(row))}
                onClick={clickable ? () => onRowClick?.(row) : undefined}
                onKeyDown={clickable ? (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onRowClick?.(row); } } : undefined}
                tabIndex={clickable ? 0 : undefined}
              >
                {columns.map((c) => (
                  <td key={c.key} className={cn(aligns[c.align ?? 'left'], c.className)}>
                    {c.cell(row)}
                  </td>
                ))}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
