import { Suspense, useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router';
import { useAuth } from '@/auth/useAuth';
import { cn } from '@/lib/cn';
import { useTheme } from '@/lib/theme';
import { useSignalR } from '@/realtime/useSignalR';
import { useTenant } from '@/features/tenant/hooks';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { SkeletonLines } from '@/components/ui/Skeleton';
import { ConnectionIndicator } from './ConnectionIndicator';
import { Icons, type IconName } from './icons';

const NAV: Array<{ to: string; label: string; icon: IconName; end?: boolean }> = [
  { to: '/', label: 'Overview', icon: 'overview', end: true },
  { to: '/ingestion', label: 'Ingestion', icon: 'ingestion' },
  { to: '/reconciliation', label: 'Reconciliation', icon: 'reconciliation' },
  { to: '/cases', label: 'Cases', icon: 'cases' },
  { to: '/rules', label: 'Rules', icon: 'rules' },
  { to: '/operations', label: 'Operations', icon: 'operations' },
  { to: '/sources', label: 'Source systems', icon: 'sources' },
];

export function AppShell() {
  const auth = useAuth();
  const navigate = useNavigate();
  const theme = useTheme();
  const tenant = useTenant();
  const [mobileOpen, setMobileOpen] = useState(false);
  useSignalR(auth.token);

  const logout = () => {
    auth.logout();
    void navigate('/login', { replace: true });
  };

  return (
    <div className="min-h-dvh flex bg-bg">
      {/* Sidebar */}
      <aside
        className={cn(
          'fixed inset-y-0 left-0 z-40 flex w-60 flex-col border-r border-line bg-surface transition-transform md:static md:translate-x-0 md:w-16 lg:w-60',
          mobileOpen ? 'translate-x-0' : '-translate-x-full',
        )}
        aria-label="Primary"
      >
        <div className="flex h-14 items-center gap-2.5 px-4 border-b border-line">
          <span className="grid size-8 place-items-center rounded-md bg-accent text-on-accent font-bold text-sm shrink-0" aria-hidden="true">DS</span>
          <div className="min-w-0 md:hidden lg:block">
            <p className="text-sm font-semibold leading-4 text-ink truncate">DistrictSync</p>
            <p className="text-[11px] text-muted leading-4">Operations console</p>
          </div>
        </div>
        <nav className="flex-1 overflow-y-auto p-2 space-y-0.5">
          {NAV.map((item) => {
            const Icon = Icons[item.icon];
            return (
              <NavLink key={item.to} to={item.to} end={item.end} className="nav-link md:justify-center lg:justify-start" title={item.label} onClick={() => setMobileOpen(false)}>
                <Icon className="size-4.5 shrink-0" />
                <span className="md:hidden lg:inline truncate">{item.label}</span>
              </NavLink>
            );
          })}
        </nav>
        <div className="p-3 border-t border-line text-[11px] text-muted md:hidden lg:block">
          <p className="truncate">API <code className="mono">/api/v1</code></p>
          <p className="truncate">Hub <code className="mono">/hubs/operations</code></p>
        </div>
      </aside>
      {mobileOpen && <div className="fixed inset-0 z-30 bg-black/40 md:hidden" onClick={() => setMobileOpen(false)} aria-hidden="true" />}

      {/* Main column */}
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-20 flex h-14 items-center gap-3 border-b border-line bg-surface/90 backdrop-blur px-4">
          <Button variant="ghost" size="sm" className="md:hidden -ml-2" onClick={() => setMobileOpen(true)} aria-label="Open navigation" aria-expanded={mobileOpen}>
            <Icons.menu className="size-5" />
          </Button>
          <div className="min-w-0 flex items-center gap-2">
            {tenant.isLoading ? (
              <div className="skeleton h-4 w-32" />
            ) : tenant.data ? (
              <>
                <span className="text-sm font-semibold text-ink truncate">{tenant.data.name}</span>
                <code className="mono text-muted hidden sm:inline">{tenant.data.slug}</code>
                {!tenant.data.isActive && <Badge tone="crit">inactive</Badge>}
              </>
            ) : (
              <span className="text-sm text-muted">Tenant unavailable</span>
            )}
          </div>
          <div className="ml-auto flex items-center gap-2">
            <ConnectionIndicator />
            <div className="hidden sm:flex items-center gap-1.5 pl-2 border-l border-line">
              <span className="text-sm text-ink truncate max-w-40" title={auth.userId ?? undefined}>{auth.userId}</span>
              <span className="hidden lg:flex items-center gap-1">
                {auth.roles.map((r) => (
                  <Badge key={r} tone={r === 'admin' ? 'violet' : r === 'approver' ? 'good' : 'neutral'}>{r}</Badge>
                ))}
              </span>
            </div>
            <Button variant="ghost" size="sm" onClick={theme.toggle} aria-label={theme.resolved === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'} title={theme.resolved === 'dark' ? 'Light mode' : 'Dark mode'}>
              {theme.resolved === 'dark' ? <Icons.sun className="size-4" /> : <Icons.moon className="size-4" />}
            </Button>
            <Button variant="ghost" size="sm" onClick={logout} aria-label="Log out" title="Log out">
              <Icons.logout className="size-4" />
            </Button>
          </div>
        </header>
        <main id="main" className="flex-1 min-w-0 p-4 lg:p-6">
          <Suspense fallback={<div className="card p-6"><SkeletonLines lines={5} /></div>}>
            <Outlet />
          </Suspense>
        </main>
      </div>
    </div>
  );
}
