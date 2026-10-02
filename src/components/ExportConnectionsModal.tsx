import React, { useState, useMemo } from 'react';
import {
  X,
  Download,
  Copy,
  Check,
  ShieldCheck,
  ShieldAlert,
  Database,
  Code2,
  Share2,
} from 'lucide-react';
import type { DatabaseConfig, ConnectionsExportData } from '../types';
import { translations } from '../localization/translations';

interface ExportConnectionsModalProps {
  isOpen: boolean;
  onClose: () => void;
  connections: DatabaseConfig[];
  language: 'en' | 'ar';
}

export const ExportConnectionsModal: React.FC<ExportConnectionsModalProps> = ({
  isOpen,
  onClose,
  connections,
  language,
}) => {
  const t = translations[language];
  const [includePasswords, setIncludePasswords] = useState(true);
  const [copied, setCopied] = useState(false);
  const [showPreview, setShowPreview] = useState(false);

  // Generate exported structure
  const exportData: ConnectionsExportData = useMemo(() => {
    const sanitizedConnections = connections.map(conn => {
      const copy: DatabaseConfig = { ...conn };
      if (!includePasswords) {
        delete copy.password;
      }
      return copy;
    });

    return {
      app: 'ByteBridge',
      version: '1.0',
      exportedAt: new Date().toISOString(),
      totalConnections: sanitizedConnections.length,
      connections: sanitizedConnections,
    };
  }, [connections, includePasswords]);

  const jsonString = useMemo(() => {
    return JSON.stringify(exportData, null, 2);
  }, [exportData]);

  // Breakdown of database types
  const typeCounts = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const c of connections) {
      const type = c.type || (c.database.endsWith('.sqlite') ? 'SQLite' : c.port === 5432 ? 'PostgreSQL' : 'Firebird');
      counts[type] = (counts[type] || 0) + 1;
    }
    return counts;
  }, [connections]);

  if (!isOpen) return null;

  const handleDownload = () => {
    const blob = new Blob([jsonString], { type: 'application/json;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    const dateStr = new Date().toISOString().slice(0, 10);
    link.href = url;
    link.download = `bytebridge-connections-${dateStr}.json`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  };

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(jsonString);
      setCopied(true);
      setTimeout(() => setCopied(false), 2500);
    } catch {
      // Fallback
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-stone-900/60 backdrop-blur-xs animate-in fade-in duration-150">
      <div
        className="bg-white rounded-2xl border border-stone-200 shadow-2xl max-w-lg w-full max-h-[90vh] flex flex-col overflow-hidden text-stone-800"
        dir={language === 'ar' ? 'rtl' : 'ltr'}
      >
        {/* Header */}
        <div className="flex items-center justify-between p-5 border-b border-stone-100 bg-stone-50/50 shrink-0">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-amber-500/10 text-amber-700 flex items-center justify-center border border-amber-500/20">
              <Download className="w-5 h-5 text-amber-600" />
            </div>
            <div>
              <h2 className="text-base font-bold text-stone-900 leading-snug">
                {t.exportConnections}
              </h2>
              <p className="text-xs text-stone-500">
                {t.migratingSetupBadge} • JSON
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="p-1.5 text-stone-400 hover:text-stone-700 hover:bg-stone-100 rounded-lg transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content Body */}
        <div className="p-5 space-y-4 overflow-y-auto">
          <p className="text-xs text-stone-600 leading-relaxed">
            {t.exportConnectionsDesc}
          </p>

          {/* Connection summary pill card */}
          <div className="p-3.5 rounded-xl bg-stone-50 border border-stone-200/80 space-y-2.5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold text-stone-700 flex items-center gap-1.5">
                <Database className="w-3.5 h-3.5 text-stone-500" />
                {t.exportReadyMsg.replace('{0}', String(connections.length))}
              </span>
              <span className="text-[11px] font-mono px-2 py-0.5 rounded-full bg-stone-200/80 text-stone-700 font-semibold">
                v1.0
              </span>
            </div>

            {/* Type breakdown pills */}
            <div className="flex flex-wrap gap-1.5 pt-1">
              {Object.entries(typeCounts).map(([typeName, count]) => (
                <span
                  key={typeName}
                  className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[11px] font-medium bg-white border border-stone-200 text-stone-600 shadow-2xs"
                >
                  <span className="w-1.5 h-1.5 rounded-full bg-amber-500" />
                  {typeName}: <strong className="font-mono text-stone-800">{count}</strong>
                </span>
              ))}
            </div>
          </div>

          {/* Include Passwords Option */}
          <div className="p-3.5 rounded-xl border border-stone-200 bg-stone-50/50 hover:bg-stone-50 transition-colors">
            <label className="flex items-start gap-3 cursor-pointer">
              <input
                type="checkbox"
                checked={includePasswords}
                onChange={e => setIncludePasswords(e.target.checked)}
                className="mt-0.5 w-4 h-4 rounded border-stone-300 text-amber-600 focus:ring-amber-500"
              />
              <div className="flex-1 text-xs">
                <div className="flex items-center gap-2 font-semibold text-stone-800">
                  <span>{t.includePasswords}</span>
                  {includePasswords ? (
                    <span className="inline-flex items-center gap-1 text-[10px] font-medium text-emerald-700 bg-emerald-50 border border-emerald-200 px-1.5 py-0.5 rounded">
                      <ShieldCheck className="w-3 h-3" />
                      {language === 'ar' ? 'سلس للترحيل' : 'Seamless migration'}
                    </span>
                  ) : (
                    <span className="inline-flex items-center gap-1 text-[10px] font-medium text-amber-700 bg-amber-50 border border-amber-200 px-1.5 py-0.5 rounded">
                      <ShieldAlert className="w-3 h-3" />
                      {language === 'ar' ? 'بدون بيانات سرية' : 'Sanitized'}
                    </span>
                  )}
                </div>
                <p className="text-[11px] text-stone-500 mt-0.5 leading-normal">
                  {t.includePasswordsHint}
                </p>
              </div>
            </label>
          </div>

          {/* Migration note */}
          <div className="p-3 rounded-lg bg-amber-50/70 border border-amber-200/60 text-[11px] text-amber-800 flex items-start gap-2">
            <Share2 className="w-4 h-4 text-amber-600 shrink-0 mt-0.5" />
            <p className="leading-relaxed">{t.migrateNotice}</p>
          </div>

          {/* Collapsible JSON Preview */}
          <div className="border border-stone-200 rounded-xl overflow-hidden bg-stone-900">
            <button
              type="button"
              onClick={() => setShowPreview(!showPreview)}
              className="w-full px-3.5 py-2.5 flex items-center justify-between text-xs font-mono text-stone-300 hover:text-white transition-colors bg-stone-800/80"
            >
              <span className="flex items-center gap-1.5">
                <Code2 className="w-3.5 h-3.5 text-amber-400" />
                {t.previewJson} ({connections.length} records)
              </span>
              <span className="text-[11px] text-stone-400">
                {showPreview ? (language === 'ar' ? 'إخفاء' : 'Hide') : (language === 'ar' ? 'عرض' : 'Expand')}
              </span>
            </button>

            {showPreview && (
              <pre className="p-3 text-[11px] font-mono text-stone-200 overflow-x-auto max-h-48 leading-relaxed selection:bg-amber-600 selection:text-white">
                {jsonString}
              </pre>
            )}
          </div>
        </div>

        {/* Footer Actions */}
        <div className="p-4 border-t border-stone-100 bg-stone-50/70 flex items-center justify-between gap-3 shrink-0">
          <button
            type="button"
            onClick={handleCopy}
            disabled={connections.length === 0}
            className="inline-flex items-center gap-1.5 px-3 py-2 text-xs font-semibold rounded-lg border border-stone-300 bg-white text-stone-700 hover:bg-stone-100 transition-colors shadow-2xs disabled:opacity-50"
          >
            {copied ? (
              <>
                <Check className="w-3.5 h-3.5 text-emerald-600" />
                <span className="text-emerald-700 font-bold">{t.copiedJson}</span>
              </>
            ) : (
              <>
                <Copy className="w-3.5 h-3.5 text-stone-500" />
                <span>{t.copyJson}</span>
              </>
            )}
          </button>

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={onClose}
              className="px-3.5 py-2 text-xs font-semibold rounded-lg text-stone-600 hover:bg-stone-200/60 transition-colors"
            >
              {language === 'ar' ? 'إلغاء' : 'Cancel'}
            </button>
            <button
              type="button"
              onClick={handleDownload}
              disabled={connections.length === 0}
              className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-bold rounded-lg bg-stone-900 text-white hover:bg-stone-800 transition-colors shadow-2xs disabled:opacity-50"
            >
              <Download className="w-3.5 h-3.5" />
              <span>{t.downloadJsonFile}</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};
