import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { serviceUrl } from '@/lib/api';

async function adminLogin(formData: FormData) {
  'use server';
  const email    = String(formData.get('email') ?? '').trim();
  const password = String(formData.get('password') ?? '');

  const r = await fetch(`${serviceUrl('identity')}/auth/admin-login`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email, password })
  });

  if (!r.ok) redirect('/admin/login?err=1');

  const { access_token } = await r.json();
  cookies().set('tenantops_token', access_token, {
    httpOnly: true, sameSite: 'lax',
    secure: process.env.NODE_ENV === 'production',
    path: '/', maxAge: 3600
  });
  redirect('/admin');
}

export default function AdminLoginPage({
  searchParams,
}: {
  searchParams: { err?: string };
}) {
  return (
    <div style={{ minHeight: '75vh', display: 'flex', alignItems: 'center',
                  justifyContent: 'center', padding: '40px 24px' }}>
      <div className="pixel-box" style={{ maxWidth: 400, width: '100%' }}>
        <p style={{ fontSize: 10, color: 'var(--purple)', letterSpacing: 3,
                    textTransform: 'uppercase', marginBottom: 8 }}>
          RESTRICTED ACCESS
        </p>
        <h1 style={{ fontSize: '1.3rem', marginBottom: 4 }}>PLATFORM ADMIN</h1>
        <p style={{ fontSize: 12, color: 'var(--muted)', marginBottom: 24 }}>
          Authorised personnel only.<span className="cursor"> █</span>
        </p>

        {searchParams.err && (
          <p style={{ color: 'var(--purple)', fontSize: 12, marginBottom: 16,
                      border: '1px solid var(--purple)', padding: '8px 12px' }}>
            !! INVALID CREDENTIALS — ACCESS DENIED
          </p>
        )}

        <form action={adminLogin}>
          <label>Email</label>
          <input name="email" type="email" placeholder="admin@tenantops.dev"
                 required style={{ marginBottom: 12 }} />
          <label>Password</label>
          <input name="password" type="password" placeholder="••••••••"
                 required style={{ marginBottom: 20 }} />
          <button type="submit" style={{ width: '100%' }}>[ AUTHENTICATE ]</button>
        </form>

        <p style={{ marginTop: 16, fontSize: 11, color: 'var(--muted)', textAlign: 'center' }}>
          <a href="/" style={{ color: 'var(--muted)' }}>← Back to home</a>
        </p>
      </div>
    </div>
  );
}
