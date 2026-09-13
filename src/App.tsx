import React, { useState, useEffect, useCallback } from 'react';
import type { DatabaseConfig, GatewayConfig, OAuthConfig, RequestLogItem, WebServerModeConfig } from './types';
import { Header } from './components/Header';
import { GatewayStatusCard } from './components/GatewayStatusCard';
import { WebServerToggleCard } from './components/WebServerToggleCard';
import { CloudflareOAuthCard } from './components/CloudflareOAuthCard';
import { DatabaseCard } from './components/DatabaseCard';
import { AddEditDatabaseModal } from './components/AddEditDatabaseModal';
import { QueryConsole } from './components/QueryConsole';
import { RequestAuditLogs } from './components/RequestAuditLogs';
import { ApiQuickstart } from './components/ApiQuickstart';
import { SettingsModal } from './components/SettingsModal';
import { translations } from './localization/translations';
import { Database, Terminal, ScrollText, AlertCircle, Plus, BookOpen, Radio } from 'lucide-react';

export const App: React.FC = () => {
  const [language, setLanguage] = useState<'en' | 'ar'>('en');
  const [activeTab, setActiveTab] = useState<'connections' | 'console' | 'logs' | 'quickstart'>('connections');
  const [connections, setConnections] = useState<DatabaseConfig[]>([]);
  const [gatewayConfig, setGatewayConfig] = useState<GatewayConfig>({
    host: '127.0.0.1',
    port: 3000,
    apiKey: '',
    autoStart: true,
    maxRows: 1000,
    commandTimeoutSeconds: 30,
    baseUrl: 'http://127.0.0.1:3000',
  });
  const [serviceState, setServiceState] = useState<'running' | 'stopped' | 'pending'>('running');
  const [oauthConfig, setOAuthConfig] = useState<OAuthConfig>({
    enabled: false,
    teamDomain: '',
    audience: '',
  });
  const [webServerMode, setWebServerMode] = useState<WebServerModeConfig>({
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
  });
  const [logs, setLogs] = useState<RequestLogItem[]>([]);
  const [isRefreshing, setIsRefreshing] = useState(false);

  // Modals state
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [editingConnection, setEditingConnection] = useState<DatabaseConfig | null>(null);
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<{ id: string; name: string } | null>(null);

  const t = translations[language];

  // Set document direction for RTL Arabic support
  useEffect(() => {
    document.documentElement.dir = language === 'ar' ? 'rtl' : 'ltr';
    document.documentElement.lang = language;
  }, [language]);

  // Fetch initial data
  const fetchData = useCallback(async () => {
    try {
      const [statusRes, connsRes, logsRes] = await Promise.all([
        fetch('/api/status'),
        fetch('/api/connections'),
        fetch('/api/logs'),
      ]);

      if (statusRes.ok) {
        const sData = await statusRes.json();
        setGatewayConfig(sData.gateway);
        setServiceState(sData.service.state);
        setOAuthConfig(sData.oauth);
        if (sData.webServerMode) {
          setWebServerMode(sData.webServerMode);
        }
        if (sData.appSettings?.language) {
          setLanguage(sData.appSettings.language);
        }
      }

      if (connsRes.ok) {
        const cData = await connsRes.json();
        setConnections(cData);
      }

      if (logsRes.ok) {
        const lData = await logsRes.json();
        setLogs(lData);
      }
    } catch (e) {
      console.error('Failed to poll status', e);
    }
  }, []);

  useEffect(() => {
    fetchData();
    // Poll every 3 seconds like WPF DispatcherTimer
    const interval = setInterval(fetchData, 3000);
    return () => clearInterval(interval);
  }, [fetchData]);

  const handleManualRefresh = async () => {
    setIsRefreshing(true);
    await fetchData();
    setTimeout(() => setIsRefreshing(false), 500);
  };

  // Gateway actions
  const handleRotateKey = async () => {
    const res = await fetch('/api/gateway/key/rotate', { method: 'POST' });
    if (res.ok) {
      const data = await res.json();
      setGatewayConfig(prev => ({ ...prev, apiKey: data.apiKey }));
      await fetchData();
    }
  };

  const handleToggleGateway = async () => {
    const res = await fetch('/api/gateway/toggle', { method: 'POST' });
    if (res.ok) {
      const data = await res.json();
      setGatewayConfig(prev => ({ ...prev, autoStart: data.autoStart }));
      setServiceState(data.serviceState);
      await fetchData();
    }
  };

  const handleUpdatePort = async (newPort: number) => {
    const res = await fetch('/api/gateway/config', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ port: newPort }),
    });
    if (res.ok) {
      const data = await res.json();
      setGatewayConfig(data);
    }
  };

  const handleSaveOAuth = async (newOAuthConfig: Partial<OAuthConfig>) => {
    const res = await fetch('/api/oauth/config', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(newOAuthConfig),
    });
    if (res.ok) {
      const data = await res.json();
      setOAuthConfig(data);
    }
  };

  const handleToggleWebServerMode = async (enable: boolean) => {
    const res = await fetch('/api/webserver/toggle', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ enable }),
    });
    if (res.ok) {
      const data = await res.json();
      setWebServerMode(data);
      await fetchData();
    }
  };

  // Connection actions
  const handleToggleConnection = async (id: string) => {
    const res = await fetch(`/api/connections/${id}/toggle`, { method: 'POST' });
    if (res.ok) {
      await fetchData();
    }
  };

  const handleSaveConnection = async (connData: Partial<DatabaseConfig>) => {
    const res = await fetch('/api/connections', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(connData),
    });
    if (!res.ok) {
      const err = await res.json();
      throw new Error(err.error || 'Failed to save');
    }
    await fetchData();
  };

  const handleDeleteConnection = async (id: string) => {
    const res = await fetch(`/api/connections/${id}`, { method: 'DELETE' });
    if (res.ok) {
      setDeleteTarget(null);
      await fetchData();
    }
  };

  const handleTestConnection = async (connData: Partial<DatabaseConfig>) => {
    const res = await fetch('/api/connections/test', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(connData),
    });
    return await res.json();
  };

  const handleClearLogs = async () => {
    const res = await fetch('/api/logs', { method: 'DELETE' });
    if (res.ok) {
      setLogs([]);
    }
  };

  const handleSaveSettings = async (settings: { language: 'en' | 'ar'; autoStart: boolean }) => {
    setLanguage(settings.language);
    await fetch('/api/settings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(settings),
    });
  };

  return (
    <div className="min-h-screen bg-[#f8f9fa] flex flex-col justify-between selection:bg-stone-900 selection:text-white">
      <div className="max-w-5xl w-full mx-auto p-4 sm:p-6 lg:p-8 space-y-6">
        {/* Header */}
        <Header
          language={language}
          onLanguageChange={setLanguage}
          onAddData={() => {
            setEditingConnection(null);
            setIsAddModalOpen(true);
          }}
          onOpenSettings={() => setIsSettingsOpen(true)}
          onRefresh={handleManualRefresh}
          isRefreshing={isRefreshing}
          isWebServerMode={webServerMode.enabled}
          onScrollToWebServer={() => {
            const el = document.getElementById('web-server-mode-card');
            if (el) el.scrollIntoView({ behavior: 'smooth' });
          }}
        />

        {/* Top Gateway Card */}
        <GatewayStatusCard
          config={gatewayConfig}
          serviceState={serviceState}
          language={language}
          onRotateKey={handleRotateKey}
          onToggleGateway={handleToggleGateway}
          onUpdatePort={handleUpdatePort}
        />

        {/* Web Server Mode & Cloudflare Dual Tunnel Card */}
        <WebServerToggleCard
          config={webServerMode}
          language={language}
          onToggle={handleToggleWebServerMode}
          onRefresh={fetchData}
        />

        {/* Cloudflare Edge Auth Card */}
        <CloudflareOAuthCard
          config={oauthConfig}
          language={language}
          onSave={handleSaveOAuth}
        />

        {/* Navigation Tabs */}
        <div className="flex items-center gap-1 border-b border-stone-200 pb-px">
          <button
            onClick={() => setActiveTab('connections')}
            className={`flex items-center gap-2 px-4 py-2.5 text-xs font-semibold rounded-t-lg transition-all border-b-2 ${
              activeTab === 'connections'
                ? 'border-stone-900 text-stone-900 bg-white shadow-xs'
                : 'border-transparent text-stone-500 hover:text-stone-800'
            }`}
          >
            <Database className="w-3.5 h-3.5" />
            <span>{t.tabConnections}</span>
            <span className="ml-1 px-1.5 py-0.2 rounded-full text-[10px] bg-stone-100 text-stone-600 font-mono">
              {connections.length}
            </span>
          </button>

          <button
            onClick={() => setActiveTab('console')}
            className={`flex items-center gap-2 px-4 py-2.5 text-xs font-semibold rounded-t-lg transition-all border-b-2 ${
              activeTab === 'console'
                ? 'border-stone-900 text-stone-900 bg-white shadow-xs'
                : 'border-transparent text-stone-500 hover:text-stone-800'
            }`}
          >
            <Terminal className="w-3.5 h-3.5" />
            <span>{t.tabConsole}</span>
          </button>

          <button
            onClick={() => setActiveTab('logs')}
            className={`flex items-center gap-2 px-4 py-2.5 text-xs font-semibold rounded-t-lg transition-all border-b-2 ${
              activeTab === 'logs'
                ? 'border-stone-900 text-stone-900 bg-white shadow-xs'
                : 'border-transparent text-stone-500 hover:text-stone-800'
            }`}
          >
            <ScrollText className="w-3.5 h-3.5" />
            <span>{t.tabLogs}</span>
            {logs.length > 0 && (
              <span className="ml-1 px-1.5 py-0.2 rounded-full text-[10px] bg-stone-100 text-stone-600 font-mono">
                {logs.length}
              </span>
            )}
          </button>

          <button
            onClick={() => setActiveTab('quickstart')}
            className={`flex items-center gap-2 px-4 py-2.5 text-xs font-semibold rounded-t-lg transition-all border-b-2 ${
              activeTab === 'quickstart'
                ? 'border-stone-900 text-stone-900 bg-white shadow-xs'
                : 'border-transparent text-stone-500 hover:text-stone-800'
            }`}
          >
            <BookOpen className="w-3.5 h-3.5" />
            <span>{t.tabQuickstart}</span>
          </button>
        </div>

        {/* Tab Content */}
        {activeTab === 'connections' && (
          <div className="space-y-3">
            {connections.length === 0 ? (
              <div className="bg-white rounded-xl border border-stone-200 p-12 text-center shadow-xs">
                <Database className="w-10 h-10 text-stone-300 mx-auto mb-3" />
                <h3 className="text-base font-bold text-stone-800">{t.noDatabases}</h3>
                <p className="text-xs text-stone-500 mt-1 max-w-sm mx-auto">
                  {t.noDatabasesHint}
                </p>
                <button
                  onClick={() => {
                    setEditingConnection(null);
                    setIsAddModalOpen(true);
                  }}
                  className="mt-4 inline-flex items-center gap-1.5 px-4 py-2 text-xs font-semibold rounded-lg bg-stone-900 text-white hover:bg-stone-800 transition-colors shadow-xs"
                >
                  <Plus className="w-3.5 h-3.5" />
                  <span>{t.addData}</span>
                </button>
              </div>
            ) : (
              <div className="grid grid-cols-1 gap-3">
                {connections.map(conn => (
                  <DatabaseCard
                    key={conn.id}
                    connection={conn}
                    language={language}
                    onToggle={handleToggleConnection}
                    onEdit={conn => {
                      setEditingConnection(conn);
                      setIsAddModalOpen(true);
                    }}
                    onDelete={(id, name) => setDeleteTarget({ id, name })}
                    onTest={handleTestConnection}
                  />
                ))}
              </div>
            )}
          </div>
        )}

        {activeTab === 'console' && (
          <QueryConsole
            connections={connections}
            apiKey={gatewayConfig.apiKey}
            language={language}
          />
        )}

        {activeTab === 'logs' && (
          <RequestAuditLogs
            logs={logs}
            language={language}
            onClearLogs={handleClearLogs}
          />
        )}

        {activeTab === 'quickstart' && (
          <ApiQuickstart config={gatewayConfig} />
        )}
      </div>

      {/* Footer info matching WPF MainWindow */}
      <footer className="border-t border-stone-200 py-4 px-6 text-center text-xs text-stone-400 font-mono">
        ByteBridge v1.0.0 • HTTP Gateway bound to {gatewayConfig.host}:{gatewayConfig.port}
      </footer>

      {/* Add / Edit Database Modal */}
      <AddEditDatabaseModal
        isOpen={isAddModalOpen}
        editingConnection={editingConnection}
        language={language}
        onClose={() => {
          setIsAddModalOpen(false);
          setEditingConnection(null);
        }}
        onSave={handleSaveConnection}
        onTest={handleTestConnection}
      />

      {/* Settings Modal */}
      <SettingsModal
        isOpen={isSettingsOpen}
        language={language}
        autoStart={gatewayConfig.autoStart}
        onClose={() => setIsSettingsOpen(false)}
        onSave={handleSaveSettings}
      />

      {/* Delete Confirmation Modal */}
      {deleteTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-stone-900/40 backdrop-blur-xs p-4">
          <div className="bg-white rounded-xl max-w-sm w-full p-6 shadow-xl border border-stone-200 space-y-4">
            <div className="flex items-start gap-3">
              <div className="p-2 rounded-lg bg-rose-50 text-rose-600 border border-rose-200">
                <AlertCircle className="w-5 h-5" />
              </div>
              <div>
                <h3 className="text-base font-bold text-stone-900">{t.delete}</h3>
                <p className="text-xs text-stone-600 mt-1 leading-relaxed">
                  {t.deleteConfirm.replace('{0}', deleteTarget.name)}
                </p>
              </div>
            </div>

            <div className="flex items-center justify-end gap-2 pt-2 border-t border-stone-100">
              <button
                onClick={() => setDeleteTarget(null)}
                className="px-3 py-1.5 text-xs font-semibold rounded-lg border border-stone-300 text-stone-700 hover:bg-stone-100"
              >
                {t.cancel}
              </button>
              <button
                onClick={() => handleDeleteConnection(deleteTarget.id)}
                className="px-4 py-1.5 text-xs font-semibold rounded-lg bg-rose-600 text-white hover:bg-rose-700"
              >
                {t.delete}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default App;
