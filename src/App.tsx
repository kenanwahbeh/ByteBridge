import React, { useState, useEffect, useCallback, useMemo } from 'react';
import type { DatabaseConfig, GatewayConfig, OAuthConfig, RequestLogItem, WebServerModeConfig } from './types';
import { Header } from './components/Header';
import { GatewayStatusCard } from './components/GatewayStatusCard';
import { WebServerToggleCard } from './components/WebServerToggleCard';
import { CloudflareOAuthCard } from './components/CloudflareOAuthCard';
import { DatabaseCard } from './components/DatabaseCard';
import { AddEditDatabaseModal } from './components/AddEditDatabaseModal';
import { RequestAuditLogs } from './components/RequestAuditLogs';
import { ApiQuickstart } from './components/ApiQuickstart';
import { SettingsModal } from './components/SettingsModal';
import { ExportConnectionsModal } from './components/ExportConnectionsModal';
import { ImportConnectionsModal } from './components/ImportConnectionsModal';
import { ConnectionsSummaryRow } from './components/ConnectionsSummaryRow';
import { ConnectionActivityChart } from './components/ConnectionActivityChart';
import { translations } from './localization/translations';
import {
  Database,
  ScrollText,
  AlertCircle,
  Plus,
  BookOpen,
  Radio,
  Search,
  X,
  Filter,
  Download,
  Upload,
  CheckCircle2,
  ArrowUpDown,
  ArrowUp,
  ArrowDown,
  ArrowDownAZ,
  ArrowUpZA,
} from 'lucide-react';

