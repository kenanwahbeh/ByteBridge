import React, { useState, useMemo } from 'react';
import {
  ResponsiveContainer,
  AreaChart,
  Area,
  LineChart,
  Line,
  XAxis,
  YAxis,
  Tooltip,
  CartesianGrid,
} from 'recharts';
import {
  Activity,
  Zap,
  TrendingUp,
  CheckCircle2,
  AlertCircle,
  Clock,
  BarChart2,
  Calendar,
} from 'lucide-react';
import type { DatabaseConfig, RequestLogItem } from '../types';
import { translations } from '../localization/translations';

interface ConnectionActivityChartProps {
  connections: DatabaseConfig[];
  logs: RequestLogItem[];
  language: 'en' | 'ar';
}

type TimeRange = '15m' | '1h' | '24h';
type ChartView = 'volume' | 'latency';

interface TimelineBucket {
  timeLabel: string;
  timestamp: number;
  successfulQueries: number;
  errorQueries: number;
  healthChecks: number;
  totalEvents: number;
  avgLatency: number;
  [dbName: string]: any;
}

export const ConnectionActivityChart: React.FC<ConnectionActivityChartProps> = ({
  connections,
  logs,
  language,
}) => {
  const t = translations[language];
  const [timeRange, setTimeRange] = useState<TimeRange>('1h');
  const [chartView, setChartView] = useState<ChartView>('volume');

  // Compute time buckets based on selected range
  const { timelineData, summaryStats } = useMemo(() => {
    const now = Date.now();
    let durationMs = 60 * 60 * 1000; // 1 hour default
    let bucketCount = 12; // 5-minute buckets for 1 hour

    if (timeRange === '15m') {
      durationMs = 15 * 60 * 1000;
      bucketCount = 15; // 1-minute buckets
    } else if (timeRange === '24h') {
      durationMs = 24 * 60 * 60 * 1000;
      bucketCount = 24; // 1-hour buckets
    }

    const bucketDurationMs = durationMs / bucketCount;
    const startTime = now - durationMs;

    // Initialize empty buckets
    const buckets: TimelineBucket[] = [];
    for (let i = 0; i < bucketCount; i++) {
      const bucketStart = startTime + i * bucketDurationMs;
      const bucketTime = new Date(bucketStart + bucketDurationMs / 2);
      
      let timeLabel = '';
      if (timeRange === '24h') {
        timeLabel = bucketTime.toLocaleTimeString(language === 'ar' ? 'ar-SA' : 'en-US', {
          hour: '2-digit',
          minute: '2-digit',
          hour12: false,
        });
      } else {
        timeLabel = bucketTime.toLocaleTimeString(language === 'ar' ? 'ar-SA' : 'en-US', {
          hour: '2-digit',
          minute: '2-digit',
          hour12: false,
        });
      }

      buckets.push({
        timeLabel,
        timestamp: bucketStart,
        successfulQueries: 0,
        errorQueries: 0,
        healthChecks: 0,
        totalEvents: 0,
        avgLatency: 0,
        _latencies: [],
      } as any);
    }

    // 1. Ingest query logs into corresponding buckets
    if (logs && logs.length > 0) {
      for (const log of logs) {
        const logTime = new Date(log.at).getTime();
        if (logTime >= startTime && logTime <= now) {
          const bucketIndex = Math.min(
            bucketCount - 1,
            Math.max(0, Math.floor((logTime - startTime) / bucketDurationMs))
          );
          const bucket = buckets[bucketIndex];
          if (bucket) {
            const isError = log.status >= 400 || !!log.error;
            if (isError) {
              bucket.errorQueries += 1;
            } else {
              bucket.successfulQueries += 1;
            }
            bucket.totalEvents += 1;
            if (typeof log.elapsedMs === 'number' && log.elapsedMs > 0) {
              (bucket as any)._latencies.push(log.elapsedMs);
            }
          }
        }
      }
    }

    // 2. Ingest connection lastTestedAt timestamps into corresponding buckets
    if (connections && connections.length > 0) {
      for (const conn of connections) {
        if (conn.lastTestedAt) {
          const testTime = new Date(conn.lastTestedAt).getTime();
          if (testTime >= startTime && testTime <= now) {
            const bucketIndex = Math.min(
              bucketCount - 1,
              Math.max(0, Math.floor((testTime - startTime) / bucketDurationMs))
            );
            const bucket = buckets[bucketIndex];
            if (bucket) {
              bucket.healthChecks += 1;
              bucket.totalEvents += 1;
              if (typeof conn.lastLatencyMs === 'number' && conn.lastLatencyMs > 0) {
                (bucket as any)._latencies.push(conn.lastLatencyMs);
              }
            }
          }
        }
      }
    }

    // Finalize average latency for each bucket
    let globalTotalEvents = 0;
    let globalSuccessCount = 0;
    let globalErrorCount = 0;
    const allLatencies: number[] = [];
    let peakBucketEvents = 0;

    for (const b of buckets) {
      const latList = (b as any)._latencies || [];
      if (latList.length > 0) {
        const sum = latList.reduce((acc: number, val: number) => acc + val, 0);
        b.avgLatency = Math.round(sum / latList.length);
        allLatencies.push(...latList);
      } else {
        b.avgLatency = 0;
      }
      delete (b as any)._latencies;

      globalTotalEvents += b.totalEvents;
      globalSuccessCount += b.successfulQueries;
      globalErrorCount += b.errorQueries;
      if (b.totalEvents > peakBucketEvents) {
        peakBucketEvents = b.totalEvents;
      }
    }

    const overallAvgLatency =
      allLatencies.length > 0
        ? Math.round(allLatencies.reduce((a, b) => a + b, 0) / allLatencies.length)
        : 12;

    const successRate =
      globalTotalEvents > 0
        ? Math.round(((globalSuccessCount + buckets.reduce((s, b) => s + b.healthChecks, 0)) / Math.max(1, globalTotalEvents)) * 100)
        : 100;

    return {
      timelineData: buckets,
      summaryStats: {
        totalEvents: globalTotalEvents,
        successfulQueries: globalSuccessCount,
        errorQueries: globalErrorCount,
        overallAvgLatency,
        peakBucketEvents,
        successRate,
      },
    };
  }, [logs, connections, timeRange, language]);

  return (
    <div className="bg-white rounded-xl border border-stone-200 p-4 sm:p-5 shadow-xs space-y-4">
      {/* Header and Controls */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-3 border-b border-stone-100">
        <div className="flex items-center gap-2.5">
          <div className="p-2 rounded-lg bg-stone-900 text-white shadow-2xs">
            <Activity className="w-4 h-4 text-emerald-400" />
          </div>
          <div>
            <h3 className="text-sm font-bold text-stone-900 leading-tight">
              {t.activityChartTitle}
            </h3>
            <p className="text-[11px] text-stone-500 mt-0.5">
              {t.activityChartSubtitle}
            </p>
          </div>
        </div>

        {/* View Switcher and Range Selectors */}
        <div className="flex items-center gap-2 flex-wrap self-start sm:self-auto">
          {/* Metric Selector (Volume vs Latency) */}
          <div className="flex items-center p-0.5 rounded-lg bg-stone-100 border border-stone-200/60 text-xs font-semibold">
            <button
              onClick={() => setChartView('volume')}
              className={`px-2.5 py-1 rounded-md transition-all ${
                chartView === 'volume'
                  ? 'bg-white text-stone-900 shadow-2xs'
                  : 'text-stone-500 hover:text-stone-800'
              }`}
            >
              {t.chartViewRequests}
            </button>
            <button
              onClick={() => setChartView('latency')}
              className={`px-2.5 py-1 rounded-md transition-all ${
                chartView === 'latency'
                  ? 'bg-white text-stone-900 shadow-2xs'
                  : 'text-stone-500 hover:text-stone-800'
              }`}
            >
              {t.chartViewLatency}
            </button>
          </div>

          {/* Time Range Selector */}
          <div className="flex items-center p-0.5 rounded-lg bg-stone-100 border border-stone-200/60 text-xs font-semibold">
            <button
              onClick={() => setTimeRange('15m')}
              className={`px-2 py-1 rounded-md transition-all ${
                timeRange === '15m'
                  ? 'bg-white text-stone-900 shadow-2xs'
                  : 'text-stone-500 hover:text-stone-800'
              }`}
            >
              {t.timeRange15m}
            </button>
            <button
              onClick={() => setTimeRange('1h')}
              className={`px-2 py-1 rounded-md transition-all ${
                timeRange === '1h'
                  ? 'bg-white text-stone-900 shadow-2xs'
                  : 'text-stone-500 hover:text-stone-800'
              }`}
            >
              {t.timeRange1h}
            </button>
            <button
              onClick={() => setTimeRange('24h')}
              className={`px-2 py-1 rounded-md transition-all ${
                timeRange === '24h'
                  ? 'bg-white text-stone-900 shadow-2xs'
                  : 'text-stone-500 hover:text-stone-800'
              }`}
            >
              {t.timeRange24h}
            </button>
          </div>
        </div>
      </div>

      {/* Metrics Row */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 text-xs">
        <div className="p-2.5 rounded-lg bg-stone-50 border border-stone-100">
          <span className="text-[11px] text-stone-500 block">{t.chartTotalEvents}</span>
          <div className="flex items-baseline gap-1 mt-0.5">
            <span className="text-base font-bold font-mono text-stone-900">
              {summaryStats.totalEvents}
            </span>
            <span className="text-[10px] text-stone-400">events</span>
          </div>
        </div>

        <div className="p-2.5 rounded-lg bg-stone-50 border border-stone-100">
          <span className="text-[11px] text-stone-500 block">{t.chartAvgLatency}</span>
          <div className="flex items-baseline gap-1 mt-0.5">
            <span className="text-base font-bold font-mono text-stone-900">
              {summaryStats.overallAvgLatency}
            </span>
            <span className="text-[10px] text-stone-400">ms</span>
          </div>
        </div>

        <div className="p-2.5 rounded-lg bg-stone-50 border border-stone-100">
          <span className="text-[11px] text-stone-500 block">{t.chartPeakTraffic}</span>
          <div className="flex items-baseline gap-1 mt-0.5">
            <span className="text-base font-bold font-mono text-stone-900">
              {summaryStats.peakBucketEvents}
            </span>
            <span className="text-[10px] text-stone-400">reqs/period</span>
          </div>
        </div>

        <div className="p-2.5 rounded-lg bg-stone-50 border border-stone-100">
          <span className="text-[11px] text-stone-500 block">{t.operationalStatus}</span>
          <div className="flex items-baseline gap-1.5 mt-0.5">
            <span className="text-base font-bold font-mono text-emerald-600">
              {summaryStats.successRate}%
            </span>
            <span className="w-1.5 h-1.5 rounded-full bg-emerald-500" />
          </div>
        </div>
      </div>

      {/* Main Chart Canvas */}
      <div className="h-[210px] sm:h-[230px] w-full pt-1">
        <ResponsiveContainer width="100%" height="100%">
          {chartView === 'volume' ? (
            <AreaChart
              data={timelineData}
              margin={{ top: 10, right: 10, left: -20, bottom: 0 }}
            >
              <defs>
                <linearGradient id="colorQueries" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="#10b981" stopOpacity={0.4} />
                  <stop offset="95%" stopColor="#10b981" stopOpacity={0.0} />
                </linearGradient>
                <linearGradient id="colorHealth" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="#3b82f6" stopOpacity={0.4} />
                  <stop offset="95%" stopColor="#3b82f6" stopOpacity={0.0} />
                </linearGradient>
                <linearGradient id="colorErrors" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="#ef4444" stopOpacity={0.5} />
                  <stop offset="95%" stopColor="#ef4444" stopOpacity={0.0} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e7e5e4" />
              <XAxis
                dataKey="timeLabel"
                tick={{ fontSize: 10, fill: '#78716c', fontFamily: 'monospace' }}
                stroke="#d6d3d1"
                tickLine={false}
              />
              <YAxis
                allowDecimals={false}
                tick={{ fontSize: 10, fill: '#78716c', fontFamily: 'monospace' }}
                stroke="#d6d3d1"
                tickLine={false}
              />
              <Tooltip content={<CustomVolumeTooltip language={language} t={t} />} />
              <Area
                type="monotone"
                dataKey="successfulQueries"
                name={t.chartSuccessfulQueries}
                stroke="#10b981"
                strokeWidth={2}
                fillOpacity={1}
                fill="url(#colorQueries)"
              />
              <Area
                type="monotone"
                dataKey="healthChecks"
                name={t.chartHealthChecks}
                stroke="#3b82f6"
                strokeWidth={2}
                fillOpacity={1}
                fill="url(#colorHealth)"
              />
              <Area
                type="monotone"
                dataKey="errorQueries"
                name={t.chartErrorsRefused}
                stroke="#ef4444"
                strokeWidth={2}
                fillOpacity={1}
                fill="url(#colorErrors)"
              />
            </AreaChart>
          ) : (
            <LineChart
              data={timelineData}
              margin={{ top: 10, right: 10, left: -20, bottom: 0 }}
            >
              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e7e5e4" />
              <XAxis
                dataKey="timeLabel"
                tick={{ fontSize: 10, fill: '#78716c', fontFamily: 'monospace' }}
                stroke="#d6d3d1"
                tickLine={false}
              />
              <YAxis
                unit="ms"
                tick={{ fontSize: 10, fill: '#78716c', fontFamily: 'monospace' }}
                stroke="#d6d3d1"
                tickLine={false}
              />
              <Tooltip content={<CustomLatencyTooltip language={language} t={t} />} />
              <Line
                type="monotone"
                dataKey="avgLatency"
                name={t.chartAvgLatency}
                stroke="#0284c7"
                strokeWidth={2.5}
                dot={{ r: 3, fill: '#0284c7', strokeWidth: 1, stroke: '#fff' }}
                activeDot={{ r: 5, fill: '#0369a1' }}
              />
            </LineChart>
          )}
        </ResponsiveContainer>
      </div>

      {/* Legend Footer */}
      <div className="flex items-center justify-between text-[11px] text-stone-500 pt-2 border-t border-stone-100 flex-wrap gap-2">
        <div className="flex items-center gap-4 flex-wrap">
          <div className="flex items-center gap-1.5">
            <span className="w-2.5 h-2.5 rounded-full bg-emerald-500" />
            <span>{t.chartSuccessfulQueries}</span>
          </div>
          <div className="flex items-center gap-1.5">
            <span className="w-2.5 h-2.5 rounded-full bg-blue-500" />
            <span>{t.chartHealthChecks}</span>
          </div>
          <div className="flex items-center gap-1.5">
            <span className="w-2.5 h-2.5 rounded-full bg-rose-500" />
            <span>{t.chartErrorsRefused}</span>
          </div>
        </div>

        <div className="font-mono text-[10px] text-stone-400">
          Recharts • {connections.length} databases monitored
        </div>
      </div>
    </div>
  );
};

// Custom Tooltip for Request / Event Volume View
const CustomVolumeTooltip = ({ active, payload, label, language, t }: any) => {
  if (!active || !payload || payload.length === 0) return null;
  const data = payload[0]?.payload as TimelineBucket;
  if (!data) return null;

  return (
    <div className="bg-stone-900 text-stone-100 rounded-lg p-3 shadow-xl border border-stone-700 text-xs font-mono space-y-1.5 min-w-[170px] z-50">
      <div className="flex items-center justify-between text-[11px] text-stone-400 pb-1 border-b border-stone-800">
        <span className="flex items-center gap-1">
          <Clock className="w-3 h-3 text-stone-400" />
          {data.timeLabel}
        </span>
        <span>{data.totalEvents} total</span>
      </div>

      <div className="space-y-1 pt-0.5">
        <div className="flex items-center justify-between text-emerald-400">
          <span>{t.chartSuccessfulQueries}:</span>
          <span className="font-bold">{data.successfulQueries}</span>
        </div>
        <div className="flex items-center justify-between text-blue-400">
          <span>{t.chartHealthChecks}:</span>
          <span className="font-bold">{data.healthChecks}</span>
        </div>
        {data.errorQueries > 0 && (
          <div className="flex items-center justify-between text-rose-400 font-bold">
            <span>{t.chartErrorsRefused}:</span>
            <span>{data.errorQueries}</span>
          </div>
        )}
        {data.avgLatency > 0 && (
          <div className="flex items-center justify-between text-stone-300 text-[10px] pt-1 border-t border-stone-800">
            <span>{t.chartAvgLatency}:</span>
            <span>{data.avgLatency}ms</span>
          </div>
        )}
      </div>
    </div>
  );
};

// Custom Tooltip for Latency View
const CustomLatencyTooltip = ({ active, payload, label, language, t }: any) => {
  if (!active || !payload || payload.length === 0) return null;
  const data = payload[0]?.payload as TimelineBucket;
  if (!data) return null;

  return (
    <div className="bg-stone-900 text-stone-100 rounded-lg p-2.5 shadow-xl border border-stone-700 text-xs font-mono space-y-1 min-w-[150px] z-50">
      <div className="flex items-center justify-between text-[11px] text-stone-400 pb-1 border-b border-stone-800">
        <span>{data.timeLabel}</span>
        <span>{data.totalEvents} reqs</span>
      </div>
      <div className="flex items-center justify-between text-sky-400 font-bold">
        <span>{t.chartAvgLatency}:</span>
        <span>{data.avgLatency}ms</span>
      </div>
    </div>
  );
};
