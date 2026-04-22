import { cache } from 'react';

export type Tenant = {
  tenantId: string;
  slug: string;
  domain?: string | null;
  name: string;
  theme: { primary: string; accent: string; logo: string };
};

function parseTheme(json: string): Tenant['theme'] {
  try {
    const t = JSON.parse(json) ?? {};
    return {
      primary: t.primary ?? '#0b5cad',
      accent: t.accent ?? '#f08a24',
      logo: t.logo ?? '/logos/default.svg'
    };
  } catch {
    return { primary: '#0b5cad', accent: '#f08a24', logo: '/logos/default.svg' };
  }
}

/**
 * Resolve the tenant for a request.
 *
 * Precedence (per spec §2):
 *   1. Custom domain: Host header → Tenants.Domain
 *   2. /t/{slug} route param (local dev fallback)
 *
 * Uses React's `cache()` so a single render pass hits the tenant-api once.
 */
export const resolveTenant = cache(async (opts: { host?: string | null; slug?: string | null }): Promise<Tenant | null> => {
  const base = process.env.TENANT_API_BASE_URL;
  if (!base) throw new Error('TENANT_API_BASE_URL not configured');

  const qs = new URLSearchParams();
  if (opts.host) qs.set('host', opts.host);
  if (opts.slug) qs.set('slug', opts.slug);
  if ([...qs].length === 0) return null;

  const res = await fetch(`${base}/resolve?${qs.toString()}`, {
    next: { revalidate: 60, tags: ['tenant'] }
  });
  if (res.status === 404) return null;
  if (!res.ok) throw new Error(`tenant-api ${res.status}`);
  const data = await res.json();
  return {
    tenantId: data.tenantId,
    slug: data.slug,
    domain: data.domain,
    name: data.name,
    theme: parseTheme(data.themeJson ?? '{}')
  };
});
