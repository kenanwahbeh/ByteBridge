import crypto from 'crypto';
import type { DatabaseConfig, GatewayConfig, OAuthConfig, QueryRequest, QueryResponse, ExecuteResponse, RequestLogItem, WebServerModeConfig } from '../types';

export class GatewayEngine {
  public config: GatewayConfig;
  public oauthConfig: OAuthConfig;
  public webServerMode: WebServerModeConfig;
  public serviceState: 'running' | 'stopped' | 'pending' = 'running';
  public connections: DatabaseConfig[] = [];
  public logs: RequestLogItem[] = [];
  public appSettings = {
    language: 'en' as 'en' | 'ar',
    autoStart: true,
  };

  // Mock table schemas and rows for each database
  private mockTables: Record<string, Record<string, { columns: string[]; rows: any[][] }>> = {
    'sales': {
      'customers': {
        columns: ['ID', 'NAME', 'EMAIL', 'BALANCE', 'COUNTRY', 'CREATED_AT'],
        rows: [
          [101, 'Acme Industrial Ltd', 'procurement@acme.corp', 14500.50, 'United States', '2025-01-15T09:30:00Z'],
          [102, 'Helios Solar Systems', 'contact@heliossolar.de', 8920.00, 'Germany', '2025-02-10T11:45:00Z'],
          [103, 'Al-Noor Trading Est', 'sales@alnoor-trade.sa', 26340.75, 'Saudi Arabia', '2025-02-28T14:20:00Z'],
          [104, 'Nordic Timber AB', 'info@nordictimber.se', 3400.00, 'Sweden', '2025-03-01T08:15:00Z'],
          [105, 'Pacific Horizon Marine', 'ops@pacifichorizon.jp', 18750.25, 'Japan', '2025-03-12T16:00:00Z'],
          [106, 'Atlas Logistics Group', 'admin@atlaslogistics.co.uk', 9400.00, 'United Kingdom', '2025-04-05T10:10:00Z'],
        ],
      },
      'orders': {
        columns: ['ID', 'CUSTOMER_ID', 'ORDER_NUM', 'TOTAL_AMOUNT', 'STATUS', 'ORDER_DATE'],
        rows: [
          [5001, 101, 'ORD-2026-001', 4500.00, 'COMPLETED', '2026-01-12T14:20:00Z'],
          [5002, 101, 'ORD-2026-002', 10000.50, 'PROCESSING', '2026-02-18T09:40:00Z'],
          [5003, 102, 'ORD-2026-003', 8920.00, 'SHIPPED', '2026-02-24T16:15:00Z'],
          [5004, 103, 'ORD-2026-004', 26340.75, 'PENDING', '2026-03-01T11:00:00Z'],
        ],
      },
    },
    'inventory': {
      'products': {
        columns: ['ID', 'SKU', 'NAME', 'STOCK_QTY', 'UNIT_PRICE', 'CATEGORY'],
        rows: [
          [201, 'SKU-FDB-01', 'Heavy Duty Hydraulic Valve', 45, 120.50, 'Hydraulics'],
          [202, 'SKU-FDB-02', 'High-Temp Sensor Probe', 128, 48.00, 'Sensors'],
          [203, 'SKU-FDB-03', 'Rotary Flange 2-Inch', 85, 34.20, 'Fittings'],
          [204, 'SKU-FDB-04', 'Microprocessor Relay Board', 210, 89.90, 'Electronics'],
        ],
      },
    },
    'accounting': {
      'invoices': {
        columns: ['ID', 'INVOICE_NUM', 'AMOUNT', 'PAID', 'DUE_DATE'],
        rows: [
          [901, 'INV-8801', 14500.50, 1, '2026-02-28'],
          [902, 'INV-8802', 8920.00, 0, '2026-03-31'],
          [903, 'INV-8803', 26340.75, 0, '2026-04-15'],
        ],
      },
    },
  };

