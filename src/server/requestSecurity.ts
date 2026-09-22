type HeaderValue = string | string[] | undefined;

export function isLoopbackAddress(address: string | undefined): boolean {
  if (!address) return false;

  return address === '::1'
    || address.startsWith('127.')
    || address.startsWith('::ffff:127.');
}

function hasHeader(value: HeaderValue): boolean {
  if (Array.isArray(value)) return value.length > 0;
  return typeof value === 'string' && value.trim().length > 0;
}

export function isDirectLoopbackRequest(
  remoteAddress: string | undefined,
  cloudflareIp: HeaderValue,
  forwardedFor: HeaderValue,
  origin: HeaderValue,
  host: HeaderValue,
): boolean {
  return isLoopbackAddress(remoteAddress)
    && !hasHeader(cloudflareIp)
    && !hasHeader(forwardedFor)
    && isSameOriginBrowserRequest(origin, host);
}

export function isCloudflareTunnelRequest(
  remoteAddress: string | undefined,
  cloudflareIp: HeaderValue,
  origin: HeaderValue,
  host: HeaderValue,
): boolean {
  return isLoopbackAddress(remoteAddress)
    && hasHeader(cloudflareIp)
    && isTrustedHttpsOrigin(origin, host);
}

export function getSingleHeader(value: HeaderValue): string | null {
  if (typeof value !== 'string') return null;

  const normalized = value.trim();
  return normalized || null;
}

function isTrustedHttpsOrigin(
  origin: HeaderValue,
  host: HeaderValue,
): boolean {
  if (!hasHeader(origin)) return true;
  if (typeof origin !== 'string' || typeof host !== 'string') return false;

  try {
    const parsed = new URL(origin);

    return parsed.protocol === 'https:'
      && parsed.host.toLowerCase() === host.toLowerCase();
  } catch {
    return false;
  }
}

function isSameOriginBrowserRequest(
  origin: HeaderValue,
  host: HeaderValue,
): boolean {
  if (!hasHeader(origin)) return true;
  if (typeof origin !== 'string' || typeof host !== 'string') return false;

  try {
    const parsed = new URL(origin);
    const hostname = parsed.hostname
      .replace(/^\[|\]$/g, '')
      .toLowerCase();

    return parsed.protocol === 'http:'
      && parsed.host.toLowerCase() === host.toLowerCase()
      && (hostname === 'localhost' || isLoopbackAddress(hostname));
  } catch {
    return false;
  }
}
