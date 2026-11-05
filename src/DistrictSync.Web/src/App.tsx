import { lazy } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createBrowserRouter } from 'react-router';
import { isApiError } from '@/api/client';
import { RequireAuth } from '@/auth/RequireAuth';
import { AppShell } from '@/components/layout/AppShell';
import { Toaster } from '@/components/ui/Toaster';
import { useTheme } from '@/lib/theme';
import RouteError from '@/pages/RouteError';

// Route pages are code-split; the shell renders a skeleton while a chunk loads.
const LoginPage = lazy(() => import('@/features/auth/LoginPage'));
const OverviewPage = lazy(() => import('@/features/dashboard/OverviewPage'));
const IngestionPage = lazy(() => import('@/features/ingestion/IngestionPage'));
const ReconciliationPage = lazy(() => import('@/features/reconciliation/ReconciliationPage'));
const RunDetailPage = lazy(() => import('@/features/reconciliation/RunDetailPage'));
const CasesPage = lazy(() => import('@/features/cases/CasesPage'));
const CaseDetailPage = lazy(() => import('@/features/cases/CaseDetailPage'));
const RulesPage = lazy(() => import('@/features/rules/RulesPage'));
const RuleSetDetailPage = lazy(() => import('@/features/rules/RuleSetDetailPage'));
const OperationsPage = lazy(() => import('@/features/operations/OperationsPage'));
const SourcesPage = lazy(() => import('@/features/sources/SourcesPage'));
const NotFoundPage = lazy(() => import('@/pages/NotFoundPage'));

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 15_000,
      refetchOnWindowFocus: true,
      retry: (failureCount, error) => {
        // Client errors (4xx) will not fix themselves; transient failures get two more tries.
        if (isApiError(error) && error.status > 0 && error.status < 500) return false;
        return failureCount < 2;
      },
    },
    mutations: { retry: false },
  },
});

const router = createBrowserRouter([
  { path: '/login', element: <LoginPage />, errorElement: <RouteError /> },
  {
    element: <RequireAuth />,
    errorElement: <RouteError />,
    children: [
      {
        element: <AppShell />,
        children: [
          { index: true, element: <OverviewPage /> },
          { path: 'ingestion', element: <IngestionPage /> },
          { path: 'reconciliation', element: <ReconciliationPage /> },
          { path: 'reconciliation/runs/:id', element: <RunDetailPage /> },
          { path: 'cases', element: <CasesPage /> },
          { path: 'cases/:id', element: <CaseDetailPage /> },
          { path: 'rules', element: <RulesPage /> },
          { path: 'rules/:id', element: <RuleSetDetailPage /> },
          { path: 'operations', element: <OperationsPage /> },
          { path: 'sources', element: <SourcesPage /> },
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
]);

function ThemeSync() {
  useTheme();
  return null;
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ThemeSync />
      <RouterProvider router={router} />
      <Toaster />
    </QueryClientProvider>
  );
}
