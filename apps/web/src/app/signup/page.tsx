import { redirect } from 'next/navigation';
import { serviceUrl } from '@/lib/api';
import { getCurrentTenant } from '@/lib/currentTenant';

async function register(formData: FormData) {
  'use server';
  const email = String(formData.get('email') ?? '').trim();
  const password = String(formData.get('password') ?? '');
  const displayName = String(formData.get('displayName') ?? '').trim();
  const tenantId = String(formData.get('tenantId') ?? '');

  const r = await fetch(`${serviceUrl('identity')}/auth/register`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ tenantId, email, password, displayName: displayName || null })
  });
  if (!r.ok) redirect('/signup?err=1');
  redirect('/login?registered=1');
}

export default async function SignupPage({ searchParams }: { searchParams: { err?: string } }) {
  const tenant = await getCurrentTenant();
  return (
    <div className="card" style={{ maxWidth: 420, margin: '48px auto' }}>
      <h1>Create account</h1>
      {searchParams.err && (
        <p style={{ color: '#c00' }}>Registration failed. That email may already be registered.</p>
      )}
      {!tenant && (
        <p style={{ color: '#c00' }}>
          Please <a href="/">select a tenant</a> first before registering.
        </p>
      )}
      {tenant && <p>Registering with: <strong>{tenant.name}</strong></p>}
      <form action={register}>
        <input name="tenantId" type="hidden" defaultValue={tenant?.tenantId ?? ''} />
        <input name="displayName" placeholder="Display name (optional)" style={{ marginBottom: 8 }} />
        <input name="email" type="email" placeholder="Email" required style={{ marginBottom: 8 }} />
        <input name="password" type="password" placeholder="Password" required />
        <div style={{ marginTop: 12 }}>
          <button type="submit" disabled={!tenant}>Create account</button>
        </div>
      </form>
      <p style={{ marginTop: 16, fontSize: 13 }}>
        Already have an account? <a href="/login">Sign in</a>
      </p>
    </div>
  );
}
