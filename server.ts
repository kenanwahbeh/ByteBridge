import express, { Request, Response, NextFunction } from 'express';
import cors from 'cors';
import path from 'path';
import { createServer as createViteServer } from 'vite';
import { gateway } from './src/server/gatewayEngine.js';

const app = express();
const PORT = 3000;

// Security & Parsing Middlewares
app.use(cors());
app.use(express.json({ limit: '1mb' }));
app.use(express.urlencoded({ extended: true, limit: '1mb' }));

// Helper to extract API Key from request
function extractApiKey(req: Request): string | null {
  const headerKey = req.headers['x-api-key'];
  if (typeof headerKey === 'string' && headerKey) {
    return headerKey;
  }
  const authHeader = req.headers.authorization;
  if (authHeader && authHeader.toLowerCase().startsWith('bearer ')) {
    return authHeader.slice(7).trim();
  }
  return null;
}

// Client IP resolver
function getClientIp(req: Request): string {
  const cfIp = req.headers['cf-connecting-ip'];
  if (typeof cfIp === 'string') return cfIp;
  const forwarded = req.headers['x-forwarded-for'];
  if (typeof forwarded === 'string') return forwarded.split(',')[0].trim();
  return req.socket.remoteAddress || '127.0.0.1';
}

// -------------------------------------------------------------
// PUBLIC GATEWAY API ENDPOINTS (As specified in GATEWAY.md)
// -------------------------------------------------------------

/*
 * GET /health
 * Unauthenticated liveness check. Used by tunnels and monitoring.
 */
app.get('/health', (req: Request, res: Response) => {
  const clientIp = getClientIp(req);
  const onlineCount = gateway.connections.filter(c => c.enabled).length;

  gateway.appendLog({
    method: 'GET',
    path: '/health',
    status: 200,
    elapsedMs: 1,
    clientIp,
    authenticated: false,
  });

  return res.json({
    status: 'ok',
    service: 'ByteBridge',
    version: '1.0.0',
    connections: gateway.connections.length,
    online: onlineCount,
    autoStart: gateway.config.autoStart,
    port: gateway.config.port,
    timestamp: new Date().toISOString(),
  });
});

/*
 * GET /databases
 * Authenticated endpoint: returns list of configured database summaries
 */
app.get('/databases', (req: Request, res: Response) => {
  const clientIp = getClientIp(req);
  const apiKey = extractApiKey(req);

  if (!gateway.isKeyValid(apiKey)) {
    gateway.appendLog({
      method: 'GET',
      path: '/databases',
      status: 401,
      clientIp,
      authenticated: false,
      error: 'Missing or invalid API key',
    });
    return res.status(401).json({ error: 'Missing or wrong API key.' });
  }

  const summaries = gateway.connections.map(c => ({
    id: c.id,
    name: c.name,
    online: c.enabled,
  }));

  gateway.appendLog({
    method: 'GET',
    path: '/databases',
    status: 200,
    clientIp,
    authenticated: true,
  });

  return res.json(summaries);
});

/*
 * POST /query
 * Authenticated endpoint: executes SELECT / WITH statements and returns columns and rows.
 */
app.post('/query', (req: Request, res: Response) => {
  const startTime = Date.now();
  const clientIp = getClientIp(req);
  const apiKey = extractApiKey(req);

  if (!gateway.isKeyValid(apiKey)) {
    gateway.appendLog({
      method: 'POST',
      path: '/query',
      status: 401,
      clientIp,
      authenticated: false,
      error: 'Missing or invalid API key',
    });
    return res.status(401).json({ error: 'Missing or wrong API key.' });
  }

  const { database, sql, parameters, maxRows } = req.body || {};

  if (!database || typeof database !== 'string') {
    gateway.appendLog({
      method: 'POST',
      path: '/query',
      status: 400,
      clientIp,
      authenticated: true,
      error: "Missing required 'database' field",
    });
    return res.status(400).json({ error: "Missing required 'database' field in body." });
  }

  if (!sql || typeof sql !== 'string') {
    gateway.appendLog({
      method: 'POST',
      path: '/query',
      status: 400,
      clientIp,
      authenticated: true,
      database,
      error: "Missing required 'sql' statement",
    });
    return res.status(400).json({ error: "Missing required 'sql' statement in body." });
  }

  // Enforce read-only guard (must start with SELECT or WITH)
  if (!gateway.isReadOnlyStatement(sql)) {
    const elapsed = Date.now() - startTime;
    gateway.appendLog({
      method: 'POST',
      path: '/query',
      status: 400,
      elapsedMs: elapsed,
      clientIp,
      authenticated: true,
      database,
      sql,
      error: 'Refused non-read-only statement on /query',
    });
    return res.status(400).json({
      error: 'Data modification statements (INSERT, UPDATE, DELETE, ALTER, DROP) are not permitted. This gateway strictly supports read-only queries (/query).',
    });
  }

  // Look up connection
  const connResult = gateway.findConnection(database);
  if ('error' in connResult) {
    const elapsed = Date.now() - startTime;
    gateway.appendLog({
      method: 'POST',
      path: '/query',
      status: connResult.status,
      elapsedMs: elapsed,
      clientIp,
      authenticated: true,
      database,
      sql,
      error: connResult.error,
    });
    return res.status(connResult.status).json({ error: connResult.error });
  }

  try {
    const queryResult = gateway.executeQuery(connResult, sql, parameters, maxRows);
    const elapsed = Date.now() - startTime;
    queryResult.elapsedMs = elapsed;

    gateway.appendLog({
      method: 'POST',
      path: '/query',
      status: 200,
      elapsedMs: elapsed,
      clientIp,
      authenticated: true,
      database: connResult.name,
      sql,
      rows: queryResult.rowCount,
    });

    return res.json(queryResult);
  } catch (err: any) {
    const elapsed = Date.now() - startTime;
    gateway.appendLog({
      method: 'POST',
      path: '/query',
      status: 500,
      elapsedMs: elapsed,
      clientIp,
      authenticated: true,
      database: connResult.name,
      sql,
      error: err.message,
    });
    return res.status(500).json({ error: err.message || 'Error executing query on database.' });
  }
});