export const App: React.FC = () => {
  const [language, setLanguage] = useState<'en' | 'ar'>('en');
  const [activeTab, setActiveTab] = useState<'connections' | 'logs' | 'quickstart'>('connections');
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
        nameAr: 'Database Gateway Tunnel (Data API)',
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
        nameAr: 'Web Server Control Panel Tunnel (Remote UI & Mgmt)',
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
  const [isExportModalOpen, setIsExportModalOpen] = useState(false);
  const [isImportModalOpen, setIsImportModalOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<{ id: string; name: string } | null>(null);
  const [toastNotification, setToastNotification] = useState<{ message: string; type: 'success' | 'info' } | null>(null);

  // Search, Type Filtering & Automatic Sorting
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedTypeFilter, setSelectedTypeFilter] = useState('ALL');
  const [onlineFilter, setOnlineFilter] = useState<'ALL' | 'ONLINE' | 'OFFLINE'>('ALL');
  const [showActivityChart, setShowActivityChart] = useState(true);
  const [sortField, setSortField] = useState<'name' | 'type' | 'lastActive'>('name');
  const [sortOrder, setSortOrder] = useState<'asc' | 'desc'>('asc');

  const getConnectionType = useCallback((conn: DatabaseConfig): string => {
    if (conn.type) return conn.type;
    if (conn.database.endsWith('.sqlite') || conn.database.endsWith('.db')) return 'SQLite';
    if (conn.port === 5432) return 'PostgreSQL';
    if (conn.port === 3306) return 'MySQL';
    return 'Firebird';
  }, []);

  const getConnectionLastActiveTimestamp = useCallback((conn: DatabaseConfig): number => {
    let latestTime = conn.lastTestedAt ? new Date(conn.lastTestedAt).getTime() : 0;
    if (logs && logs.length > 0) {
      const connNameLower = conn.name.toLowerCase();
      const connIdLower = conn.id.toLowerCase();
      for (const log of logs) {
        if (log.database) {
          const logDbLower = log.database.toLowerCase();
          if (logDbLower === connNameLower || logDbLower === connIdLower) {
            const logTime = new Date(log.at).getTime();
            if (logTime > latestTime) {
              latestTime = logTime;
            }
          }
        }
      }
    }
    return latestTime;
  }, [logs]);

  const availableTypes = useMemo(() => Array.from(new Set(connections.map(c => getConnectionType(c)))), [connections, getConnectionType]);

  const filteredAndSortedConnections = useMemo(() => {
    const filtered = connections.filter(conn => {
      // Quick filter by Online / Offline status
      if (onlineFilter === 'ONLINE' && !conn.enabled) return false;
      if (onlineFilter === 'OFFLINE' && conn.enabled) return false;

      const connType = getConnectionType(conn);
      if (selectedTypeFilter !== 'ALL' && connType.toLowerCase() !== selectedTypeFilter.toLowerCase()) {
        return false;
      }
      if (!searchQuery.trim()) return true;
      const q = searchQuery.toLowerCase().trim();
      return (
        conn.name.toLowerCase().includes(q) ||
        connType.toLowerCase().includes(q) ||
        conn.database.toLowerCase().includes(q) ||
        conn.server.toLowerCase().includes(q) ||
        conn.username.toLowerCase().includes(q) ||
        conn.id.toLowerCase().includes(q)
      );
    });

    return [...filtered].sort((a, b) => {
      let comparison = 0;
      if (sortField === 'name') {
        comparison = a.name.localeCompare(b.name, language, { sensitivity: 'base' });
      } else if (sortField === 'type') {
        const typeA = getConnectionType(a);
        const typeB = getConnectionType(b);
        comparison = typeA.localeCompare(typeB, language, { sensitivity: 'base' });
        if (comparison === 0) {
          comparison = a.name.localeCompare(b.name, language, { sensitivity: 'base' });
        }
      } else if (sortField === 'lastActive') {
        const timeA = getConnectionLastActiveTimestamp(a);
        const timeB = getConnectionLastActiveTimestamp(b);
        comparison = timeA - timeB;
        if (comparison === 0) {
          comparison = a.name.localeCompare(b.name, language, { sensitivity: 'base' });
        }
      }

      return sortOrder === 'asc' ? comparison : -comparison;
    });
  }, [connections, selectedTypeFilter, searchQuery, sortField, sortOrder, language, getConnectionType, getConnectionLastActiveTimestamp]);

  const t = translations[language];

  // Set document direction for RTL Arabic support
  useEffect(() => {
    document.documentElement.dir = language === 'ar' ? 'rtl' : 'ltr';
    document.documentElement.lang = language;
  }, [language]);

  // Fetch initial data
  const fetchData = useCallback(async () => {
    try {
      const [statusRes, connsRes, logsRes] = await Promise.allSettled([
        fetch('/api/status', { headers: { Accept: 'application/json' } }),
        fetch('/api/connections', { headers: { Accept: 'application/json' } }),
        fetch('/api/logs', { headers: { Accept: 'application/json' } }),
      ]);

      if (statusRes.status === 'fulfilled' && statusRes.value.ok) {
        const ct = statusRes.value.headers.get('content-type') || '';
        if (ct.includes('application/json')) {
          const sData = await statusRes.value.json();
          if (sData && sData.gateway) {
            setGatewayConfig(sData.gateway);
            setServiceState(sData.service?.state || 'running');
            if (sData.oauth) setOAuthConfig(sData.oauth);
            if (sData.webServerMode) setWebServerMode(sData.webServerMode);
            if (sData.appSettings?.language) setLanguage(sData.appSettings.language);
          }
        }
      }

      if (connsRes.status === 'fulfilled' && connsRes.value.ok) {
        const ct = connsRes.value.headers.get('content-type') || '';
        if (ct.includes('application/json')) {
          const cData = await connsRes.value.json();
          if (Array.isArray(cData)) {
            setConnections(cData);
          }
        }
      }

      if (logsRes.status === 'fulfilled' && logsRes.value.ok) {
        const ct = logsRes.value.headers.get('content-type') || '';
        if (ct.includes('application/json')) {
          const lData = await logsRes.value.json();
          if (Array.isArray(lData)) {
            setLogs(lData);
          }
        }
      }
    } catch {
      // Gracefully ignore transient poll or warmup states without logging fatal errors
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
    try {
      const res = await fetch('/api/gateway/key/rotate', {
        method: 'POST',
        headers: { Accept: 'application/json' },
      });
      if (res.ok && res.headers.get('content-type')?.includes('application/json')) {
        const data = await res.json();
        setGatewayConfig(prev => ({ ...prev, apiKey: data.apiKey }));
        await fetchData();
      }
    } catch {
      // ignore
    }
  };

  const handleToggleGateway = async () => {
    try {
      const res = await fetch('/api/gateway/toggle', {
        method: 'POST',
        headers: { Accept: 'application/json' },
      });
      if (res.ok && res.headers.get('content-type')?.includes('application/json')) {
        const data = await res.json();
        setGatewayConfig(prev => ({ ...prev, autoStart: data.autoStart }));
        setServiceState(data.serviceState);
        await fetchData();
      }
    } catch {
      // ignore
    }
  };

  const handleUpdatePort = async (newPort: number) => {
    try {
      const res = await fetch('/api/gateway/config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({ port: newPort }),
      });
      if (res.ok && res.headers.get('content-type')?.includes('application/json')) {
        const data = await res.json();
        setGatewayConfig(data);
      }
    } catch {
      // ignore
    }
  };

  const handleSaveOAuth = async (newOAuthConfig: Partial<OAuthConfig>) => {
    try {
      const res = await fetch('/api/oauth/config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(newOAuthConfig),
      });
      if (res.ok && res.headers.get('content-type')?.includes('application/json')) {
        const data = await res.json();
        setOAuthConfig(data);
      }
    } catch {
      // ignore
    }
  };

  const handleToggleWebServerMode = async (enable: boolean) => {
    try {
      const res = await fetch('/api/webserver/toggle', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({ enable }),
      });
      if (res.ok && res.headers.get('content-type')?.includes('application/json')) {
        const data = await res.json();
        setWebServerMode(data);
        await fetchData();
      }
    } catch {
      // ignore
    }
  };

  // Connection actions
  const handleToggleConnection = async (id: string) => {
    try {
      const res = await fetch(`/api/connections/${id}/toggle`, { method: 'POST', headers: { Accept: 'application/json' } });
      if (res.ok) {
        await fetchData();
      }
    } catch {
      // ignore
    }
  };

  const handleSaveConnection = async (connData: Partial<DatabaseConfig>) => {
    const res = await fetch('/api/connections', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(connData),
    });
    if (!res.ok) {
      let errMsg = 'Failed to save';
      if (res.headers.get('content-type')?.includes('application/json')) {
        const err = await res.json();
        errMsg = err.error || errMsg;
      }
      throw new Error(errMsg);
    }
    await fetchData();
  };

  const handleDeleteConnection = async (id: string) => {
    const res = await fetch(`/api/connections/${id}`, { method: 'DELETE', headers: { Accept: 'application/json' } });
    if (res.ok) {
      setDeleteTarget(null);
      await fetchData();
    }
  };

  const handleImportSuccess = (count: number) => {
    fetchData();
    setToastNotification({
      message: t.importSuccessMsg.replace('{0}', String(count)),
      type: 'success',
    });
    setTimeout(() => setToastNotification(null), 4000);
  };

  const handleTestConnection = async (connData: Partial<DatabaseConfig>) => {
    try {
      const res = await fetch('/api/connections/test', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(connData),
      });
      if (res.headers.get('content-type')?.includes('application/json')) {
        const data = await res.json();
        await fetchData();
        return data;
      }
      await fetchData();
      return { success: res.ok, message: res.ok ? 'Connection reachable' : `Status ${res.status}` };
    } catch (e: any) {
      await fetchData();
      return { success: false, message: e?.message || 'Network error' };
    }
  };

  const handleClearLogs = async () => {
    const res = await fetch('/api/logs', { method: 'DELETE', headers: { Accept: 'application/json' } });
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
          <div className="space-y-4">
            {/* Connections Summary Row: total active vs total connections & quick count of online vs offline */}
            {connections.length > 0 && (
              <ConnectionsSummaryRow
                connections={connections}
                language={language}
                onlineFilter={onlineFilter}
                onFilterChange={setOnlineFilter}
                showChart={showActivityChart}
                onToggleChart={() => setShowActivityChart(prev => !prev)}
              />
            )}

            {/* Connection Activity Chart: recharts visualization over time based on logs and lastTestedAt timestamps */}
            {connections.length > 0 && showActivityChart && (
              <ConnectionActivityChart
                connections={connections}
                logs={logs}
                language={language}
              />
            )}

            {/* Search and Filter Toolbar (rendered when connections exist or when searching) */}
            {connections.length > 0 && (
              <div className="bg-white rounded-xl border border-stone-200 p-3 shadow-xs space-y-3">
                <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-3">
                  {/* Search Input Field */}
                  <div className="relative flex-1">
                    <div
                      className={`absolute top-1/2 -translate-y-1/2 pointer-events-none text-stone-400 ${
                        language === 'ar' ? 'right-3' : 'left-3'
                      }`}
                    >
                      <Search className="w-4 h-4" />
                    </div>
                    <input
                      type="text"
                      value={searchQuery}
                      onChange={e => setSearchQuery(e.target.value)}
                      placeholder={t.searchPlaceholder}
                      className={`w-full py-2 rounded-lg border border-stone-200 bg-stone-50/70 text-stone-900 text-xs placeholder:text-stone-400 focus:outline-none focus:ring-2 focus:ring-stone-900/10 focus:border-stone-400 focus:bg-white transition-all ${
                        language === 'ar' ? 'pr-9 pl-8' : 'pl-9 pr-8'
                      }`}
                    />
                    {searchQuery && (
                      <button
                        onClick={() => setSearchQuery('')}
                        className={`absolute top-1/2 -translate-y-1/2 p-1 text-stone-400 hover:text-stone-700 hover:bg-stone-200/60 rounded-full transition-colors ${
                          language === 'ar' ? 'left-2.5' : 'right-2.5'
                        }`}
                        title={t.clearSearch}
                      >
                        <X className="w-3.5 h-3.5" />
                      </button>
                    )}
                  </div>

                  {/* Engine Dropdown, Sort Controls, Export/Import & Add Data Action */}
                  <div className="flex items-center gap-2 shrink-0 flex-wrap">
                    {/* Engine Filter Dropdown */}
                    <div className="relative flex items-center">
                      <select
                        value={selectedTypeFilter}
                        onChange={e => setSelectedTypeFilter(e.target.value)}
                        className="py-2 px-3 rounded-lg border border-stone-200 bg-white text-stone-800 text-xs font-medium focus:outline-none focus:ring-2 focus:ring-stone-900/10 focus:border-stone-400 cursor-pointer shadow-2xs"
                      >
                        <option value="ALL">{t.filterAllTypes}</option>
                        {availableTypes.map(tName => (
                          <option key={tName} value={tName}>
                            {tName}
                          </option>
                        ))}
                      </select>
                    </div>

                    {/* Sort By Dropdown */}
                    <div className="relative flex items-center">
                      <select
                        value={sortField}
                        onChange={e => setSortField(e.target.value as 'name' | 'type' | 'lastActive')}
                        title={t.sortBy}
                        className="py-2 px-3 rounded-lg border border-stone-200 bg-white text-stone-800 text-xs font-medium focus:outline-none focus:ring-2 focus:ring-stone-900/10 focus:border-stone-400 cursor-pointer shadow-2xs"
                      >
                        <option value="name">{t.sortBy}: {t.sortByName}</option>
                        <option value="type">{t.sortBy}: {t.sortByType}</option>
                        <option value="lastActive">{t.sortBy}: {t.sortByLastActive}</option>
                      </select>
                    </div>

                    {/* Sort Order Toggle Button */}
                    <button
                      type="button"
                      onClick={() => setSortOrder(prev => (prev === 'asc' ? 'desc' : 'asc'))}
                      title={`${t.sortOrder}: ${sortOrder === 'asc' ? t.sortAscending : t.sortDescending}`}
                      className="inline-flex items-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-lg border border-stone-200 bg-white text-stone-700 hover:bg-stone-50 hover:text-stone-900 transition-colors shadow-2xs"
                    >
                      {sortField === 'lastActive' ? (
                        sortOrder === 'asc' ? (
                          <ArrowUp className="w-3.5 h-3.5 text-stone-600" />
                        ) : (
                          <ArrowDown className="w-3.5 h-3.5 text-stone-600" />
                        )
                      ) : sortOrder === 'asc' ? (
                        <ArrowDownAZ className="w-3.5 h-3.5 text-stone-600" />
                      ) : (
                        <ArrowUpZA className="w-3.5 h-3.5 text-stone-600" />
                      )}
                      <span className="hidden md:inline">
                        {sortOrder === 'asc' ? t.sortAscending : t.sortDescending}
                      </span>
                    </button>

                    <button
                      type="button"
                      onClick={() => setIsExportModalOpen(true)}
                      title={t.exportConnections}
                      className="inline-flex items-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-lg border border-stone-200 bg-white text-stone-700 hover:bg-stone-50 hover:text-stone-900 transition-colors shadow-2xs"
                    >
                      <Download className="w-3.5 h-3.5 text-stone-500" />
                      <span className="hidden sm:inline">{t.exportConnections}</span>
                      <span className="sm:hidden">{language === 'ar' ? 'تصدير' : 'Export'}</span>
                    </button>

                    <button
                      type="button"
                      onClick={() => setIsImportModalOpen(true)}
                      title={t.importConnections}
                      className="inline-flex items-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-lg border border-stone-200 bg-white text-stone-700 hover:bg-stone-50 hover:text-stone-900 transition-colors shadow-2xs"
                    >
                      <Upload className="w-3.5 h-3.5 text-stone-500" />
                      <span className="hidden sm:inline">{t.importConnections}</span>
                      <span className="sm:hidden">{language === 'ar' ? 'استيراد' : 'Import'}</span>
                    </button>

                    <button
                      onClick={() => {
                        setEditingConnection(null);
                        setIsAddModalOpen(true);
                      }}
                      className="inline-flex items-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-lg bg-stone-900 text-white hover:bg-stone-800 transition-colors shadow-2xs shrink-0"
                    >
                      <Plus className="w-3.5 h-3.5" />
                      <span>{t.addData}</span>
                    </button>
                  </div>
                </div>

                {/* Engine Quick Pills & Results Counter */}
                <div className="flex flex-wrap items-center justify-between gap-2 pt-2 border-t border-stone-100 text-xs text-stone-500">
                  <div className="flex items-center gap-1.5 flex-wrap">
                    <span className="text-[11px] font-semibold text-stone-400 uppercase tracking-wider mr-1">
                      {t.filterByType}:
                    </span>
                    <button
                      onClick={() => setSelectedTypeFilter('ALL')}
                      className={`px-2.5 py-0.5 rounded-full text-[11px] font-medium transition-all ${
                        selectedTypeFilter === 'ALL'
                          ? 'bg-stone-900 text-white font-semibold shadow-2xs'
                          : 'bg-stone-100 text-stone-600 hover:bg-stone-200'
                      }`}
                    >
                      {t.filterAllTypes}
                    </button>
                    {availableTypes.map(tName => (
                      <button
                        key={tName}
                        onClick={() => setSelectedTypeFilter(tName)}
                        className={`px-2.5 py-0.5 rounded-full text-[11px] font-medium transition-all ${
                          selectedTypeFilter === tName
                            ? 'bg-stone-900 text-white font-semibold shadow-2xs'
                            : 'bg-stone-100 text-stone-600 hover:bg-stone-200'
                        }`}
                      >
                        {tName}
                      </button>
                    ))}
                  </div>

                  <div className="flex items-center gap-2 flex-wrap">
                    <span className="text-[11px] font-mono text-stone-400">
                      {t.showingResults
                        .replace('{0}', String(filteredAndSortedConnections.length))
                        .replace('{1}', String(connections.length))}
                      {' • '}
                      <span className="font-semibold text-stone-600">
                        {sortField === 'name' ? t.sortByName : sortField === 'type' ? t.sortByType : t.sortByLastActive}
                      </span>
                      {' '}({sortOrder === 'asc' ? t.sortAscending : t.sortDescending})
                    </span>
                    {(searchQuery || selectedTypeFilter !== 'ALL' || onlineFilter !== 'ALL') && (
                      <button
                        onClick={() => {
                          setSearchQuery('');
                          setSelectedTypeFilter('ALL');
                          setOnlineFilter('ALL');
                        }}
                        className="text-[11px] text-stone-500 hover:text-stone-800 underline transition-colors"
                      >
                        {t.clearSearch}
                      </button>
                    )}
                  </div>
                </div>
              </div>
            )}

            {/* Empty state when no databases exist in system */}
            {connections.length === 0 ? (
              <div className="bg-white rounded-xl border border-stone-200 p-12 text-center shadow-xs">
                <Database className="w-10 h-10 text-stone-300 mx-auto mb-3" />
                <h3 className="text-base font-bold text-stone-800">{t.noDatabases}</h3>
                <p className="text-xs text-stone-500 mt-1 max-w-sm mx-auto">
                  {t.noDatabasesHint}
                </p>
                <div className="mt-4 flex items-center justify-center gap-2.5 flex-wrap">
                  <button
                    onClick={() => {
                      setEditingConnection(null);
                      setIsAddModalOpen(true);
                    }}
                    className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-semibold rounded-lg bg-stone-900 text-white hover:bg-stone-800 transition-colors shadow-xs"
                  >
                    <Plus className="w-3.5 h-3.5" />
                    <span>{t.addData}</span>
                  </button>
                  <button
                    onClick={() => setIsImportModalOpen(true)}
                    className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs font-semibold rounded-lg bg-white border border-stone-300 text-stone-700 hover:bg-stone-50 transition-colors shadow-2xs"
                  >
                    <Upload className="w-3.5 h-3.5 text-stone-500" />
                    <span>{t.importConnections}</span>
                  </button>
                </div>
              </div>
            ) : filteredAndSortedConnections.length === 0 ? (
              /* Empty state when search or filter returns no results */
              <div className="bg-white rounded-xl border border-stone-200 p-10 text-center shadow-xs">
                <div className="w-12 h-12 rounded-full bg-stone-100 flex items-center justify-center mx-auto mb-3 text-stone-400">
                  <Search className="w-6 h-6" />
                </div>
                <h3 className="text-base font-bold text-stone-800">
                  {t.noSearchMatches.replace('{0}', searchQuery || selectedTypeFilter)}
                </h3>
                <p className="text-xs text-stone-500 mt-1 max-w-sm mx-auto">
                  {t.noSearchMatchesHint}
                </p>
                <button
                  onClick={() => {
                    setSearchQuery('');
                    setSelectedTypeFilter('ALL');
                    setOnlineFilter('ALL');
                  }}
                  className="mt-4 inline-flex items-center gap-1.5 px-3.5 py-1.5 text-xs font-semibold rounded-lg bg-stone-100 text-stone-700 hover:bg-stone-200 border border-stone-300 transition-colors"
                >
                  <X className="w-3.5 h-3.5" />
                  <span>{t.clearSearch}</span>
                </button>
              </div>
            ) : (
              <div className="grid grid-cols-1 gap-3">
                {filteredAndSortedConnections.map(conn => (
                  <DatabaseCard
                    key={conn.id}
                    connection={conn}
                    language={language}
                    onToggle={handleToggleConnection}
                    onDelete={(id, name) => setDeleteTarget({ id, name })}
                    onTest={handleTestConnection}
                    logs={logs}
                  />
                ))}
              </div>
            )}
          </div>
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
      {/* Export Connections Modal */}
      <ExportConnectionsModal
        isOpen={isExportModalOpen}
        onClose={() => setIsExportModalOpen(false)}
        connections={connections}
        language={language}
      />

      {/* Import Connections Modal */}
      <ImportConnectionsModal
        isOpen={isImportModalOpen}
        onClose={() => setIsImportModalOpen(false)}
        existingConnections={connections}
        language={language}
        onImportSuccess={handleImportSuccess}
      />

      {/* Toast Notification Banner */}
      {toastNotification && (
        <div className="fixed bottom-6 right-6 z-50 flex items-center gap-2.5 px-4 py-3 rounded-xl bg-stone-900 text-white shadow-2xl border border-stone-700 animate-in slide-in-from-bottom-3 duration-200">
          <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
          <span className="text-xs font-semibold">{toastNotification.message}</span>
          <button
            onClick={() => setToastNotification(null)}
            className="ml-2 p-1 text-stone-400 hover:text-white rounded transition-colors"
          >
            <X className="w-3.5 h-3.5" />
          </button>
        </div>
      )}
    </div>
  );
};

export default App;
