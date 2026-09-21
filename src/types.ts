export interface DatabaseConfig {
  id: string;
  name: string;
  server: string;
  port: number;
  username: string;
  password?: string;
  database: string;
  enabled: boolean;
  lastTestSuccessful?: boolean;
  lastTestedAt?: string | null;
}

export interface DatabaseSummary {
  id: string;
  name: string;
  online: boolean;
}

export interface GatewayConfig {
  host: string;
  port: number;
  apiKey: string;
  autoStart: boolean;
  maxRows: number;
  commandTimeoutSeconds: number;
  baseUrl: string;
}

export interface OAuthConfig {
  enabled: boolean;
  teamDomain: string;
  audience: string;
}

export interface QueryRequest {
  database: string;
  sql: string;
  parameters?: Record<string, any>;
  maxRows?: number;
}

export interface QueryResponse {
  columns: string[];
  rows: (string | number | boolean | null)[][];
  rowCount: number;
  truncated: boolean;
  elapsedMs: number;
}

export interface ExecuteResponse {
  rowsAffected: number;
  elapsedMs: number;
}

export interface RequestLogItem {
  id: string;
  at: string;
  method: string;
  path: string;
  status: number;
  elapsedMs: number;
  localPeer: string;
  clientIp: string;
  authenticated: boolean;
  database?: string;
  sql?: string;
  rows?: number;
  rowsAffected?: number;
  error?: string;
}

export interface AppSettings {
  language: 'en' | 'ar';
  autoStart: boolean;
}

export type ServiceState = 'running' | 'stopped' | 'pending';

export interface TunnelEndpoint {
  id: 'database' | 'control_panel';
  name: string;
  nameAr: string;
  targetPort: number;
  protocol: 'http' | 'tcp' | 'https';
  subdomain: string;
  publicUrl: string;
  active: boolean;
  connectedAt?: string;
  metrics: {
    requestsTotal: number;
    bytesTransferred: string;
    latencyMs: number;
  };
}

export interface WebServerModeConfig {
  enabled: boolean;
  status: 'offline' | 'starting' | 'online';
  provider: 'cloudflare';
  accountName: string;
  remoteManagementAllowed: boolean;
  requireCloudflareAccess: boolean;
  tunnels: {
    databaseGateway: TunnelEndpoint;
    controlPanel: TunnelEndpoint;
  };
  startedAt?: string | null;
}