  constructor() {
    // Generate an initial random 32-byte hex key matching C# SqliteDatabase.RegenerateApiKey()
    const initialKey = crypto.randomBytes(32).toString('hex');

    this.config = {
      host: '127.0.0.1',
      port: 3000,
      apiKey: initialKey,
      autoStart: true,
      maxRows: 1000,
      commandTimeoutSeconds: 30,
      baseUrl: 'http://127.0.0.1:3000',
    };

    this.oauthConfig = {
      enabled: false,
      teamDomain: '',
      audience: '',
    };

    // Dual Tunnel Web Server Mode (Cloudflare Tunnels: Port 8080 for Data, Port 3000 for Server Web UI/API)
    this.webServerMode = {
      enabled: false,
      status: 'offline',
      provider: 'cloudflare',
      accountName: 'bytebridge-cloud',
      remoteManagementAllowed: true,
      requireCloudflareAccess: false,
      startedAt: null,
      tunnels: {
        databaseGateway: {
          id: 'database',
          name: 'Database Gateway Tunnel (Data API)',
          nameAr: 'نفق بوابة البيانات (Data Gateway)',
          targetPort: 8080,
          protocol: 'http',
          subdomain: 'db-gateway-edge.bytebridge.io',
          publicUrl: 'https://db-gateway-edge.bytebridge.io',
          active: false,
          metrics: {
            requestsTotal: 0,
            bytesTransferred: '0 KB',
            latencyMs: 14,
          },
        },
        controlPanel: {
          id: 'control_panel',
          name: 'Web Server Control Panel Tunnel (Remote UI & Mgmt)',
          nameAr: 'نفق لوحة التحكم عن بعد (Server Web UI)',
          targetPort: 3000,
          protocol: 'http',
          subdomain: 'panel-remote.bytebridge.io',
          publicUrl: 'https://panel-remote.bytebridge.io',
          active: false,
          metrics: {
            requestsTotal: 0,
            bytesTransferred: '0 KB',
            latencyMs: 18,
          },
        },
      },
    };

    // Seed default connections
    this.connections = [
      {
        id: 'conn-sales-01',
        name: 'Sales',
        server: '127.0.0.1',
        port: 3050,
        username: 'SYSDBA',
        password: 'masterkey',
        database: 'C:\\data\\sales.fdb',
        enabled: true,
        lastTestSuccessful: true,
        lastTestedAt: new Date(Date.now() - 3600000).toISOString(),
      },
      {
        id: 'conn-inventory-02',
        name: 'Inventory',
        server: '127.0.0.1',
        port: 3050,
        username: 'SYSDBA',
        password: 'masterkey',
        database: 'C:\\data\\inventory.fdb',
        enabled: true,
        lastTestSuccessful: true,
        lastTestedAt: new Date(Date.now() - 7200000).toISOString(),
      },
      {
        id: 'conn-accounting-03',
        name: 'Accounting',
        server: '192.168.1.50',
        port: 3050,
        username: 'SYSDBA',
        password: 'masterkey',
        database: 'C:\\db\\accounting.fdb',
        enabled: false,
        lastTestSuccessful: false,
        lastTestedAt: new Date(Date.now() - 86400000).toISOString(),
      },
    ];

    // Seed initial request logs
    this.appendLog({
      method: 'GET',
      path: '/health',
      status: 200,
      elapsedMs: 2,
      localPeer: '127.0.0.1',
      clientIp: '127.0.0.1',
      authenticated: true,
    });

    this.appendLog({
      method: 'POST',
      path: '/query',
      status: 200,
      elapsedMs: 14,
      localPeer: '127.0.0.1',
      clientIp: '127.0.0.1',
      authenticated: true,
      database: 'Sales',
      sql: 'SELECT ID, NAME, BALANCE FROM CUSTOMERS WHERE ID = @id',
      rows: 1,
    });
  }

  public regenerateApiKey(): string {
    const newKey = crypto.randomBytes(32).toString('hex');
    this.config.apiKey = newKey;
    return newKey;
  }

  public isKeyValid(providedKey?: string | null): boolean {
    if (!providedKey) return false;
    const cleanKey = providedKey.trim();
    if (cleanKey.length !== this.config.apiKey.length) return false;
    return crypto.timingSafeEqual(
      Buffer.from(cleanKey, 'utf-8'),
      Buffer.from(this.config.apiKey, 'utf-8')
    );
  }

  public appendLog(logData: Partial<RequestLogItem>): RequestLogItem {
    const item: RequestLogItem = {
      id: crypto.randomUUID(),
      at: new Date().toISOString(),
      method: logData.method || 'GET',
      path: logData.path || '/',
      status: logData.status || 200,
      elapsedMs: logData.elapsedMs ?? 1,
      localPeer: logData.localPeer || '127.0.0.1',
      clientIp: logData.clientIp || '127.0.0.1',
      authenticated: !!logData.authenticated,
      database: logData.database,
      sql: logData.sql ? logData.sql.slice(0, 2000) : undefined,
      rows: logData.rows,
      rowsAffected: logData.rowsAffected,
      error: logData.error,
    };

    this.logs.unshift(item);
    if (this.logs.length > 200) {
      this.logs.pop();
    }
    return item;
  }

