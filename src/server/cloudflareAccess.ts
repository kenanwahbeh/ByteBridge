import type { JWTVerifyGetKey } from 'jose';
import type { OAuthConfig } from '../types';

type KeyResolverFactory = (
  url: URL,
) => Promise<JWTVerifyGetKey>;

const defaultResolverFactory: KeyResolverFactory = async (url) => {
  const { createRemoteJWKSet } = await import('jose');

  return createRemoteJWKSet(url, {
    timeoutDuration: 5_000,
    cooldownDuration: 60_000,
    cacheMaxAge: 60 * 60 * 1_000,
  });
};

export class CloudflareAccessVerifier {
  private resolverKey = '';

  private resolver: Promise<JWTVerifyGetKey> | undefined;

  constructor(
    private readonly resolverFactory: KeyResolverFactory =
      defaultResolverFactory,
  ) {}

  async verify(token: string, config: OAuthConfig): Promise<boolean> {
    const teamDomain = normalizeTeamDomain(config.teamDomain);
    const audience = config.audience.trim();

    if (!config.enabled
      || !teamDomain
      || !audience
      || audience.length > 2_048
      || !token
      || token.length > 32_768) {
      return false;
    }

    try {
      const resolver = await this.getResolver(teamDomain);
      const { jwtVerify } = await import('jose');

      await jwtVerify(token, resolver, {
        issuer: `https://${teamDomain}`,
        audience,
        algorithms: ['RS256'],
        clockTolerance: 120,
      });

      return true;
    } catch {
      return false;
    }
  }

  private getResolver(teamDomain: string): Promise<JWTVerifyGetKey> {
    if (!this.resolver || this.resolverKey !== teamDomain) {
      this.resolverKey = teamDomain;
      const resolver = this.resolverFactory(
        new URL(`https://${teamDomain}/cdn-cgi/access/certs`),
      );
      this.resolver = resolver;

      void resolver.catch(() => {
        if (this.resolver === resolver
          && this.resolverKey === teamDomain) {
          this.resolver = undefined;
          this.resolverKey = '';
        }
      });
    }

    return this.resolver;
  }
}

export function normalizeTeamDomain(value: string): string | null {
  const normalized = value.trim().toLowerCase();

  if (!/^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.cloudflareaccess\.com$/.test(normalized)) {
    return null;
  }

  return normalized;
}
