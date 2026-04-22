import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { serviceUrl } from '@/lib/api';
import { getCurrentTenant } from '@/lib/currentTenant';

async function login(formData: FormData) {
  'use server';
  const email = String(formData.get('email') ?? '');
  const password = String(formData.get('password') ?? '');
  const tenantId = String(formData.get('tenantId') ?? '');
  const r = await fetch(`${serviceUrl('identity')}/auth/login`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ tenantId, email, password })
  });
  if (!r.ok) redirect('/login?err=1');
  const { access_token } = await r.json();
  cookies().set('tenantops_token', access_token, {
    httpOnly: true, sameSite: 'lax', secure: process.env.NODE_ENV === 'production',
    path: '/', maxAge: 3600
  });
  const slug = cookies().get('tenant_slug')?.value;
  redirect(slug ? `/t/${slug}` : '/');
}

export default async function LoginPage({ searchParams }: { searchParams: { err?: string } }) {
  const tenant = await getCurrentTenant();
  return (
    <div className="card" style={{ maxWidth: 420, margin: '48px auto' }}>
      <h1>Sign in</h1>
      {searchParams.err && <p style={{ color: '#c00' }}>Invalid credentials.</p>}
      <p>Tenant: <strong>{tenant?.name ?? '(select a tenant first)'}</strong></p>
      <form action={login}>
        <input name="tenantId" type="hidden" defaultValue={tenant?.tenantId ?? ''} />
        <input name="email" placeholder="email" required />
        <input name="password" type="password" placeholder="password" required style={{ marginTop: 8 }} />
        <div style={{ marginTop: 12 }}>
          <button type="submit" disabled={!tenant}>Sign in</button>
        </div>
      </form>
      <p style={{ marginTop: 16, fontSize: 13 }}>
        Demo creds (after registering): <code>demo@contoso.test</code> / any password you registered with.
      </p>
    </div>
  );
}
