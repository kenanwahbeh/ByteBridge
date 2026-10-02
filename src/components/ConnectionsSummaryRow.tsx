import React from 'react';
import {
  Activity,
  Layers,
  CheckCircle2,
  XCircle,
  BarChart2,
  TrendingUp,
  Server,
  Wifi,
  WifiOff,
} from 'lucide-react';
import type { DatabaseConfig } from '../types';
import { translations } from '../localization/translations';

interface ConnectionsSummaryRowProps {
  connections: DatabaseConfig[];
  language: 'en' | 'ar';
  onlineFilter: 'ALL' | 'ONLINE' | 'OFFLINE';
  onFilterChange: (filter: 'ALL' | 'ONLINE' | 'OFFLINE') => void;
  showChart: boolean;
  onToggleChart: () => void;
}

export const ConnectionsSummaryRow: React.FC<ConnectionsSummaryRowProps> = ({
  connections,
  language,
  onlineFilter,
  onFilterChange,
  showChart,
  onToggleChart,
}) => {
  const t = translations[language];

  const totalConnections = connections.length;
  const onlineCount = connections.filter((c) => c.enabled).length;
  const offlineCount = connections.filter((c) => !c.enabled).length;
  const activeCount = connections.filter(
    (c) => c.enabled && c.lastTestSuccessful !== false
  ).length;

  const activePercent =
    totalConnections > 0
      ? Math.round((activeCount / totalConnections) * 100)
      : 0;

  return (
    <div className="bg-white rounded-xl border border-stone-200 p-4 shadow-xs">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        {/* Left: Total Active vs Total Connections */}
        <div className="flex items-center gap-3">
          <div className="p-2.5 rounded-xl bg-stone-900 text-white shadow-2xs shrink-0">
            <Activity className="w-5 h-5 text-emerald-400" />
          </div>
          <div>
            <div className="flex items-baseline gap-2">
              <span className="text-xl font-bold font-mono text-stone-900 leading-tight">
                {activeCount}
                <span className="text-stone-400 text-sm font-normal"> / {totalConnections}</span>
              </span>
              <span className="text-xs font-semibold text-stone-700">
                {t.activeConnectionsLabel}
              </span>
              <span className="text-[11px] font-mono font-semibold px-2 py-0.5 rounded-full bg-emerald-50 text-emerald-700 border border-emerald-200/60">
                {activePercent}% {t.operationalStatus}
              </span>
            </div>

            {/* Micro visual progress capacity line */}
            <div className="w-48 sm:w-64 h-1.5 bg-stone-100 rounded-full mt-2 overflow-hidden flex">
              <div
                className="bg-emerald-500 h-full rounded-full transition-all duration-500"
                style={{ width: `${activePercent}%` }}
              />
            </div>
          </div>
        </div>

        {/* Center / Right: Quick Count for 'Online' vs 'Offline' & Chart Toggle */}
        <div className="flex items-center gap-2 flex-wrap sm:flex-nowrap">
          {/* Quick Count Filter Buttons */}
          <div className="flex items-center p-1 rounded-lg bg-stone-100/80 border border-stone-200/70 text-xs font-medium">
            {/* All */}
            <button
              type="button"
              onClick={() => onFilterChange('ALL')}
              className={`px-3 py-1.5 rounded-md transition-all ${
                onlineFilter === 'ALL'
                  ? 'bg-white text-stone-900 font-semibold shadow-2xs'
                  : 'text-stone-600 hover:text-stone-900'
              }`}
            >
              {t.allStatus} ({totalConnections})
            </button>

            {/* Online Quick Count */}
            <button
              type="button"
              onClick={() => onFilterChange(onlineFilter === 'ONLINE' ? 'ALL' : 'ONLINE')}
              className={`flex items-center gap-1.5 px-3 py-1.5 rounded-md transition-all ${
                onlineFilter === 'ONLINE'
                  ? 'bg-emerald-600 text-white font-semibold shadow-2xs'
                  : 'text-stone-700 hover:text-stone-900'
              }`}
            >
              <span
                className={`w-2 h-2 rounded-full ${
                  onlineFilter === 'ONLINE' ? 'bg-white' : 'bg-emerald-500 animate-pulse'
                }`}
              />
              <span>{t.onlineCountLabel.replace('{0}', String(onlineCount))}</span>
            </button>

            {/* Offline Quick Count */}
            <button
              type="button"
              onClick={() => onFilterChange(onlineFilter === 'OFFLINE' ? 'ALL' : 'OFFLINE')}
              className={`flex items-center gap-1.5 px-3 py-1.5 rounded-md transition-all ${
                onlineFilter === 'OFFLINE'
                  ? 'bg-stone-800 text-white font-semibold shadow-2xs'
                  : 'text-stone-600 hover:text-stone-900'
              }`}
            >
              <span
                className={`w-2 h-2 rounded-full ${
                  onlineFilter === 'OFFLINE' ? 'bg-white' : 'bg-stone-400'
                }`}
              />
              <span>{t.offlineCountLabel.replace('{0}', String(offlineCount))}</span>
            </button>
          </div>

          {/* Toggle Activity Chart Button */}
          <button
            type="button"
            onClick={onToggleChart}
            className={`flex items-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-lg border transition-all shadow-2xs shrink-0 ${
              showChart
                ? 'bg-stone-900 text-white border-stone-900 hover:bg-stone-800'
                : 'bg-white text-stone-700 border-stone-200 hover:bg-stone-50'
            }`}
            title={t.activityChartTitle}
          >
            <BarChart2 className="w-3.5 h-3.5 text-emerald-400" />
            <span className="hidden sm:inline">
              {showChart ? t.chartHide : t.chartShow}
            </span>
          </button>
        </div>
      </div>
    </div>
  );
};
