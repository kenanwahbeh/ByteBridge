import React, { useState } from 'react';
import { Database, Server, User, Folder, Activity, Edit2, Trash2, CheckCircle2, XCircle } from 'lucide-react';
import type { DatabaseConfig } from '../types';
import { translations } from '../localization/translations';

interface DatabaseCardProps {
  connection: DatabaseConfig;
  language: 'en' | 'ar';
  onToggle: (id: string) => Promise<void>;
  onEdit: (conn: DatabaseConfig) => void;
  onDelete: (id: string, name: string) => void;
  onTest: (conn: DatabaseConfig) => Promise<{ success: boolean; latencyMs?: number; message?: string }>;
}

export const DatabaseCard: React.FC<DatabaseCardProps> = ({
  connection,
  language,
  onToggle,
  onEdit,
  onDelete,
  onTest,
}) => {
  const t = translations[language];
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);

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
      setTimeout(() => setTestResult(null), 4000);
    } finally {
      setTesting(false);
    }
  };

  return (
    <div
      className={`bg-white rounded-xl border transition-all p-5 shadow-xs relative overflow-hidden ${
        connection.enabled
          ? 'border-stone-300 ring-1 ring-emerald-500/20'
          : 'border-stone-200 opacity-90'
      }`}
    >
      {/* Top row: Name, Online status, and Action buttons */}
      <div className="flex items-start justify-between gap-3 mb-3">
        <div className="flex items-center gap-2.5">
          <div
            className={`p-2 rounded-lg border ${
              connection.enabled
                ? 'bg-emerald-50 text-emerald-700 border-emerald-200'
                : 'bg-stone-100 text-stone-500 border-stone-200'
            }`}
          >
            <Database className="w-5 h-5" />
          </div>
          <div>
            <div className="flex items-center gap-2">
              <h3 className="text-base font-bold text-stone-900 leading-tight">
                {connection.name}
              </h3>
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
            <Activity className={`w-3.5 h-3.5 ${testing ? 'animate-spin' : ''}`} />
          </button>

          <button
            onClick={() => onEdit(connection)}
            className="p-1.5 rounded-lg border border-stone-200 text-stone-600 hover:bg-stone-100 transition-colors shadow-2xs"
            title={t.edit}
          >
            <Edit2 className="w-3.5 h-3.5" />
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
    </div>
  );
};
