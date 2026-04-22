import { headers, cookies } from 'next/headers';
import { resolveTenant, type Tenant } from './tenant';

/**
 * Server-side helper called from layouts and pages to load the current tenant.
 * Reads the resolved slug/host that middleware attached to the request, then
 * calls tenant-api through resolveTenant() (which is React-cached per render).
 */
export async function getCurrentTenant(): Promise<Tenant | null> {
  const h = headers();
  const host = h.get('x-resolved-tenant-host');
  let slug = h.get('x-resolved-tenant-slug') ?? cookies().get('tenant_slug')?.value ?? null;
  return resolveTenant({ host, slug });
}
