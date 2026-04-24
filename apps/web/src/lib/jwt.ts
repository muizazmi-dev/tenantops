export type JwtPayload = {
  tid_app?: string;
  tenant_slug?: string;
  email?: string;
  name?: string;
  role?: string | string[];
};

export function decodeJwt(token: string): JwtPayload | null {
  try {
    const part = token.split('.')[1];
    if (!part) return null;
    const json = Buffer.from(part, 'base64url').toString('utf-8');
    return JSON.parse(json) as JwtPayload;
  } catch {
    return null;
  }
}

export function isPlatformAdmin(payload: JwtPayload | null): boolean {
  if (!payload) return false;
  const role = payload.role;
  if (Array.isArray(role)) return role.includes('platform-admin');
  return role === 'platform-admin';
}
