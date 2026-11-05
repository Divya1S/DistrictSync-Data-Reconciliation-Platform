import { useState, type FormEvent } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router';
import { api } from '@/api/client';
import { ROLES, type DevTokenRequest, type DevTokenResponse, type Role } from '@/api/types';
import { useAuth } from '@/auth/useAuth';
import { Alert, ErrorAlert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Checkbox, Field, Input } from '@/components/ui/Field';

const ROLE_HELP: Record<Role, string> = {
  admin: 'Everything, including approvals and source-system keys',
  analyst: 'Work cases, propose resolutions, run reconciliations',
  approver: 'Approve or reject proposed resolutions',
  viewer: 'Read-only access',
};

export default function LoginPage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from ?? '/';

  const [tenantSlug, setTenantSlug] = useState('northgate');
  const [userId, setUserId] = useState('ops.analyst');
  const [roles, setRoles] = useState<Role[]>(['analyst']);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<unknown>(null);

  if (auth.isAuthenticated) return <Navigate to="/" replace />;

  const toggleRole = (role: Role) => setRoles((prev) => (prev.includes(role) ? prev.filter((r) => r !== role) : [...prev, role]));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      const body: DevTokenRequest = { tenantSlug: tenantSlug.trim(), userId: userId.trim(), roles };
      const response = await api.post<DevTokenResponse>('/auth/dev-token', body, { anonymous: true });
      auth.login(response);
      void navigate(from, { replace: true });
    } catch (err) {
      setError(err);
    } finally {
      setSubmitting(false);
    }
  };

  const valid = tenantSlug.trim().length > 0 && userId.trim().length > 0 && roles.length > 0;

  return (
    <div className="min-h-dvh grid place-items-center bg-bg p-6">
      <div className="w-full max-w-md">
        <div className="flex items-center gap-3 mb-6">
          <span className="grid size-10 place-items-center rounded-lg bg-accent text-on-accent font-bold" aria-hidden="true">DS</span>
          <div>
            <h1 className="text-lg font-semibold text-ink leading-6">DistrictSync</h1>
            <p className="text-xs text-muted">Operations console</p>
          </div>
        </div>

        <form onSubmit={submit} className="card p-5 space-y-4" aria-describedby="dev-login-note">
          <Alert tone="info" title="Development sign-in">
            <p id="dev-login-note">
              This issues a short-lived token from <code className="mono">POST /auth/dev-token</code> for the roles you pick. It exists for local development and demos; production deployments authenticate against the tenant's identity provider.
            </p>
          </Alert>

          <Field label="Tenant slug" required hint="The demo tenant is northgate.">
            {(p) => <Input {...p} value={tenantSlug} onChange={(e) => setTenantSlug(e.target.value)} autoComplete="organization" mono required data-autofocus />}
          </Field>
          <Field label="User id" required hint="Shown as the actor on cases and batches you touch.">
            {(p) => <Input {...p} value={userId} onChange={(e) => setUserId(e.target.value)} autoComplete="username" required />}
          </Field>

          <fieldset className="space-y-2">
            <legend className="text-xs font-medium text-ink-2 mb-1">Roles <span className="text-crit" aria-hidden="true">*</span></legend>
            {ROLES.map((role) => (
              <div key={role} className="flex items-start gap-2">
                <Checkbox label={<span className="font-medium">{role}</span>} checked={roles.includes(role)} onChange={() => toggleRole(role)} />
                <span className="text-xs text-muted pt-0.5">{ROLE_HELP[role]}</span>
              </div>
            ))}
            {roles.length === 0 && <p className="text-[11px] text-crit-ink" role="alert">Pick at least one role.</p>}
          </fieldset>

          <ErrorAlert error={error} title="Sign-in failed" />

          <Button type="submit" variant="primary" className="w-full" loading={submitting} disabled={!valid}>
            Sign in
          </Button>
        </form>
      </div>
    </div>
  );
}
