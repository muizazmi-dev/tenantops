import { cookies } from 'next/headers';

/**
 * Thin server-side fetch wrapper that:
 *   * targets the APIM base URL in Azure, or the direct service URL locally
 *   * attaches the user's session bearer token
 *   * DOES NOT set x-tenant-id — in production APIM injects it after validating
 *     the JWT. In local dev the backend reads the tenant from the JWT claim.
 *
 * Security note: if we set x-tenant-id from the browser cookie we would be
 * trusting the client. The backend middleware enforces claim/header agreement
 * anyway, but we keep the client silent on this field to keep the server the
 * source of truth.
 */
export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const base = process.env.NEXT_PUBLIC_APIM_BASE_URL
    ?? process.env.CORE_API_BASE_URL
    ?? 'http://core-api:8080';

  const token = cookies().get('tenantops_token')?.value;

  const headers = new Headers(init.headers);
  if (token) headers.set('Authorization', `Bearer ${token}`);
  if (!headers.has('content-type') && init.body) headers.set('content-type', 'application/json');

  return fetch(`${base}${path}`, { ...init, headers, cache: 'no-store' });
}

/** Service-specific helpers: in local dev we call the service directly. */
export function serviceUrl(svc: 'tenant' | 'core' | 'ai' | 'identity'): string {
  const map: Record<string, string | undefined> = {
    tenant: process.env.TENANT_API_BASE_URL,
    core: process.env.CORE_API_BASE_URL,
    ai: process.env.AI_API_BASE_URL,
    identity: process.env.IDENTITY_API_BASE_URL
  };
  const url = map[svc];
  if (!url) throw new Error(`${svc.toUpperCase()}_API_BASE_URL not set`);
  return url;
}
