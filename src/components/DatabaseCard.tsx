import React, { useState, useMemo } from 'react';
import {
  Database,
  Server,
  User,
  Folder,
  Activity,
  Trash2,
  CheckCircle2,
  XCircle,
  Zap,
  Clock,
  RefreshCw,
  Terminal,
} from 'lucide-react';
import {
  ResponsiveContainer,
  BarChart,
  Bar,
  Tooltip,
  Cell,
} from 'recharts';
import type { DatabaseConfig, RequestLogItem } from '../types';
import { translations } from '../localization/translations';
import { DatabaseQueriesModal } from './DatabaseQueriesModal';

interface DatabaseCardProps {
  connection: DatabaseConfig;
  language: 'en' | 'ar';
  onToggle: (id: string) => Promise<void>;
  onEdit?: (conn: DatabaseConfig) => void;
  onDelete: (id: string, name: string) => void;
  onTest: (conn: DatabaseConfig) => Promise<{ success: boolean; latencyMs?: number; message?: string }>;
  logs?: RequestLogItem[];
}

function formatRelativeTime(dateStr: string | null | undefined, language: 'en' | 'ar', t: any): string {
  if (!dateStr) return t.healthNeverChecked;
  const date = new Date(dateStr);
  const now = new Date();
  const diffSec = Math.floor((now.getTime() - date.getTime()) / 1000);

  if (diffSec < 45) {
    return t.healthJustNow;
  }
  const diffMin = Math.floor(diffSec / 60);
  if (diffMin < 60) {
    return t.healthMinutesAgo.replace('{0}', String(diffMin));
  }
  const diffHours = Math.floor(diffMin / 60);
  if (diffHours < 24) {
    return t.healthHoursAgo.replace('{0}', String(diffHours));
  }
  const diffDays = Math.floor(diffHours / 24);
  return t.healthDaysAgo.replace('{0}', String(diffDays));
}

