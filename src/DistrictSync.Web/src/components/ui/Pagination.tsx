import { Button } from './Button';
import { Select } from './Field';
import { fmtNumber } from '@/lib/format';

export interface PaginationProps {
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (page: number) => void;
  onPageSizeChange?: (size: number) => void;
  pageSizes?: number[];
}

export function Pagination({ page, pageSize, totalCount, onPageChange, onPageSizeChange, pageSizes = [10, 25, 50, 100] }: PaginationProps) {
  const totalPages = Math.max(1, Math.ceil(totalCount / Math.max(1, pageSize)));
  const from = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(totalCount, page * pageSize);
  return (
    <nav className="flex flex-wrap items-center justify-between gap-3 px-3 py-2 border-t border-line text-xs text-ink-2" aria-label="Pagination">
      <div className="flex items-center gap-2">
        <span>
          {fmtNumber(from)}–{fmtNumber(to)} of {fmtNumber(totalCount)}
        </span>
        {onPageSizeChange && (
          <label className="inline-flex items-center gap-1.5">
            <span className="sr-only">Rows per page</span>
            <Select value={pageSize} onChange={(e) => onPageSizeChange(Number(e.target.value))} className="h-7 w-auto py-0 text-xs" aria-label="Rows per page">
              {pageSizes.map((s) => (
                <option key={s} value={s}>{s} / page</option>
              ))}
            </Select>
          </label>
        )}
      </div>
      <div className="flex items-center gap-1">
        <Button size="sm" variant="ghost" onClick={() => onPageChange(1)} disabled={page <= 1} aria-label="First page">«</Button>
        <Button size="sm" variant="ghost" onClick={() => onPageChange(page - 1)} disabled={page <= 1} aria-label="Previous page">‹</Button>
        <span className="px-2 tabular-nums" aria-current="page">Page {page} of {totalPages}</span>
        <Button size="sm" variant="ghost" onClick={() => onPageChange(page + 1)} disabled={page >= totalPages} aria-label="Next page">›</Button>
        <Button size="sm" variant="ghost" onClick={() => onPageChange(totalPages)} disabled={page >= totalPages} aria-label="Last page">»</Button>
      </div>
    </nav>
  );
}
