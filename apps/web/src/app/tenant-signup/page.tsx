import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { serviceUrl } from '@/lib/api';

async function createTenant(formData: FormData) {
  'use server';

  const orgName       = String(formData.get('orgName') ?? '').trim();
  const slug          = String(formData.get('slug') ?? '').trim().toLowerCase();
  const adminEmail    = String(formData.get('adminEmail') ?? '').trim();
  const adminPassword = String(formData.get('adminPassword') ?? '');
  const adminDisplayName = String(formData.get('adminDisplayName') ?? '').trim();

  // Register the tenant + admin user
  const r = await fetch(`${serviceUrl('identity')}/auth/register-tenant`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ orgName, slug, adminEmail, adminPassword,
                           adminDisplayName: adminDisplayName || null })
  });

  if (r.status === 409) redirect('/tenant-signup?err=slug');
  if (r.status === 422) redirect('/tenant-signup?err=email');
  if (!r.ok)           redirect('/tenant-signup?err=1');

  const { access_token, slug: confirmedSlug } = await r.json();

  // Optional PNG logo upload
  const logoFile = formData.get('logo') as File | null;
  if (logoFile && logoFile.size > 0) {
    if (logoFile.size > 256 * 1024) redirect('/tenant-signup?err=logo-size');

    const buffer = await logoFile.arrayBuffer();
    const logoBase64 = Buffer.from(buffer).toString('base64');

    await fetch(`${serviceUrl('tenant')}/me/tenant/logo`, {
      method: 'POST',
      headers: {
        authorization: `Bearer ${access_token}`,
        'content-type': 'application/json'
      },
      body: JSON.stringify({ logoBase64, mime: 'image/png' })
    });
  }

  cookies().set('tenantops_token', access_token, {
    httpOnly: true, sameSite: 'lax',
    secure: process.env.NODE_ENV === 'production',
    path: '/', maxAge: 3600
  });
  cookies().set('tenant_slug', confirmedSlug, {
    httpOnly: false, sameSite: 'lax', path: '/', maxAge: 86400
  });

  redirect(`/t/${confirmedSlug}`);
}

const errMessages: Record<string, string> = {
  slug:        'That domain is already taken. Choose a different one.',
  email:       'That email is already registered. Try signing in instead.',
  'logo-size': 'Logo must be under 256 KB.',
  '1':         'Something went wrong. Check your details and try again.'
};

export default function TenantSignupPage({
  searchParams
}: {
  searchParams: { err?: string }
}) {
  const errMsg = searchParams.err ? errMessages[searchParams.err] ?? errMessages['1'] : null;

  return (
    <div className="card" style={{ maxWidth: 480, margin: '48px auto' }}>
      <h1>Create your organisation</h1>
      <p style={{ color: '#555', marginTop: 0 }}>
        Set up a new tenant workspace. You&apos;ll be signed in as the admin immediately.
      </p>

      {errMsg && <p style={{ color: '#c00', background: '#fff0f0', padding: '8px 12px', borderRadius: 6 }}>{errMsg}</p>}

      <form action={createTenant}>
        <label style={labelStyle}>Organisation name</label>
        <input name="orgName" placeholder="Acme Corp" required style={{ marginBottom: 12 }} />

        <label style={labelStyle}>
          Domain <span style={{ fontWeight: 400, color: '#888' }}>(unique URL identifier, e.g. acme → /t/acme)</span>
        </label>
        <input name="slug" placeholder="acme" pattern="[a-z0-9][a-z0-9-]{1,62}[a-z0-9]"
               title="3–64 lowercase letters, digits or hyphens; cannot start or end with a hyphen"
               required style={{ marginBottom: 12 }} />

        <hr style={{ margin: '16px 0', border: 'none', borderTop: '1px solid #e2e2e2' }} />
        <p style={{ margin: '0 0 12px', fontWeight: 600, fontSize: 14 }}>Admin account</p>

        <label style={labelStyle}>Display name <span style={{ fontWeight: 400, color: '#888' }}>(optional)</span></label>
        <input name="adminDisplayName" placeholder="Jane Smith" style={{ marginBottom: 12 }} />

        <label style={labelStyle}>Email</label>
        <input name="adminEmail" type="email" placeholder="admin@acme.com" required style={{ marginBottom: 12 }} />

        <label style={labelStyle}>Password</label>
        <input name="adminPassword" type="password" placeholder="Strong password" required style={{ marginBottom: 12 }} />

        <hr style={{ margin: '16px 0', border: 'none', borderTop: '1px solid #e2e2e2' }} />
        <label style={labelStyle}>
          Logo <span style={{ fontWeight: 400, color: '#888' }}>(PNG only, max 256 KB — optional, can upload later)</span>
        </label>
        <input name="logo" type="file" accept="image/png,.png" style={{ marginBottom: 16 }} />

        <button type="submit" style={{ width: '100%' }}>Create organisation</button>
      </form>

      <p style={{ marginTop: 16, fontSize: 13 }}>
        Already have an account? <a href="/login">Sign in</a>
      </p>
    </div>
  );
}

const labelStyle: React.CSSProperties = {
  display: 'block', fontSize: 13, fontWeight: 600, marginBottom: 4, color: '#333'
};