  /*
   * Guards /query against accidental writes.
   * Matches FirebirdExecutor.IsReadOnlyStatement in C#
   */
  public isReadOnlyStatement(sql: string): boolean {
    const statement = this.stripLeadingNoise(sql).trim();
    return this.startsWithKeyword(statement, 'SELECT') || this.startsWithKeyword(statement, 'WITH');
  }

  private stripLeadingNoise(sql: string): string {
    let index = 0;
    while (index < sql.Length || index < sql.length) {
      const char = sql[index];
      if (/\s/.test(char)) {
        index++;
        continue;
      }
      // Line comment --
      if (char === '-' && index + 1 < sql.length && sql[index + 1] === '-') {
        while (index < sql.length && sql[index] !== '\n') {
          index++;
        }
        continue;
      }
      // Block comment /* ... */
      if (char === '/' && index + 1 < sql.length && sql[index + 1] === '*') {
        const end = sql.indexOf('*/', index + 2);
        if (end < 0) return '';
        index = end + 2;
        continue;
      }
      break;
    }
    return sql.slice(index);
  }

  private startsWithKeyword(text: string, keyword: string): boolean {
    const upperText = text.toUpperCase();
    const upperKey = keyword.toUpperCase();
    if (!upperText.startsWith(upperKey)) return false;
    if (upperText.length === upperKey.length) return true;
    const next = upperText[upperKey.length];
    return !/[A-Z0-9_$]/.test(next);
  }

  public findConnection(dbIdentifier: string): DatabaseConfig | { error: string; status: number } {
    const lowerId = dbIdentifier.trim().toLowerCase();
    const matches = this.connections.filter(
      c => c.id.toLowerCase() === lowerId || c.name.toLowerCase() === lowerId
    );

    if (matches.length === 0) {
      return { error: `No connection matches '${dbIdentifier}'`, status: 404 };
    }
    if (matches.length > 1) {
      return {
        error: `Database '${dbIdentifier}' matches more than one connection. Specify its ID instead.`,
        status: 400,
      };
    }

    const conn = matches[0];
    if (!conn.enabled) {
      return {
        error: `Database connection '${conn.name}' is Offline. Turn it Online in the ByteBridge control panel.`,
        status: 409,
      };
    }

    return conn;
  }

