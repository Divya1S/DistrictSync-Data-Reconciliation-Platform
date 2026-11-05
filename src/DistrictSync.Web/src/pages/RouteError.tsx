import { Link, isRouteErrorResponse, useRouteError } from 'react-router';
import { errorMessage } from '@/api/client';
import { Button } from '@/components/ui/Button';

export default function RouteError() {
  const error = useRouteError();
  const title = isRouteErrorResponse(error) ? `${error.status} ${error.statusText}` : 'Something went wrong';
  const detail = isRouteErrorResponse(error) ? (typeof error.data === 'string' ? error.data : undefined) : errorMessage(error);
  return (
    <div className="min-h-dvh grid place-items-center bg-bg p-6">
      <div className="card max-w-md w-full p-6 text-center">
        <p className="text-base font-semibold text-ink">{title}</p>
        {detail && <p className="text-sm text-ink-2 mt-2 break-words">{detail}</p>}
        <div className="mt-4 flex justify-center gap-2">
          <Button onClick={() => window.location.reload()}>Reload</Button>
          <Link to="/"><Button variant="primary">Back to overview</Button></Link>
        </div>
      </div>
    </div>
  );
}
