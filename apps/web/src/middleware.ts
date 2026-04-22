import { NextResponse, type NextRequest } from 'next/server';

/**
 * Edge middleware: resolves tenant before any page renders.
 *
 * Priority (per spec §2):
 *   1. Host header ≠ localhost/default → custom-domain tenant
 *   2. /t/{slug}/* → slug-based fallback (local dev)
 *
 * The resolved slug is written to:
 *   * Response cookie `tenant_slug` (read by server components)
 *   * Request header `x-resolved-tenant-slug` (read by RSC fetchers)
 *
 * We deliberately DO NOT resolve the full tenant here (no tenant-api call
 * from middleware) — middleware runs on every asset, and we'd create a hot
 * loop. Resolution happens once per page render in `resolveTenant()`.
 */
export async function middleware(req: NextRequest) {
  const url = req.nextUrl;
  const host = req.headers.get('host')?.split(':')[0] ?? '';

  // Skip static assets and internal routes
  if (
    url.pathname.startsWith('/_next') ||
    url.pathname.startsWith('/api/internal') ||
    url.pathname.match(/\.(png|jpg|jpeg|svg|ico|css|js|woff2?)$/)
  ) {
    return NextResponse.next();
  }

  let slug: string | null = null;

  // 1) Custom-domain path: skip for localhost-family
  const isLocalHost = host === 'localhost' || host === '127.0.0.1' || host.endsWith('.localtest.me');
  if (!isLocalHost) {
    // Actual custom-domain lookup happens inside resolveTenant during SSR.
    // Here we just mark the request so the layout knows to use the host.
    const res = NextResponse.next();
    res.headers.set('x-resolved-tenant-host', host);
    return res;
  }

  // 2) /t/{slug} route
  const m = url.pathname.match(/^\/t\/([a-z0-9-]+)(\/|$)/);
  if (m) slug = m[1];

  // 3) *.localtest.me subdomain as secondary local-dev path
  if (!slug && host.endsWith('.localtest.me')) {
    slug = host.split('.')[0];
  }

  const res = NextResponse.next();
  if (slug) {
    res.headers.set('x-resolved-tenant-slug', slug);
    res.cookies.set('tenant_slug', slug, { path: '/', httpOnly: false, sameSite: 'lax' });
  }
  return res;
}

export const config = { matcher: ['/((?!_next/static|_next/image|favicon.ico).*)'] };