  public executeQuery(
    conn: DatabaseConfig,
    rawSql: string,
    parameters: Record<string, any> = {},
    maxRows: number = 1000
  ): QueryResponse {
    const startTime = Date.now();
    const normDbKey = conn.name.toLowerCase();
    const dbTables = this.mockTables[normDbKey] || this.mockTables['sales'];

    // Identify target table name from SQL (e.g., FROM CUSTOMERS or FROM ORDERS)
    const fromMatch = rawSql.match(/FROM\s+([A-Za-z0-9_$"']+)/i);
    const tableName = fromMatch ? fromMatch[1].replace(/["']/g, '').toLowerCase() : Object.keys(dbTables)[0];

    const tableData = dbTables[tableName] || Object.values(dbTables)[0] || {
      columns: ['COLUMN_1', 'VALUE'],
      rows: [['RECORD_1', 'Sample row data from ' + conn.name]],
    };

    let selectedColumns = [...tableData.columns];
    let candidateRows = tableData.rows.map(r => [...r]);

    // Handle parameter filtering if @id or :id provided
    const idParam = parameters['id'] ?? parameters['@id'] ?? parameters['ID'] ?? parameters['@ID'];
    if (idParam !== undefined && selectedColumns.includes('ID')) {
      const idColIdx = selectedColumns.indexOf('ID');
      candidateRows = candidateRows.filter(r => String(r[idColIdx]) === String(idParam));
    }

    // Handle column selection e.g. SELECT ID, NAME FROM ...
    const selectMatch = rawSql.match(/SELECT\s+(.*?)\s+FROM/i);
    if (selectMatch && selectMatch[1].trim() !== '*') {
      const reqCols = selectMatch[1]
        .split(',')
        .map(c => c.trim().replace(/["']/g, '').toUpperCase())
        .filter(c => Boolean(c));

      const validIndices: number[] = [];
      const newCols: string[] = [];

      for (const rc of reqCols) {
        const idx = tableData.columns.indexOf(rc);
        if (idx !== -1) {
          validIndices.push(idx);
          newCols.push(rc);
        } else {
          // Computed or unrecognised column
          newCols.push(rc);
          validIndices.push(-1);
        }
      }

      if (validIndices.length > 0) {
        selectedColumns = newCols;
        candidateRows = candidateRows.map(r =>
          validIndices.map((idx, i) => (idx !== -1 ? r[idx] : `val_${i}`))
        );
      }
    }

    const rowCap = Math.min(maxRows || this.config.maxRows, this.config.maxRows);
    const truncated = candidateRows.length > rowCap;
    const finalRows = candidateRows.slice(0, rowCap);

    return {
      columns: selectedColumns,
      rows: finalRows,
      rowCount: finalRows.length,
      truncated,
      elapsedMs: Math.max(1, Date.now() - startTime),
    };
  }

  public executeWrite(
    conn: DatabaseConfig,
    rawSql: string,
    parameters: Record<string, any> = {}
  ): ExecuteResponse {
    const startTime = Date.now();
    const normDbKey = conn.name.toLowerCase();
    let dbTables = this.mockTables[normDbKey];
    if (!dbTables) {
      dbTables = this.mockTables['sales'];
    }

    const upperSql = rawSql.toUpperCase();
    let rowsAffected = 1;

    if (upperSql.includes('INSERT')) {
      const match = rawSql.match(/INSERT\s+INTO\s+([A-Za-z0-9_$"']+)/i);
      const tableName = match ? match[1].replace(/["']/g, '').toLowerCase() : 'customers';
      const tbl = dbTables[tableName];
      if (tbl) {
        const newId = (tbl.rows.length ? Math.max(...tbl.rows.map(r => (typeof r[0] === 'number' ? r[0] : 100))) : 100) + 1;
        const newRow = tbl.columns.map((col, idx) => {
          if (idx === 0) return newId;
          const val = parameters[col.toLowerCase()] || parameters['@' + col.toLowerCase()] || parameters[col] || parameters['@' + col];
          if (val !== undefined) return val;
          if (col.includes('DATE') || col.includes('AT')) return new Date().toISOString();
          return `New_${col}`;
        });
        tbl.rows.push(newRow);
      }
    } else if (upperSql.includes('UPDATE')) {
      rowsAffected = 1;
    } else if (upperSql.includes('DELETE')) {
      rowsAffected = 1;
    }

    return {
      rowsAffected,
      elapsedMs: Math.max(1, Date.now() - startTime),
    };
  }

  public toggleWebServerMode(enable?: boolean): WebServerModeConfig {
    const newState = enable !== undefined ? enable : !this.webServerMode.enabled;
    this.webServerMode.enabled = newState;
    this.webServerMode.status = newState ? 'online' : 'offline';
    this.webServerMode.startedAt = newState ? new Date().toISOString() : null;

    this.webServerMode.tunnels.databaseGateway.active = newState;
    this.webServerMode.tunnels.controlPanel.active = newState;

    if (newState) {
      const now = new Date().toISOString();
      this.webServerMode.tunnels.databaseGateway.connectedAt = now;
      this.webServerMode.tunnels.controlPanel.connectedAt = now;

      // Log event
      this.appendLog({
        method: 'SYSTEM',
        path: '/tunnel/dual-connect',
        status: 200,
        elapsedMs: 25,
        authenticated: true,
        sql: 'Cloudflare Dual Tunnel established: [Tunnel 1: :8080 Data API] & [Tunnel 2: :3000 Web Server Control Panel]',
      });
    } else {
      this.appendLog({
        method: 'SYSTEM',
        path: '/tunnel/disconnect',
        status: 200,
        elapsedMs: 5,
        authenticated: true,
        sql: 'Cloudflare Dual Tunnel disconnected. Web server switched to local loopback mode.',
      });
    }

    return this.webServerMode;
  }

  public incrementTunnelMetrics(tunnelType: 'database' | 'control_panel', bytes: number = 512): void {
    const tunnel = tunnelType === 'database' 
      ? this.webServerMode.tunnels.databaseGateway 
      : this.webServerMode.tunnels.controlPanel;
    
    tunnel.metrics.requestsTotal += 1;
    const currentKb = Math.round((tunnel.metrics.requestsTotal * bytes) / 1024);
    tunnel.metrics.bytesTransferred = currentKb > 1024 
      ? `${(currentKb / 1024).toFixed(2)} MB` 
      : `${currentKb} KB`;
  }
}

export const gateway = new GatewayEngine();
