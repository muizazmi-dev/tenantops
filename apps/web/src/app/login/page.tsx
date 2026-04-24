import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { serviceUrl } from '@/lib/api';
import { resolveTenant } from '@/lib/tenant';
import { getCurrentTenant } from '@/lib/currentTenant';

async function login(formData: FormData) {
  'use server';
  const workspace = String(formData.get('workspace') ?? '').trim().toLowerCase();
  const email     = String(formData.get('email') ?? '').trim();
  const password  = String(formData.get('password') ?? '');

  if (!workspace) redirect('/login?err=no-workspace');

  // Resolve tenantId from the slug the user typed
  const resolveRes = await fetch(
    `${serviceUrl('tenant')}/resolve?slug=${encodeURIComponent(workspace)}`,
    { cache: 'no-store' }
  );
  if (!resolveRes.ok) redirect(`/login?err=no-workspace&workspace=${workspace}`);
  const { tenantId } = await resolveRes.json();

  const r = await fetch(`${serviceUrl('identity')}/auth/login`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ tenantId, email, password })
  });
  if (!r.ok) redirect(`/login?err=1&workspace=${workspace}`);

  const { access_token, slug } = await r.json();
  cookies().set('tenantops_token', access_token, {
    httpOnly: true, sameSite: 'lax',
    secure: process.env.NODE_ENV === 'production',
    path: '/', maxAge: 3600
  });
  cookies().set('tenant_slug', slug, {
    httpOnly: false, sameSite: 'lax', path: '/', maxAge: 86400
  });

  redirect(slug ? `/t/${slug}` : '/');
}

export default async function LoginPage({
  searchParams,
}: {
  searchParams: { err?: string; registered?: string; tenant?: string; workspace?: string };
}) {
  // Pre-fill workspace from URL param, then cookie/middleware fallback
  const prefillSlug =
    searchParams.workspace ??
    searchParams.tenant ??
    null;

  const tenant = prefillSlug
    ? await resolveTenant({ slug: prefillSlug })
    : await getCurrentTenant();

  const errMsg =
    searchParams.err === 'no-workspace' ? 'Enter your workspace domain first.' :
    searchParams.err === '1'            ? 'Invalid credentials.' :
    null;

  return (
    <div style={{ minHeight: '75vh', display: 'flex', alignItems: 'center',
                  justifyContent: 'center', padding: '40px 24px' }}>
      <div className="pixel-box" style={{ maxWidth: 420, width: '100%' }}>
        <h1 style={{ fontSize: '1.3rem', marginBottom: 4 }}>SIGN IN</h1>

        {searchParams.registered && (
          <p style={{ color: 'var(--green)', fontSize: 12, marginBottom: 12,
                      border: '1px solid var(--green)', padding: '8px 12px' }}>
            ✔ ACCOUNT CREATED — SIGN IN BELOW
          </p>
        )}
        {errMsg && (
          <p style={{ color: 'var(--purple)', fontSize: 12, marginBottom: 12,
                      border: '1px solid var(--purple)', padding: '8px 12px' }}>
            !! {errMsg}
          </p>
        )}

        <form action={login}>
          <label>Workspace</label>
          <input name="workspace" placeholder="your-org-domain"
                 defaultValue={tenant?.slug ?? prefillSlug ?? ''}
                 required style={{ marginBottom: 12 }} />

          <label>Email</label>
          <input name="email" type="email" placeholder="you@example.com"
                 required style={{ marginBottom: 12 }} />

          <label>Password</label>
          <input name="password" type="password" placeholder="••••••••"
                 required style={{ marginBottom: 20 }} />

          <button type="submit" style={{ width: '100%' }}>
            [ SIGN IN ]
          </button>
        </form>

        <p style={{ marginTop: 16, fontSize: 11, color: 'var(--muted)', textAlign: 'center' }}>
          No account? <a href="/signup">Add user to this tenant</a>
          &nbsp;·&nbsp;
          <a href="/tenant-signup" style={{ color: 'var(--purple)' }}>Create new org</a>
        </p>
      </div>
    </div>
  );
}