export const DatabaseCard: React.FC<DatabaseCardProps> = ({
  connection,
  language,
  onToggle,
  onEdit,
  onDelete,
  onTest,
  logs,
}) => {
  const t = translations[language];
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const [showQueriesModal, setShowQueriesModal] = useState(false);

  // Total queries belonging to this database across logs
  const dbQueriesCount = useMemo(() => {
    if (!logs || logs.length === 0) return 0;
    const connNameLower = connection.name.toLowerCase();
    const connIdLower = connection.id.toLowerCase();
    return logs.filter((log) => {
      if (!log.database) return false;
      const logDbLower = log.database.toLowerCase();
      return logDbLower === connNameLower || logDbLower === connIdLower;
    }).length;
  }, [logs, connection.name, connection.id]);

  // Calculate request activity for the last 10 minutes
  const { chartData, totalRecentRequests, peakRequestsPerMin } = useMemo(() => {
    const now = Date.now();
    const buckets: {
      minuteOffset: number;
      minuteLabel: string;
      time: string;
      requests: number;
      errorCount: number;
    }[] = [];

    // Create 10 buckets (from 9 minutes ago down to current minute 0)
    for (let i = 9; i >= 0; i--) {
      const bucketTime = new Date(now - i * 60000);
      const hours = bucketTime.getHours().toString().padStart(2, '0');
      const minutes = bucketTime.getMinutes().toString().padStart(2, '0');
      buckets.push({
        minuteOffset: i,
        minuteLabel: i === 0 ? (language === 'ar' ? 'الآن' : 'Now') : `-${i}m`,
        time: `${hours}:${minutes}`,
        requests: 0,
        errorCount: 0,
      });
    }

    if (logs && logs.length > 0) {
      const connNameLower = connection.name.toLowerCase();
      const connIdLower = connection.id.toLowerCase();

      for (const log of logs) {
        if (!log.database) continue;
        const logDbLower = log.database.toLowerCase();
        if (logDbLower === connNameLower || logDbLower === connIdLower) {
          const logTime = new Date(log.at).getTime();
          const diffMinutes = Math.floor((now - logTime) / 60000);
          if (diffMinutes >= 0 && diffMinutes < 10) {
            const bucketIndex = 9 - diffMinutes;
            if (buckets[bucketIndex]) {
              buckets[bucketIndex].requests += 1;
              if (log.status >= 400 || log.error) {
                buckets[bucketIndex].errorCount += 1;
              }
            }
          }
        }
      }
    }

    const total = buckets.reduce((acc, b) => acc + b.requests, 0);
    const peak = buckets.reduce((max, b) => Math.max(max, b.requests), 0);

    return {
      chartData: buckets,
      totalRecentRequests: total,
      peakRequestsPerMin: peak,
    };
  }, [logs, connection.name, connection.id, language]);

  const isHealthy = connection.lastTestSuccessful === true;
  const isFailing = connection.lastTestSuccessful === false;
  const isUnchecked = connection.lastTestSuccessful === undefined || connection.lastTestSuccessful === null;

  const dbType = connection.type || (
    connection.database.endsWith('.sqlite') || connection.database.endsWith('.db')
      ? 'SQLite'
      : connection.port === 5432
      ? 'PostgreSQL'
      : connection.port === 3306
      ? 'MySQL'
      : 'Firebird'
  );

  const handleRunTest = async () => {
    setTesting(true);
    setTestResult(null);
    try {
      const res = await onTest(connection);
      setTestResult({
        success: res.success,
        message: res.success
          ? t.connectionSuccessful.replace('{0}', String(res.latencyMs || 12))
          : t.connectionFailed.replace('{0}', res.message || 'Error'),
      });
      setTimeout(() => setTestResult(null), 4500);
    } finally {
      setTesting(false);
    }
  };

  const borderAccentClass = isHealthy
    ? (language === 'ar' ? 'border-r-4 border-r-emerald-500' : 'border-l-4 border-l-emerald-500')
    : isFailing
    ? (language === 'ar' ? 'border-r-4 border-r-rose-500' : 'border-l-4 border-l-rose-500')
    : (language === 'ar' ? 'border-r-4 border-r-amber-400' : 'border-l-4 border-l-amber-400');

  const cardBorderClass = isHealthy
    ? 'border-stone-300 ring-1 ring-emerald-500/20 hover:border-emerald-300'
    : isFailing
    ? 'border-rose-300 ring-1 ring-rose-500/20 hover:border-rose-400'
    : 'border-stone-200 hover:border-stone-300';

  return (
    <div
      className={`bg-white rounded-xl border transition-all p-5 shadow-xs relative overflow-hidden ${cardBorderClass} ${borderAccentClass} ${
        !connection.enabled ? 'opacity-90' : ''
      }`}
    >
      {/* Top row: Name, Online status, and Action buttons */}
      <div className="flex items-start justify-between gap-3 mb-3">
        <div className="flex items-center gap-2.5">
          <div
            className={`p-2 rounded-lg border transition-colors ${
              isHealthy
                ? 'bg-emerald-50 text-emerald-700 border-emerald-200'
                : isFailing
                ? 'bg-rose-50 text-rose-700 border-rose-200'
                : 'bg-stone-100 text-stone-500 border-stone-200'
            }`}
          >
            <Database className="w-5 h-5" />
          </div>
          <div>
            <div className="flex items-center gap-2 flex-wrap">
              <h3 className="text-base font-bold text-stone-900 leading-tight">
                {connection.name}
              </h3>

              <span className="inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-mono font-semibold bg-stone-100 text-stone-700 border border-stone-200">
                {dbType}
              </span>

              {/* Online/Offline status badge */}
              <span
                className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-semibold ${
                  connection.enabled
                    ? 'bg-emerald-100 text-emerald-800'
                    : 'bg-stone-100 text-stone-600'
                }`}
              >
                <span
                  className={`w-1.5 h-1.5 rounded-full ${
                    connection.enabled ? 'bg-emerald-500' : 'bg-stone-400'
                  }`}
                />
                {connection.enabled ? t.online : t.offline}
              </span>
            </div>
            <p className="text-[11px] text-stone-400 font-mono mt-0.5">ID: {connection.id}</p>
          </div>
        </div>

        {/* Action buttons */}
        <div className="flex items-center gap-1.5">
          <button
            onClick={() => onToggle(connection.id)}
            className={`px-3 py-1 text-xs font-semibold rounded-lg transition-colors shadow-2xs ${
              connection.enabled
                ? 'bg-emerald-600 text-white hover:bg-emerald-700'
                : 'bg-stone-100 text-stone-700 hover:bg-stone-200 border border-stone-300'
            }`}
          >
            {connection.enabled ? t.online : t.offline}
          </button>

          <button
            onClick={handleRunTest}
            disabled={testing}
            className="p-1.5 rounded-lg border border-stone-200 text-stone-600 hover:bg-stone-100 transition-colors shadow-2xs"
            title={t.testConnection}
          >
            <Activity className={`w-3.5 h-3.5 ${testing ? 'animate-spin text-emerald-600' : ''}`} />
          </button>

          {/* Queries (استعلاماتها) button — replaces Edit button */}
          <button
            onClick={() => setShowQueriesModal(true)}
            className="px-2.5 py-1 rounded-lg border border-stone-200 text-stone-700 hover:bg-stone-100 hover:text-stone-900 transition-colors shadow-2xs flex items-center gap-1.5 text-xs font-semibold"
            title={t.databaseQueries}
          >
            <Terminal className="w-3.5 h-3.5 text-stone-500" />
            <span>{t.databaseQueries}</span>
            {dbQueriesCount > 0 && (
              <span className="px-1.5 py-0.2 rounded-full text-[10px] font-mono font-bold bg-stone-200/80 text-stone-800">
                {dbQueriesCount}
              </span>
            )}
          </button>

          <button
            onClick={() => onDelete(connection.id, connection.name)}
            className="p-1.5 rounded-lg border border-stone-200 text-stone-400 hover:text-rose-600 hover:bg-rose-50 transition-colors shadow-2xs"
            title={t.delete}
          >
            <Trash2 className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      {/* Visual Health Indicator Bar */}
      <div
        className={`my-3 py-2 px-3 rounded-lg border flex flex-wrap items-center justify-between gap-2.5 transition-all ${
          isHealthy
            ? 'bg-emerald-50/80 border-emerald-200 text-emerald-900'
            : isFailing
            ? 'bg-rose-50/90 border-rose-200 text-rose-900'
            : 'bg-stone-50 border-stone-200 text-stone-700'
        }`}
      >
        {/* Left / Start: Status icon + label + latency / error */}
        <div className="flex items-center gap-2.5 min-w-0">
          <div className="relative flex items-center justify-center shrink-0">
            {isHealthy && (
              <>
                <span className="absolute w-3.5 h-3.5 rounded-full bg-emerald-400/50 animate-ping" />
                <span className="relative w-2.5 h-2.5 rounded-full bg-emerald-600 shadow-xs" />
              </>
            )}
            {isFailing && (
              <>
                <span className="absolute w-3.5 h-3.5 rounded-full bg-rose-400/50 animate-ping" />
                <span className="relative w-2.5 h-2.5 rounded-full bg-rose-600 shadow-xs" />
              </>
            )}
            {isUnchecked && <span className="w-2.5 h-2.5 rounded-full bg-amber-400" />}
          </div>

          <div className="flex items-center gap-2 flex-wrap">
            <span
              className={`text-xs font-bold tracking-wide flex items-center gap-1 ${
                isHealthy ? 'text-emerald-800' : isFailing ? 'text-rose-800' : 'text-stone-700'
              }`}
            >
              {isHealthy && <CheckCircle2 className="w-3.5 h-3.5 text-emerald-600 shrink-0" />}
              {isFailing && <XCircle className="w-3.5 h-3.5 text-rose-600 shrink-0" />}
              {isUnchecked && <Clock className="w-3.5 h-3.5 text-amber-500 shrink-0" />}
              <span>{isHealthy ? t.healthHealthy : isFailing ? t.healthFailing : t.healthUnchecked}</span>
            </span>

            {isHealthy && connection.lastLatencyMs !== undefined && (
              <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[11px] font-mono font-semibold bg-emerald-100 text-emerald-900 border border-emerald-300/70">
                <Zap className="w-3 h-3 text-emerald-600" />
                {connection.lastLatencyMs}ms
              </span>
            )}

            {isFailing && connection.lastErrorMessage && (
              <span
                className="text-[11px] text-rose-700 font-mono truncate max-w-xs sm:max-w-sm"
                title={connection.lastErrorMessage}
              >
                • {connection.lastErrorMessage}
              </span>
            )}
          </div>
        </div>

        {/* Right / End: Last tested timestamp + Quick Check Button */}
        <div className="flex items-center gap-2">
          <span
            className="text-[11px] text-stone-500 font-mono flex items-center gap-1"
            title={connection.lastTestedAt ? new Date(connection.lastTestedAt).toLocaleString() : undefined}
          >
            <Clock className="w-3 h-3 text-stone-400 shrink-0" />
            <span>{formatRelativeTime(connection.lastTestedAt, language, t)}</span>
          </span>

          <button
            onClick={handleRunTest}
            disabled={testing}
            className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-md text-[11px] font-semibold border transition-colors shadow-2xs ${
              isHealthy
                ? 'bg-white text-emerald-700 border-emerald-300 hover:bg-emerald-100/70'
                : isFailing
                ? 'bg-white text-rose-700 border-rose-300 hover:bg-rose-100/70'
                : 'bg-white text-stone-700 border-stone-300 hover:bg-stone-100'
            }`}
          >
            <RefreshCw className={`w-3 h-3 ${testing ? 'animate-spin' : ''}`} />
            <span>{testing ? t.healthChecking : t.healthCheckNow}</span>
          </button>
        </div>
      </div>

      {/* Metadata items */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-2 py-2 px-3 bg-stone-50 rounded-lg border border-stone-150 text-xs font-mono">
        <div className="flex items-center gap-1.5 text-stone-600 truncate">
          <Server className="w-3.5 h-3.5 text-stone-400 shrink-0" />
          <span className="truncate">
            {connection.server}:{connection.port}
          </span>
        </div>

        <div className="flex items-center gap-1.5 text-stone-600 truncate">
          <User className="w-3.5 h-3.5 text-stone-400 shrink-0" />
          <span className="truncate">{connection.username}</span>
        </div>

        <div className="flex items-center gap-1.5 text-stone-600 truncate">
          <Folder className="w-3.5 h-3.5 text-stone-400 shrink-0" />
          <span className="truncate" title={connection.database}>
            {connection.database}
          </span>
        </div>
      </div>

      {/* Connection test alert banner if active */}
      {testResult && (
        <div
          className={`mt-2.5 px-3 py-1.5 rounded-lg text-xs font-medium flex items-center gap-2 ${
            testResult.success
              ? 'bg-emerald-50 text-emerald-800 border border-emerald-200'
              : 'bg-rose-50 text-rose-800 border border-rose-200'
          }`}
        >
          {testResult.success ? (
            <CheckCircle2 className="w-4 h-4 text-emerald-600 shrink-0" />
          ) : (
            <XCircle className="w-4 h-4 text-rose-600 shrink-0" />
          )}
          <span>{testResult.message}</span>
        </div>
      )}

      {/* Card Footer: Last 10 Minutes Request Activity Sparkline / Bar Chart */}
      <div className="mt-3 pt-3 border-t border-stone-200/90 flex flex-col gap-2">
        <div className="flex items-center justify-between text-xs flex-wrap gap-2">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="font-semibold text-stone-700 flex items-center gap-1.5">
              <Activity className="w-3.5 h-3.5 text-stone-500" />
              <span>{t.activityLast10m}</span>
            </span>

            <span
              className={`px-2 py-0.5 rounded-full text-[11px] font-mono font-semibold border ${
                totalRecentRequests > 0
                  ? isHealthy
                    ? 'bg-emerald-50 text-emerald-800 border-emerald-200'
                    : isFailing
                    ? 'bg-rose-50 text-rose-800 border-rose-200'
                    : 'bg-stone-100 text-stone-800 border-stone-200'
                  : 'bg-stone-100 text-stone-500 border-stone-200'
              }`}
            >
              {t.activityRequests.replace('{0}', String(totalRecentRequests))}
            </span>

            {peakRequestsPerMin > 0 && (
              <span className="text-[11px] font-mono text-stone-400">
                • {t.activityPeak.replace('{0}', String(peakRequestsPerMin))}
              </span>
            )}
          </div>

          <div className="text-[11px] font-mono text-stone-500 flex items-center gap-1.5">
            {totalRecentRequests > 0 ? (
              <span className="inline-flex items-center gap-1 text-emerald-700 font-medium">
                <span className="relative flex h-2 w-2">
                  <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75" />
                  <span className="relative inline-flex rounded-full h-2 w-2 bg-emerald-500" />
                </span>
                <span>{t.activityActive}</span>
              </span>
            ) : (
              <span className="text-stone-400 italic">{t.activityIdle}</span>
            )}
          </div>
        </div>

        {/* Small Sparkline / Bar Chart using Recharts */}
        <div className="h-10 w-full bg-stone-50/90 rounded-md border border-stone-200/80 px-2 py-1">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart
              data={chartData}
              margin={{ top: 2, right: 1, left: 1, bottom: 0 }}
            >
              <Tooltip
                isAnimationActive={false}
                content={({ active, payload }) => {
                  if (active && payload && payload.length) {
                    const d = payload[0].payload;
                    return (
                      <div className="bg-stone-900/95 backdrop-blur-xs text-white text-[11px] font-mono px-2.5 py-1.5 rounded-md shadow-lg border border-stone-700 pointer-events-none">
                        <div className="text-stone-300 font-semibold flex items-center justify-between gap-3">
                          <span>{d.time}</span>
                          <span className="text-stone-400 text-[10px]">{d.minuteLabel}</span>
                        </div>
                        <div className="mt-0.5 text-emerald-400 font-bold">
                          {d.requests} {d.requests === 1 ? 'request' : 'requests'}
                          {d.errorCount > 0 && (
                            <span className="text-rose-400 ml-1.5">({d.errorCount} failed)</span>
                          )}
                        </div>
                      </div>
                    );
                  }
                  return null;
                }}
              />
              <Bar
                dataKey="requests"
                radius={[2, 2, 0, 0]}
                isAnimationActive={false}
                minPointSize={2}
              >
                {chartData.map((entry, index) => {
                  const hasErrors = entry.errorCount > 0;
                  const fillColor = entry.requests === 0
                    ? '#e2e8f0'
                    : hasErrors
                    ? '#f43f5e'
                    : isHealthy
                    ? '#10b981'
                    : isFailing
                    ? '#f43f5e'
                    : '#6366f1';
                  return (
                    <Cell
                      key={`cell-${index}`}
                      fill={fillColor}
                      fillOpacity={entry.requests === 0 ? 0.45 : 0.9}
                    />
                  );
                })}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        </div>

        {/* 10-Minute Timeline Labels */}
        <div className="flex items-center justify-between text-[10px] font-mono text-stone-400 px-1">
          <span>-9m</span>
          <span>-6m</span>
          <span>-3m</span>
          <span className="font-semibold text-stone-500">{language === 'ar' ? 'الآن' : 'Now'}</span>
        </div>
      </div>

      {/* Database Queries Modal (استعلاماتها) */}
      <DatabaseQueriesModal
        isOpen={showQueriesModal}
        connection={connection}
        language={language}
        logs={logs || []}
        onClose={() => setShowQueriesModal(false)}
      />
    </div>
  );
};
