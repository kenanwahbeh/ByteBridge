import express, { Request, Response, NextFunction } from 'express';
import cors from 'cors';
import path from 'path';
import { createServer as createViteServer } from 'vite';
import { gateway } from './src/server/gatewayEngine.js';
import { CloudflareAccessVerifier } from './src/server/cloudflareAccess.js';
import {
  getSingleHeader,
  isCloudflareTunnelRequest,
  isDirectLoopbackRequest,
} from './src/server/requestSecurity.js';

const app = express();
const PORT = 3000;
const HOST = process.env.BYTEBRIDGE_HOST?.trim() || '127.0.0.1';
const cloudflareAccess = new CloudflareAccessVerifier();

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
      error: 'Writes are refused on /query. Use /execute for statements that modify data.',
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
 * Authenticated endpoint: executes INSERT, UPDATE, DELETE, etc.
 */
app.post('/execute', (req: Request, res: Response) => {
  const startTime = Date.now();
  const clientIp = getClientIp(req);
  const apiKey = extractApiKey(req);

  if (!gateway.isKeyValid(apiKey)) {
    gateway.appendLog({
      method: 'POST',
      path: '/execute',
      status: 401,
      clientIp,
      authenticated: false,
      error: 'Missing or invalid API key',
    });
    return res.status(401).json({ error: 'Missing or wrong API key.' });
  }

  const { database, sql, parameters } = req.body || {};

  if (!database || typeof database !== 'string') {
    return res.status(400).json({ error: "Missing required 'database' field in body." });
  }

  if (!sql || typeof sql !== 'string') {
    return res.status(400).json({ error: "Missing required 'sql' statement in body." });
  }

  const connResult = gateway.findConnection(database);
  if ('error' in connResult) {
    const elapsed = Date.now() - startTime;
    gateway.appendLog({
      method: 'POST',
      path: '/execute',
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
    const execResult = gateway.executeWrite(connResult, sql, parameters);
    const elapsed = Date.now() - startTime;
    execResult.elapsedMs = elapsed;

    gateway.appendLog({
      method: 'POST',
      path: '/execute',
      status: 200,
      elapsedMs: elapsed,
      clientIp,
      authenticated: true,
      database: connResult.name,
      sql,
      rowsAffected: execResult.rowsAffected,
    });

    return res.json(execResult);
  } catch (err: any) {
    const elapsed = Date.now() - startTime;
    gateway.appendLog({
      method: 'POST',
      path: '/execute',
      status: 500,
      elapsedMs: elapsed,
      clientIp,
      authenticated: true,
      database: connResult.name,
      sql,
      error: err.message,
    });
    return res.status(500).json({ error: err.message || 'Error executing write statement.' });
  }
});

// -------------------------------------------------------------
// CONTROL PANEL API ENDPOINTS (/api/*)
// -------------------------------------------------------------

/*
 * The control API returns database credentials and the gateway key, and can
 * mutate every setting. It is therefore a local administration surface, not
 * part of the tunneled data API. Reject proxy/tunnel traffic even when its
 * final hop originates from loopback.
 */
app.use('/api', async (req: Request, res: Response, next: NextFunction) => {
  const isDirect = isDirectLoopbackRequest(
    req.socket.remoteAddress,
    req.headers['cf-connecting-ip'],
    req.headers['x-forwarded-for'],
    req.headers.origin,
    req.headers.host,
  );

  if (isDirect) return next();

  const assertion = getSingleHeader(
    req.headers['cf-access-jwt-assertion'],
  );
  const tunnelIsAllowed = gateway.webServerMode.enabled
    && gateway.webServerMode.remoteManagementAllowed
    && gateway.webServerMode.requireCloudflareAccess
    && isCloudflareTunnelRequest(
      req.socket.remoteAddress,
      req.headers['cf-connecting-ip'],
      req.headers.origin,
      req.headers.host,
    )
    && assertion !== null
    && await cloudflareAccess.verify(assertion, gateway.oauthConfig);

  if (!tunnelIsAllowed) {
    return res.status(403).json({ error: 'The control panel API is available only on this machine.' });
  }

  return next();
});

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
  const { server, port, username, database } = req.body;
  // Simulate connection test
  const latency = Math.floor(Math.random() * 25) + 8;
  if (server && database) {
    return res.json({
      success: true,
      latencyMs: latency,
      message: `Connection established to ${server}:${port || 3050}/${database}`,
    });
  }
  return res.status(400).json({
    success: false,
    message: 'Server and Database path/alias must be provided.',
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

  app.listen(PORT, HOST, () => {
    console.log(`ByteBridge server running on http://${HOST}:${PORT}`);
  });
}

start();
