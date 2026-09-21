import React from 'react';
import { ScrollText, Trash2, CheckCircle, XCircle, Clock, ShieldAlert } from 'lucide-react';
import type { RequestLogItem } from '../types';
import { translations } from '../localization/translations';

interface RequestAuditLogsProps {
  logs: RequestLogItem[];
  language: 'en' | 'ar';
  onClearLogs: () => Promise<void>;
}

export const RequestAuditLogs: React.FC<RequestAuditLogsProps> = ({
  logs,
  language,
  onClearLogs,
}) => {
  const t = translations[language];

  const getStatusBadge = (status: number) => {
    if (status >= 200 && status < 300) {
      return (
        <span className="px-1.5 py-0.5 rounded text-[10px] font-bold bg-emerald-100 text-emerald-800">
          {status}
        </span>
      );
    }
    if (status === 401 || status === 403) {
      return (
        <span className="px-1.5 py-0.5 rounded text-[10px] font-bold bg-amber-100 text-amber-800">
          {status}
        </span>
      );
    }
    return (
      <span className="px-1.5 py-0.5 rounded text-[10px] font-bold bg-rose-100 text-rose-800">
        {status}
      </span>
    );
  };

  return (
    <div className="bg-white rounded-xl border border-stone-200 p-6 shadow-xs space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <div className="flex items-center gap-2">
            <ScrollText className="w-4 h-4 text-stone-600" />
            <h2 className="text-base font-bold text-stone-900">{t.requestLogsTitle}</h2>
          </div>
          <p className="text-xs text-stone-500 mt-0.5">{t.requestLogsDesc}</p>
        </div>

        {logs.length > 0 && (
          <button
            onClick={onClearLogs}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg border border-stone-300 text-stone-600 hover:text-rose-600 hover:bg-rose-50 text-xs font-semibold transition-colors shadow-2xs"
          >
            <Trash2 className="w-3.5 h-3.5" />
            <span>{t.clearLogs}</span>
          </button>
        )}
      </div>

      {logs.length === 0 ? (
        <div className="text-center py-10 text-stone-400 text-xs border border-dashed border-stone-200 rounded-lg">
          {t.noLogs}
        </div>
      ) : (
        <div className="overflow-x-auto border border-stone-200 rounded-lg">
          <table className="w-full text-left text-xs font-mono border-collapse">
            <thead>
              <tr className="border-b border-stone-200 bg-stone-50 text-stone-600">
                <th className="px-3 py-2">Timestamp</th>
                <th className="px-3 py-2">Method</th>
                <th className="px-3 py-2">Path</th>
                <th className="px-3 py-2">Status</th>
                <th className="px-3 py-2">Latency</th>
                <th className="px-3 py-2">Database</th>
                <th className="px-3 py-2">Auth</th>
                <th className="px-3 py-2">Details / SQL</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-stone-100">
              {logs.map(item => (
                <tr key={item.id} className="hover:bg-stone-50/70">
                  <td className="px-3 py-2 text-stone-500 whitespace-nowrap">
                    {new Date(item.at).toLocaleTimeString()}
                  </td>
                  <td className="px-3 py-2 font-bold text-stone-800">
                    <span
                      className={`px-1.5 py-0.5 rounded text-[10px] ${
                        item.method === 'GET'
                          ? 'bg-sky-50 text-sky-700'
                          : 'bg-indigo-50 text-indigo-700'
                      }`}
                    >
                      {item.method}
                    </span>
                  </td>
                  <td className="px-3 py-2 text-stone-900 font-semibold">{item.path}</td>
                  <td className="px-3 py-2">{getStatusBadge(item.status)}</td>
                  <td className="px-3 py-2 text-stone-500">{item.elapsedMs}ms</td>
                  <td className="px-3 py-2 text-stone-700 font-medium">
                    {item.database || '—'}
                  </td>
                  <td className="px-3 py-2">
                    {item.authenticated ? (
                      <span className="text-emerald-600 flex items-center gap-1 text-[11px]">
                        <CheckCircle className="w-3.5 h-3.5" />
                        <span>Valid</span>
                      </span>
                    ) : (
                      <span className="text-rose-500 flex items-center gap-1 text-[11px]">
                        <ShieldAlert className="w-3.5 h-3.5" />
                        <span>Refused</span>
                      </span>
                    )}
                  </td>
                  <td className="px-3 py-2 text-stone-600 max-w-xs truncate" title={item.sql || item.error}>
                    {item.sql ? (
                      <span className="text-stone-800">{item.sql}</span>
                    ) : item.error ? (
                      <span className="text-rose-600">{item.error}</span>
                    ) : (
                      <span className="text-stone-400">—</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
};