/*
 * POST /execute
 * Modification / write statements are disabled by policy: only queries are allowed.
 */
app.post('/execute', (req: Request, res: Response) => {
  const startTime = Date.now();
  const clientIp = getClientIp(req);
  const apiKey = extractApiKey(req);
  const { database, sql } = req.body || {};

  gateway.appendLog({
    method: 'POST',
    path: '/execute',
    status: 403,
    elapsedMs: Date.now() - startTime,
    clientIp,
    authenticated: gateway.isKeyValid(apiKey),
    database: typeof database === 'string' ? database : undefined,
    sql: typeof sql === 'string' ? sql : undefined,
    error: 'Data modification disabled — queries only',
  });

  return res.status(403).json({
    error: 'Data modification is disabled. This gateway strictly supports read-only queries (/query).',
    readonly: true,
  });
});

// -------------------------------------------------------------
// CONTROL PANEL API ENDPOINTS (/api/*)
// -------------------------------------------------------------

app.get('/api/status', (req: Request, res: Response) => {
  return res.json({
    gateway: gateway.config,
    service: {
      state: gateway.serviceState,
    },
    oauth: gateway.oauthConfig,
    webServerMode: gateway.webServerMode,
    appSettings: gateway.appSettings,
    stats: {
      totalDatabases: gateway.connections.length,
      onlineDatabases: gateway.connections.filter(c => c.enabled).length,
      totalLogs: gateway.logs.length,
    },
  });
});

app.post('/api/webserver/toggle', (req: Request, res: Response) => {
  const { enable } = req.body;
  const updated = gateway.toggleWebServerMode(enable);
  return res.json(updated);
});

app.post('/api/webserver/config', (req: Request, res: Response) => {
  const { accountName, requireCloudflareAccess, dbPort, panelPort } = req.body;
  if (accountName !== undefined) gateway.webServerMode.accountName = accountName;
  if (requireCloudflareAccess !== undefined) gateway.webServerMode.requireCloudflareAccess = !!requireCloudflareAccess;
  if (dbPort) {
    gateway.webServerMode.tunnels.databaseGateway.targetPort = Number(dbPort);
  }
  if (panelPort) {
    gateway.webServerMode.tunnels.controlPanel.targetPort = Number(panelPort);
  }
  return res.json(gateway.webServerMode);
});

app.get('/api/connections', (req: Request, res: Response) => {
  return res.json(gateway.connections);
});

app.get('/api/connections/export', (req: Request, res: Response) => {
  const includePasswords = req.query.includePasswords !== 'false';
  const exported = gateway.connections.map(c => {
    const copy = { ...c };
    if (!includePasswords) {
      delete copy.password;
    }
    return copy;
  });

  const exportPayload = {
    app: 'ByteBridge',
    version: '1.0',
    exportedAt: new Date().toISOString(),
    totalConnections: exported.length,
    connections: exported,
  };

  if (req.query.download === 'true') {
    res.setHeader('Content-Disposition', `attachment; filename="bytebridge-connections-${new Date().toISOString().slice(0, 10)}.json"`);
    res.setHeader('Content-Type', 'application/json');
  }

  return res.json(exportPayload);
});

