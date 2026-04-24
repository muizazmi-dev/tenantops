import { cookies } from 'next/headers';
import { revalidateTag } from 'next/cache';
import { notFound, redirect } from 'next/navigation';
import { getCurrentTenant } from '@/lib/currentTenant';
import { serviceUrl } from '@/lib/api';

async function uploadLogo(formData: FormData) {
  'use server';
  const token = cookies().get('tenantops_token')?.value;
  if (!token) redirect('/login');

  const file = formData.get('logo') as File | null;
  if (!file || file.size === 0) return;
  if (file.size > 256 * 1024) redirect('?err=logo-size');

  const buffer = await file.arrayBuffer();
  const logoBase64 = Buffer.from(buffer).toString('base64');

  const r = await fetch(`${serviceUrl('tenant')}/me/tenant/logo`, {
    method: 'POST',
    headers: {
      authorization: `Bearer ${token}`,
      'content-type': 'application/json'
    },
    body: JSON.stringify({ logoBase64, mime: 'image/png' })
  });

  if (!r.ok) redirect('?err=upload');

  // Invalidate Next.js tenant cache so the new logo loads immediately
  revalidateTag('tenant');
}

export default async function SettingsPage({
  params,
  searchParams
}: {
  params: { slug: string };
  searchParams: { err?: string };
}) {
  const tenant = await getCurrentTenant();
  if (!tenant) notFound();

  const isSignedIn = !!cookies().get('tenantops_token')?.value;
  if (!isSignedIn) redirect('/login');

  const errMsg =
    searchParams.err === 'logo-size' ? 'Logo must be under 256 KB.' :
    searchParams.err === 'upload'    ? 'Upload failed. Ensure the file is a valid PNG.' :
    null;

  const hasLogo = tenant.theme.logo && !tenant.theme.logo.startsWith('/logos/');

  return (
    <div>
      <h1>Settings — {tenant.name}</h1>

      {/* Tenant identity card */}
      <div className="card">
        <h3 style={{ marginTop: 0 }}>Tenant identity</h3>
        <table style={{ borderCollapse: 'collapse', width: '100%' }}>
          <tbody>
            <tr style={rowStyle}>
              <td style={labelCell}>Name</td>
              <td><strong>{tenant.name}</strong></td>
            </tr>
            <tr style={rowStyle}>
              <td style={labelCell}>Domain</td>
              <td><code>{tenant.slug}</code></td>
            </tr>
            <tr style={rowStyle}>
              <td style={labelCell}>Tenant ID</td>
              <td>
                <code style={{ fontSize: 13, wordBreak: 'break-all' }}>{tenant.tenantId}</code>
                <span style={{ marginLeft: 8, fontSize: 12, color: '#888' }}>(unique across all tenants)</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      {/* Logo section */}
      <div className="card">
        <h3 style={{ marginTop: 0 }}>Organisation logo</h3>

        {hasLogo && (
          <div style={{ marginBottom: 16 }}>
            <p style={{ fontSize: 13, color: '#555', marginTop: 0 }}>Current logo:</p>
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src={tenant.theme.logo} alt={`${tenant.name} logo`}
                 style={{ maxHeight: 80, maxWidth: 240, objectFit: 'contain', border: '1px solid #e2e2e2', borderRadius: 6, padding: 8 }} />
          </div>
        )}

        {errMsg && (
          <p style={{ color: '#c00', background: '#fff0f0', padding: '8px 12px', borderRadius: 6 }}>{errMsg}</p>
        )}

        <form action={uploadLogo}>
          <label style={{ display: 'block', fontSize: 13, fontWeight: 600, marginBottom: 6 }}>
            Upload PNG logo <span style={{ fontWeight: 400, color: '#888' }}>(max 256 KB)</span>
          </label>
          <input name="logo" type="file" accept="image/png,.png" required style={{ marginBottom: 10 }} />
          <div><button type="submit">Upload logo</button></div>
        </form>
      </div>
    </div>
  );
}

const rowStyle: React.CSSProperties = { borderBottom: '1px solid #f0f0f0' };
const labelCell: React.CSSProperties = {
  padding: '8px 16px 8px 0', fontSize: 13, color: '#666', width: 110, verticalAlign: 'top'
};
