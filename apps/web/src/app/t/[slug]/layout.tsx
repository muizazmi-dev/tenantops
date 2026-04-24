import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { getCurrentTenant } from '@/lib/currentTenant';
import { decodeJwt, isPlatformAdmin } from '@/lib/jwt';

export default async function TenantLayout({
  children,
  params,
}: {
  children: React.ReactNode;
  params: { slug: string };
}) {
  const token = cookies().get('tenantops_token')?.value;

  // Not signed in → redirect to login, carrying the slug so the login page
  // knows which tenant to authenticate against.
  if (!token) {
    redirect(`/login?tenant=${params.slug}`);
  }

  const payload = decodeJwt(token);
  const tenant  = await getCurrentTenant();

  // Unknown tenant slug
  if (!tenant) {
    redirect('/');
  }

  // Platform admins can view any tenant page
  if (isPlatformAdmin(payload)) {
    return <>{children}</>;
  }

  // Signed-in user belongs to a different tenant → 403
  if (payload?.tid_app !== tenant.tenantId) {
    const userSlug = payload?.tenant_slug ?? null;
    const returnTo = userSlug ? `/t/${userSlug}` : '/';
    redirect(`/forbidden?returnTo=${encodeURIComponent(returnTo)}&tenant=${encodeURIComponent(tenant.name)}`);
  }

  return <>{children}</>;
}
