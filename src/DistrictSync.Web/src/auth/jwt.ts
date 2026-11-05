export interface JwtClaims {
  sub?: string;
  exp?: number;
  [claim: string]: unknown;
}

export interface DecodedIdentity {
  userId: string;
  roles: string[];
  tenantId: string | null;
  tenantSlug: string | null;
  expiresAt: number | null;
  claims: JwtClaims;
}

const ROLE_CLAIMS = ['role', 'roles', 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];
const NAME_CLAIMS = ['sub', 'name', 'preferred_username', 'unique_name', 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name', 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'];
const TENANT_ID_CLAIMS = ['tenant_id', 'tenantId', 'tenant', 'tid'];
const TENANT_SLUG_CLAIMS = ['tenant_slug', 'tenantSlug', 'slug'];

function base64UrlDecode(input: string): string {
  const padded = input.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(input.length / 4) * 4, '=');
  const binary = atob(padded);
  const bytes = Uint8Array.from(binary, (c) => c.charCodeAt(0));
  return new TextDecoder().decode(bytes);
}

/** Decodes the JWT payload WITHOUT verifying the signature; display purposes only. The API is the authority. */
export function decodeJwt(token: string): DecodedIdentity | null {
  const parts = token.split('.');
  if (parts.length < 2) return null;
  let claims: JwtClaims;
  try {
    claims = JSON.parse(base64UrlDecode(parts[1])) as JwtClaims;
  } catch {
    return null;
  }

  const firstString = (keys: string[]): string | null => {
    for (const k of keys) {
      const v = claims[k];
      if (typeof v === 'string' && v) return v;
    }
    return null;
  };

  const roles = new Set<string>();
  for (const k of ROLE_CLAIMS) {
    const v = claims[k];
    if (typeof v === 'string') roles.add(v.toLowerCase());
    else if (Array.isArray(v)) v.forEach((r) => typeof r === 'string' && roles.add(r.toLowerCase()));
  }

  return {
    userId: firstString(NAME_CLAIMS) ?? 'unknown',
    roles: [...roles],
    tenantId: firstString(TENANT_ID_CLAIMS),
    tenantSlug: firstString(TENANT_SLUG_CLAIMS),
    expiresAt: typeof claims.exp === 'number' ? claims.exp * 1000 : null,
    claims,
  };
}