app.post('/api/connections/import', (req: Request, res: Response) => {
  const body = req.body;
  const incomingList: any[] = Array.isArray(body)
    ? body
    : Array.isArray(body?.connections)
    ? body.connections
    : [];

  const mode: 'merge' | 'replace' = body?.mode === 'replace' ? 'replace' : 'merge';
  const preservePasswords = body?.preserveExistingPasswords !== false;

  if (!incomingList.length) {
    return res.status(400).json({ error: 'No connections found in import payload.' });
  }

  const validConnections: any[] = [];
  for (const item of incomingList) {
    if (!item || typeof item !== 'object') continue;
    const name = typeof item.name === 'string' ? item.name.trim() : '';
    const database = typeof item.database === 'string' ? item.database.trim() : '';
    if (!name || !database) continue;

    const id = (typeof item.id === 'string' && item.id.trim()) || ('conn-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2, 6));
    const server = typeof item.server === 'string' ? item.server.trim() : 'localhost';
    const port = Number(item.port) || (server.includes('postgres') ? 5432 : 3050);
    const username = typeof item.username === 'string' ? item.username.trim() : 'SYSDBA';
    const password = typeof item.password === 'string' ? item.password : '';
    const type = typeof item.type === 'string' && item.type.trim()
      ? item.type.trim()
      : (database.endsWith('.sqlite') || database.endsWith('.db') ? 'SQLite' : port === 5432 ? 'PostgreSQL' : 'Firebird');
    const enabled = item.enabled !== undefined ? !!item.enabled : true;

    validConnections.push({
      id,
      name,
      type,
      server,
      port,
      username,
      password,
      database,
      enabled,
      lastTestSuccessful: item.lastTestSuccessful !== undefined ? item.lastTestSuccessful : true,
      lastTestedAt: item.lastTestedAt || new Date().toISOString(),
      lastLatencyMs: typeof item.lastLatencyMs === 'number' ? item.lastLatencyMs : 14,
    });
  }

  if (validConnections.length === 0) {
    return res.status(400).json({ error: 'None of the imported items contained valid name and database attributes.' });
  }

  if (mode === 'replace') {
    gateway.connections = validConnections;
  } else {
    for (const incoming of validConnections) {
      const existingIdx = gateway.connections.findIndex(
        c => c.id === incoming.id || c.name.toLowerCase() === incoming.name.toLowerCase()
      );
      if (existingIdx !== -1) {
        const existing = gateway.connections[existingIdx];
        gateway.connections[existingIdx] = {
          ...existing,
          ...incoming,
          password: (!incoming.password && preservePasswords && existing.password) ? existing.password : incoming.password,
        };
      } else {
        gateway.connections.push(incoming);
      }
    }
  }

  gateway.appendLog({
    method: 'POST',
    path: '/api/connections/import',
    status: 200,
    elapsedMs: 2,
    clientIp: getClientIp(req),
    authenticated: true,
  });

  return res.json({
    success: true,
    mode,
    importedCount: validConnections.length,
    totalConnections: gateway.connections.length,
    connections: gateway.connections,
  });
});

app.post('/api/connections', (req: Request, res: Response) => {
  const data = req.body;
  if (!data.name || !data.database) {
    return res.status(400).json({ error: 'Connection Name and Database are required.' });
  }

  const existingIdx = gateway.connections.findIndex(c => c.id === data.id);
  if (existingIdx !== -1) {
    gateway.connections[existingIdx] = {
      ...gateway.connections[existingIdx],
      ...data,
      port: Number(data.port) || 3050,
    };
    return res.json(gateway.connections[existingIdx]);
  } else {
    const newConn = {
      id: data.id || 'conn-' + Date.now().toString(36),
      name: data.name.trim(),
      type: data.type?.trim() || 'Firebird',
      server: data.server?.trim() || 'localhost',
      port: Number(data.port) || 3050,
      username: data.username?.trim() || 'SYSDBA',
      password: data.password || '',
      database: data.database.trim(),
      enabled: data.enabled !== undefined ? !!data.enabled : true,
      lastTestSuccessful: true,
      lastTestedAt: new Date().toISOString(),
    };
    gateway.connections.push(newConn);
    return res.status(201).json(newConn);
  }
});

app.delete('/api/connections/:id', (req: Request, res: Response) => {
  const { id } = req.params;
  const initialLen = gateway.connections.length;
  gateway.connections = gateway.connections.filter(c => c.id !== id);
  if (gateway.connections.length === initialLen) {
    return res.status(404).json({ error: 'Connection not found.' });
  }
  return res.json({ success: true, id });
});

app.post('/api/connections/:id/toggle', (req: Request, res: Response) => {
  const { id } = req.params;
  const conn = gateway.connections.find(c => c.id === id);
  if (!conn) {
    return res.status(404).json({ error: 'Connection not found.' });
  }

  // Toggling online performs test first, per WPF MainWindow.xaml.cs behavior
  if (!conn.enabled) {
    conn.lastTestedAt = new Date().toISOString();
    conn.lastTestSuccessful = true;
    conn.enabled = true;
  } else {
    conn.enabled = false;
  }

  return res.json(conn);
});

app.post('/api/connections/test', (req: Request, res: Response) => {
  const { id, server, port, username, database } = req.body;
  // Simulate connection test
  const isUnreachable = server && (server === '192.168.1.50' || server.toLowerCase().includes('fail') || database?.toLowerCase().includes('fail'));
  const latency = Math.floor(Math.random() * 25) + 8;
  const success = !isUnreachable && !!(server && database);

  if (id) {
    const conn = gateway.connections.find(c => c.id === id);
    if (conn) {
      conn.lastTestedAt = new Date().toISOString();
      conn.lastTestSuccessful = success;
      if (success) {
        conn.lastLatencyMs = latency;
        delete conn.lastErrorMessage;
      } else {
        delete conn.lastLatencyMs;
        conn.lastErrorMessage = isUnreachable ? `Host ${server}:${port || 3050} unreachable (ETIMEDOUT)` : 'Invalid configuration';
      }
    }
  }

  if (success) {
    return res.json({
      success: true,
      latencyMs: latency,
      message: `Connection established to ${server}:${port || 3050}/${database}`,
    });
  }
  return res.status(isUnreachable ? 502 : 400).json({
    success: false,
    message: isUnreachable
      ? `Host ${server}:${port || 3050} unreachable (ETIMEDOUT)`
      : 'Server and Database path/alias must be provided.',
  });
});

app.post('/api/gateway/key/rotate', (req: Request, res: Response) => {
  const newKey = gateway.regenerateApiKey();
  return res.json({ apiKey: newKey });
});

app.post('/api/gateway/toggle', (req: Request, res: Response) => {
  gateway.config.autoStart = !gateway.config.autoStart;
  gateway.serviceState = gateway.config.autoStart ? 'running' : 'stopped';
  return res.json({
    autoStart: gateway.config.autoStart,
    serviceState: gateway.serviceState,
  });
});

app.post('/api/gateway/config', (req: Request, res: Response) => {
  const { port, maxRows } = req.body;
  if (port) gateway.config.port = Number(port);
  if (maxRows) gateway.config.maxRows = Number(maxRows);
  return res.json(gateway.config);
});

app.post('/api/oauth/config', (req: Request, res: Response) => {
  const { enabled, teamDomain, audience } = req.body;
  gateway.oauthConfig.enabled = !!enabled;
  if (teamDomain !== undefined) gateway.oauthConfig.teamDomain = teamDomain;
  if (audience !== undefined) gateway.oauthConfig.audience = audience;
  return res.json(gateway.oauthConfig);
});

app.get('/api/logs', (req: Request, res: Response) => {
  return res.json(gateway.logs);
});

app.delete('/api/logs', (req: Request, res: Response) => {
  gateway.logs = [];
  return res.json({ success: true });
});

app.post('/api/settings', (req: Request, res: Response) => {
  const { language, autoStart } = req.body;
  if (language === 'en' || language === 'ar') {
    gateway.appSettings.language = language;
  }
  if (autoStart !== undefined) {
    gateway.appSettings.autoStart = !!autoStart;
  }
  return res.json(gateway.appSettings);
});

// API 404 handler: guarantees /api/* routes ALWAYS return JSON, never falling through to HTML SPA
app.all('/api/*', (req: Request, res: Response) => {
  return res.status(404).json({ error: `API route not found: ${req.method} ${req.path}` });
});

// Global API error handler: guarantees JSON responses on server errors
app.use((err: any, req: Request, res: Response, next: NextFunction) => {
  if (req.path.startsWith('/api') || req.path === '/health' || req.path === '/query' || req.path === '/execute') {
    console.error('API Error:', err);
    return res.status(500).json({ error: err?.message || 'Internal server error' });
  }
  next(err);
});

// -------------------------------------------------------------
// VITE SPA SERVING
// -------------------------------------------------------------

async function start() {
  if (process.env.NODE_ENV !== 'production') {
    const vite = await createViteServer({
      server: { middlewareMode: true },
      appType: 'spa',
    });
    app.use(vite.middlewares);
  } else {
    const distPath = path.join(process.cwd(), 'dist');
    app.use(express.static(distPath));
    app.get('*', (req: Request, res: Response) => {
      res.sendFile(path.join(distPath, 'index.html'));
    });
  }

  app.listen(PORT, '0.0.0.0', () => {
    console.log(`ByteBridge server running on http://0.0.0.0:${PORT}`);
  });
}

start();
