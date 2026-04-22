import { cookies } from 'next/headers';
import { NextRequest, NextResponse } from 'next/server';
import { serviceUrl } from '@/lib/api';

/**
 * BFF proxy for the chat endpoint. Exists purely so the browser never sees
 * the backend bearer token — we keep it in an httpOnly cookie and inject
 * Authorization server-side.
 */
export async function POST(req: NextRequest) {
  const token = cookies().get('tenantops_token')?.value;
  if (!token) return NextResponse.json({ error: 'unauthenticated' }, { status: 401 });

  const body = await req.json();
  const upstream = await fetch(`${serviceUrl('ai')}/chat`, {
    method: 'POST',
    headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json' },
    body: JSON.stringify(body)
  });
  const text = await upstream.text();
  return new NextResponse(text, {
    status: upstream.status,
    headers: { 'content-type': upstream.headers.get('content-type') ?? 'application/json' }
  });
}
