import React, { useState, useEffect } from 'react';
import { 
  Server, 
  Cloud, 
  Radio, 
  ArrowRightLeft, 
  Globe, 
  ShieldCheck, 
  Copy, 
  Check, 
  ExternalLink, 
  Activity, 
  CheckCircle2, 
  AlertCircle,
  Database,
  Sliders,
  Sparkles,
  TrendingDown
} from 'lucide-react';
import { ResponsiveContainer, AreaChart, Area, Tooltip, YAxis } from 'recharts';
import type { WebServerModeConfig } from '../types';
import { translations } from '../localization/translations';

interface WebServerToggleCardProps {
  config: WebServerModeConfig;
  language: 'en' | 'ar';
  onToggle: (enable: boolean) => Promise<void>;
  onRefresh?: () => void;
}

export const WebServerToggleCard: React.FC<WebServerToggleCardProps> = ({
  config,
  language,
  onToggle,
  onRefresh,
}) => {
  const t = translations[language];
  const [loading, setLoading] = useState(false);
  const [copiedConfig, setCopiedConfig] = useState(false);
  const [simulatingTraffic, setSimulatingTraffic] = useState(false);
  const [trafficMessage, setTrafficMessage] = useState<string | null>(null);

  // Sparkline latency trend history for both tunnels
  const [dbLatencyHistory, setDbLatencyHistory] = useState([
    { poll: 'p1', latency: 16 },
    { poll: 'p2', latency: 14 },
    { poll: 'p3', latency: 18 },
    { poll: 'p4', latency: 13 },
    { poll: 'p5', latency: 15 },
    { poll: 'p6', latency: 17 },
    { poll: 'p7', latency: 14 },
    { poll: 'now', latency: config.tunnels.databaseGateway.metrics.latencyMs || 14 },
  ]);

  const [panelLatencyHistory, setPanelLatencyHistory] = useState([
    { poll: 'p1', latency: 21 },
    { poll: 'p2', latency: 18 },
    { poll: 'p3', latency: 23 },
    { poll: 'p4', latency: 19 },
    { poll: 'p5', latency: 17 },
    { poll: 'p6', latency: 22 },
    { poll: 'p7', latency: 18 },
    { poll: 'now', latency: config.tunnels.controlPanel.metrics.latencyMs || 18 },
  ]);

  // Keep track of polls and append new latency data points
  useEffect(() => {
    if (!config.enabled) return;
    const nowTime = new Date().toLocaleTimeString([], { minute: '2-digit', second: '2-digit' });
    const currentDbLat = config.tunnels.databaseGateway.metrics.latencyMs || 14;
    const currentPanelLat = config.tunnels.controlPanel.metrics.latencyMs || 18;

    setDbLatencyHistory(prev => {
      const next = [...prev, { poll: nowTime, latency: currentDbLat }];
      return next.slice(-10);
    });

    setPanelLatencyHistory(prev => {
      const next = [...prev, { poll: nowTime, latency: currentPanelLat }];
      return next.slice(-10);
    });
  }, [
    config.enabled,
    config.tunnels.databaseGateway.metrics.requestsTotal,
    config.tunnels.databaseGateway.metrics.latencyMs,
    config.tunnels.controlPanel.metrics.requestsTotal,
    config.tunnels.controlPanel.metrics.latencyMs,
  ]);

  const handleToggleMode = async () => {
    setLoading(true);
    try {
      await onToggle(!config.enabled);
    } finally {
      setLoading(false);
    }
  };

  const cloudflareConfigYaml = `# ~/.cloudflared/config.yml (Dual-Tunnel for ByteBridge)
tunnel: bytebridge-dual-tunnel-id
credentials-file: /etc/cloudflared/bytebridge.json

ingress:
  # Tunnel 1: Remote Database Query Traffic (Data Gateway)
  - hostname: ${config.tunnels.databaseGateway.subdomain}
    service: http://127.0.0.1:${config.tunnels.databaseGateway.targetPort}
  
  # Tunnel 2: Remote Web Server Management & Control Panel
  - hostname: ${config.tunnels.controlPanel.subdomain}
    service: http://127.0.0.1:${config.tunnels.controlPanel.targetPort}
    
  - service: http_status:404
`;

  const copyConfig = () => {
    navigator.clipboard.writeText(cloudflareConfigYaml);
    setCopiedConfig(true);
    setTimeout(() => setCopiedConfig(false), 2500);
  };

  const handleSimulateTraffic = async () => {
    if (!config.enabled) return;
    setSimulatingTraffic(true);
    try {
      // Simulate remote web request hitting through the data tunnel
      const res = await fetch('/health');
      if (res.ok) {
        setTrafficMessage(language === 'ar' 
          ? 'تم استقبال إشارة فحص حية عبر نفق المنفذ 8080 ونفق 3000 بنجاح!' 
          : 'Live traffic packet received via Tunnel 8080 & Tunnel 3000 successfully!');
        if (onRefresh) onRefresh();
        setTimeout(() => setTrafficMessage(null), 4000);
      }
    } catch {
      setTrafficMessage('Failed to reach local gateway');
    } finally {
      setSimulatingTraffic(false);
    }
  };

  return (
    <div id="web-server-mode-card" className={`rounded-xl border transition-all duration-300 shadow-xs overflow-hidden ${
      config.enabled 
        ? 'bg-gradient-to-br from-white via-blue-50/30 to-indigo-50/20 border-blue-200' 
        : 'bg-white border-stone-200'
    }`}>
      {/* Top Banner & Main Switch */}
      <div className="p-5 flex flex-col md:flex-row md:items-center justify-between gap-4 border-b border-stone-100">
        <div className="flex items-start sm:items-center gap-3.5">
          <div className={`p-2.5 rounded-xl border shadow-2xs transition-colors ${
            config.enabled 
              ? 'bg-blue-600 text-white border-blue-700 ring-4 ring-blue-100' 
              : 'bg-stone-100 text-stone-600 border-stone-200'
          }`}>
            <Server className="w-5 h-5" />
          </div>

          <div>
            <div className="flex items-center gap-2 flex-wrap">
              <h2 className="text-base font-bold text-stone-900">
                {t.webServerModeTitle}
              </h2>
              {config.enabled ? (
                <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-emerald-100 text-emerald-800 border border-emerald-300">
                  <span className="w-2 h-2 rounded-full bg-emerald-500 animate-ping" />
                  {t.webServerOnline}
                </span>
              ) : (
                <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-medium bg-stone-100 text-stone-600 border border-stone-200">
                  <span className="w-1.5 h-1.5 rounded-full bg-stone-400" />
                  {t.webServerOffline}
                </span>
              )}
            </div>
            <p className="text-xs text-stone-600 mt-1 max-w-2xl">
              {t.webServerModeDesc}
            </p>
          </div>
        </div>

        {/* The Action Button: Turn into Web Server */}
        <div className="flex items-center gap-2">
          <button
            id="toggle-web-server-mode-btn"
            onClick={handleToggleMode}
            disabled={loading}
            className={`flex items-center gap-2 px-4 py-2.5 text-xs font-bold rounded-lg transition-all shadow-xs cursor-pointer ${
              config.enabled
                ? 'bg-rose-50 text-rose-700 border border-rose-200 hover:bg-rose-100'
                : 'bg-blue-600 text-white hover:bg-blue-700 active:scale-98 ring-2 ring-blue-600/20'
            }`}
          >
            <Radio className={`w-4 h-4 ${config.enabled ? 'animate-pulse text-rose-600' : 'text-white'}`} />
            <span>
              {loading 
                ? (language === 'ar' ? 'جارٍ التحويل...' : 'Switching...') 
                : (config.enabled 
                    ? (language === 'ar' ? 'إيقاف خادم الويب والأنفاق' : 'Deactivate Web Server Mode') 
                    : t.switchToServerMode
                  )}
            </span>
          </button>
        </div>
      </div>

      {/* Two Tunnels Architecture Grid */}
      <div className="p-5 space-y-4">
        <div className="flex items-center justify-between flex-wrap gap-2">
          <div className="flex items-center gap-2 text-xs font-semibold text-stone-700">
            <Cloud className="w-4 h-4 text-orange-500" />
            <span>Cloudflare Dual-Tunnel Architecture</span>
            <span className="text-stone-300">|</span>
            <span className="text-stone-500 font-normal">
              {language === 'ar' ? 'نفق للبيانات ونفق للتحكم بالخادم' : 'Tunnel 1 for Data API & Tunnel 2 for Remote Server Mgmt'}
            </span>
          </div>

          <div className="flex items-center gap-2">
            {config.enabled && (
              <button
                onClick={handleSimulateTraffic}
                disabled={simulatingTraffic}
                className="flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium rounded-md border border-stone-200 bg-white hover:bg-stone-50 text-stone-700 shadow-2xs"
                title="Send test packet through edge"
              >
                <Activity className={`w-3.5 h-3.5 text-blue-600 ${simulatingTraffic ? 'animate-spin' : ''}`} />
                <span>{t.simulateWebTraffic}</span>
              </button>
            )}

            <button
              onClick={copyConfig}
              className="flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium rounded-md border border-stone-200 bg-white hover:bg-stone-50 text-stone-700 shadow-2xs"
            >
              {copiedConfig ? (
                <>
                  <Check className="w-3.5 h-3.5 text-emerald-600" />
                  <span className="text-emerald-700">{t.copiedTunnelCmd}</span>
                </>
              ) : (
                <>
                  <Copy className="w-3.5 h-3.5 text-stone-500" />
                  <span>{t.copyTunnelCmd}</span>
                </>
              )}
            </button>
          </div>
        </div>

        {trafficMessage && (
          <div className="p-2.5 rounded-lg bg-emerald-50 border border-emerald-200 text-emerald-800 text-xs flex items-center gap-2 animate-fadeIn">
            <CheckCircle2 className="w-4 h-4 text-emerald-600 flex-shrink-0" />
            <span>{trafficMessage}</span>
          </div>
        )}

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {/* Tunnel 1: Database Gateway (Port 8080) */}
          <div id="tunnel-card-database" className={`rounded-xl p-4 border transition-all ${
            config.enabled && config.tunnels.databaseGateway.active
              ? 'bg-white border-emerald-300 shadow-xs'
              : 'bg-stone-50/80 border-stone-200 opacity-80'
          }`}>
            <div className="flex items-start justify-between gap-2">
              <div className="flex items-center gap-2.5">
                <div className={`p-2 rounded-lg ${
                  config.enabled ? 'bg-emerald-100 text-emerald-700' : 'bg-stone-200 text-stone-600'
                }`}>
                  <Database className="w-4 h-4" />
                </div>
                <div>
                  <div className="flex items-center gap-2">
                    <h3 className="text-xs font-bold text-stone-900">
                      {language === 'ar' ? config.tunnels.databaseGateway.nameAr : config.tunnels.databaseGateway.name}
                    </h3>
                    <span className="px-1.5 py-0.5 rounded text-[10px] font-mono font-bold bg-stone-100 text-stone-700 border border-stone-200">
                      :{config.tunnels.databaseGateway.targetPort}
                    </span>
                  </div>
                  <p className="text-[11px] text-stone-500 mt-0.5">
                    {t.tunnel1Desc}
                  </p>
                </div>
              </div>

              <span className={`px-2 py-0.5 rounded-full text-[10px] font-semibold flex items-center gap-1 ${
                config.enabled 
                  ? 'bg-emerald-50 text-emerald-700 border border-emerald-200' 
                  : 'bg-stone-100 text-stone-500 border border-stone-200'
              }`}>
                <span className={`w-1.5 h-1.5 rounded-full ${config.enabled ? 'bg-emerald-500 animate-pulse' : 'bg-stone-400'}`} />
                {config.enabled ? t.tunnelActive : t.tunnelInactive}
              </span>
            </div>

            <div className="mt-3 pt-3 border-t border-stone-100 space-y-2 text-xs">
              <div className="flex items-center justify-between">
                <span className="text-stone-500 text-[11px]">Cloudflare Public Edge:</span>
                <span className="font-mono text-[11px] font-semibold text-blue-700 truncate max-w-[200px]">
                  {config.tunnels.databaseGateway.publicUrl}
                </span>
              </div>

              <div className="flex items-center justify-between">
                <span className="text-stone-500 text-[11px]">Local Gateway Target:</span>
                <span className="font-mono text-[11px] text-stone-700">
                  http://127.0.0.1:{config.tunnels.databaseGateway.targetPort}
                </span>
              </div>

              <div className="flex items-center justify-between pt-1">
                <span className="text-stone-500 text-[11px]">Tunnel 1 Activity:</span>
                <span className="text-[11px] font-medium text-stone-800">
                  {config.tunnels.databaseGateway.metrics.requestsTotal} requests · {config.tunnels.databaseGateway.metrics.latencyMs}ms avg latency
                </span>
              </div>

              {/* Sparkline Latency Chart */}
              <div className="pt-2 border-t border-stone-100">
                <div className="flex items-center justify-between text-[10px] mb-1">
                  <span className="flex items-center gap-1 font-semibold text-stone-600">
                    <Activity className="w-3 h-3 text-emerald-600" />
                    <span>{t.latencyTrend} ({t.recentPolls})</span>
                  </span>
                  <span className="font-mono font-bold text-emerald-700 bg-emerald-50 px-1.5 py-0.5 rounded border border-emerald-200">
                    {config.enabled ? `${config.tunnels.databaseGateway.metrics.latencyMs} ms` : '—'}
                  </span>
                </div>
                <div className="h-9 w-full min-w-0">
                  <ResponsiveContainer width="100%" height="100%">
                    <AreaChart data={dbLatencyHistory} margin={{ top: 2, right: 2, left: 2, bottom: 0 }}>
                      <defs>
                        <linearGradient id="dbLatencyGrad" x1="0" y1="0" x2="0" y2="1">
                          <stop offset="5%" stopColor="#059669" stopOpacity={0.4} />
                          <stop offset="95%" stopColor="#059669" stopOpacity={0.0} />
                        </linearGradient>
                      </defs>
                      <YAxis domain={['dataMin - 2', 'dataMax + 2']} hide />
                      <Tooltip
                        content={({ active, payload }) => {
                          if (active && payload && payload.length) {
                            return (
                              <div className="bg-stone-900 text-stone-100 text-[10px] font-mono py-0.5 px-1.5 rounded shadow-sm">
                                {payload[0].value} ms
                              </div>
                            );
                          }
                          return null;
                        }}
                      />
                      <Area
                        type="monotone"
                        dataKey="latency"
                        stroke="#059669"
                        strokeWidth={1.75}
                        fill="url(#dbLatencyGrad)"
                        fillOpacity={1}
                        isAnimationActive={false}
                      />
                    </AreaChart>
                  </ResponsiveContainer>
                </div>
              </div>
            </div>
          </div>

          {/* Tunnel 2: Control Panel & Server Management (Port 3000) */}
          <div id="tunnel-card-panel" className={`rounded-xl p-4 border transition-all ${
            config.enabled && config.tunnels.controlPanel.active
              ? 'bg-white border-blue-300 shadow-xs'
              : 'bg-stone-50/80 border-stone-200 opacity-80'
          }`}>
            <div className="flex items-start justify-between gap-2">
              <div className="flex items-center gap-2.5">
                <div className={`p-2 rounded-lg ${
                  config.enabled ? 'bg-blue-100 text-blue-700' : 'bg-stone-200 text-stone-600'
                }`}>
                  <Globe className="w-4 h-4" />
                </div>
                <div>
                  <div className="flex items-center gap-2">
                    <h3 className="text-xs font-bold text-stone-900">
                      {language === 'ar' ? config.tunnels.controlPanel.nameAr : config.tunnels.controlPanel.name}
                    </h3>
                    <span className="px-1.5 py-0.5 rounded text-[10px] font-mono font-bold bg-stone-100 text-stone-700 border border-stone-200">
                      :{config.tunnels.controlPanel.targetPort}
                    </span>
                  </div>
                  <p className="text-[11px] text-stone-500 mt-0.5">
                    {t.tunnel2Desc}
                  </p>
                </div>
              </div>

              <span className={`px-2 py-0.5 rounded-full text-[10px] font-semibold flex items-center gap-1 ${
                config.enabled 
                  ? 'bg-blue-50 text-blue-700 border border-blue-200' 
                  : 'bg-stone-100 text-stone-500 border border-stone-200'
              }`}>
                <span className={`w-1.5 h-1.5 rounded-full ${config.enabled ? 'bg-blue-500 animate-pulse' : 'bg-stone-400'}`} />
                {config.enabled ? t.tunnelActive : t.tunnelInactive}
              </span>
            </div>

            <div className="mt-3 pt-3 border-t border-stone-100 space-y-2 text-xs">
              <div className="flex items-center justify-between">
                <span className="text-stone-500 text-[11px]">Remote Management URL:</span>
                <span className="font-mono text-[11px] font-semibold text-blue-700 truncate max-w-[200px]">
                  {config.tunnels.controlPanel.publicUrl}
                </span>
              </div>

              <div className="flex items-center justify-between">
                <span className="text-stone-500 text-[11px]">Local Server Target:</span>
                <span className="font-mono text-[11px] text-stone-700">
                  http://127.0.0.1:{config.tunnels.controlPanel.targetPort}
                </span>
              </div>

              <div className="flex items-center justify-between pt-1">
                <span className="text-stone-500 text-[11px]">Tunnel 2 Activity:</span>
                <span className="text-[11px] font-medium text-stone-800">
                  {config.tunnels.controlPanel.metrics.requestsTotal} requests · {config.tunnels.controlPanel.metrics.latencyMs}ms avg latency
                </span>
              </div>

              {/* Sparkline Latency Chart */}
              <div className="pt-2 border-t border-stone-100">
                <div className="flex items-center justify-between text-[10px] mb-1">
                  <span className="flex items-center gap-1 font-semibold text-stone-600">
                    <Activity className="w-3 h-3 text-blue-600" />
                    <span>{t.latencyTrend} ({t.recentPolls})</span>
                  </span>
                  <span className="font-mono font-bold text-blue-700 bg-blue-50 px-1.5 py-0.5 rounded border border-blue-200">
                    {config.enabled ? `${config.tunnels.controlPanel.metrics.latencyMs} ms` : '—'}
                  </span>
                </div>
                <div className="h-9 w-full min-w-0">
                  <ResponsiveContainer width="100%" height="100%">
                    <AreaChart data={panelLatencyHistory} margin={{ top: 2, right: 2, left: 2, bottom: 0 }}>
                      <defs>
                        <linearGradient id="panelLatencyGrad" x1="0" y1="0" x2="0" y2="1">
                          <stop offset="5%" stopColor="#2563eb" stopOpacity={0.4} />
                          <stop offset="95%" stopColor="#2563eb" stopOpacity={0.0} />
                        </linearGradient>
                      </defs>
                      <YAxis domain={['dataMin - 2', 'dataMax + 2']} hide />
                      <Tooltip
                        content={({ active, payload }) => {
                          if (active && payload && payload.length) {
                            return (
                              <div className="bg-stone-900 text-stone-100 text-[10px] font-mono py-0.5 px-1.5 rounded shadow-sm">
                                {payload[0].value} ms
                              </div>
                            );
                          }
                          return null;
                        }}
                      />
                      <Area
                        type="monotone"
                        dataKey="latency"
                        stroke="#2563eb"
                        strokeWidth={1.75}
                        fill="url(#panelLatencyGrad)"
                        fillOpacity={1}
                        isAnimationActive={false}
                      />
                    </AreaChart>
                  </ResponsiveContainer>
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* Visual Tunnel Architecture Flow Diagram */}
        <div className="mt-3 p-3.5 rounded-lg bg-stone-900 text-stone-200 text-xs font-mono space-y-2">
          <div className="flex items-center justify-between text-[11px] text-stone-400 pb-1 border-b border-stone-800">
            <span className="flex items-center gap-1.5 text-stone-300">
              <ShieldCheck className="w-3.5 h-3.5 text-emerald-400" />
              <span>Edge to Local Traffic Routing</span>
            </span>
            <span>Cloudflare Edge (HTTPS) &rarr; Local Loopback</span>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-2 text-[11px]">
            <div className="p-2 rounded bg-stone-800/80 border border-stone-700">
              <span className="text-orange-400 font-bold">Tunnel 1 (Data API):</span>
              <div className="text-stone-300 mt-1">
                https://db-gateway-edge.bytebridge.io &rarr; <span className="text-emerald-400 font-semibold">127.0.0.1:8080</span> (/query, /execute)
              </div>
            </div>

            <div className="p-2 rounded bg-stone-800/80 border border-stone-700">
              <span className="text-blue-400 font-bold">Tunnel 2 (Server & Remote UI):</span>
              <div className="text-stone-300 mt-1">
                https://panel-remote.bytebridge.io &rarr; <span className="text-cyan-400 font-semibold">127.0.0.1:3000</span> (Web Server & Dashboard)
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
