import React, { useState, useMemo, useRef } from 'react';
import {
  X,
  Upload,
  FileText,
  AlertCircle,
  CheckCircle2,
  Database,
  Lock,
  ArrowRight,
  Server,
  Layers,
  Sparkles,
  RefreshCw,
} from 'lucide-react';
import type { DatabaseConfig, ImportMode } from '../types';
import { translations } from '../localization/translations';

interface ImportConnectionsModalProps {
  isOpen: boolean;
  onClose: () => void;
  existingConnections: DatabaseConfig[];
  language: 'en' | 'ar';
  onImportSuccess: (count: number, mode: ImportMode) => void;
}

export const ImportConnectionsModal: React.FC<ImportConnectionsModalProps> = ({
  isOpen,
  onClose,
  existingConnections,
  language,
  onImportSuccess,
}) => {
  const t = translations[language];
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [inputTab, setInputTab] = useState<'upload' | 'paste'>('upload');
  const [jsonText, setJsonText] = useState('');
  const [fileName, setFileName] = useState<string | null>(null);
  const [isDragging, setIsDragging] = useState(false);
  const [importMode, setImportMode] = useState<ImportMode>('merge');
  const [preserveExistingPasswords, setPreserveExistingPasswords] = useState(true);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorBanner, setErrorBanner] = useState<string | null>(null);

  // Parse and validate connections from jsonText
  const parsedResult = useMemo(() => {
    if (!jsonText.trim()) {
      return { isValid: false, connections: [], error: null };
    }

    try {
      const parsed = JSON.parse(jsonText);
      const rawList: any[] = Array.isArray(parsed)
        ? parsed
        : Array.isArray(parsed?.connections)
        ? parsed.connections
        : [];

      if (!rawList.length) {
        return {
          isValid: false,
          connections: [],
          error: t.noConnectionsFoundError,
        };
      }

      const validList: DatabaseConfig[] = [];
      for (const item of rawList) {
        if (!item || typeof item !== 'object') continue;
        const name = typeof item.name === 'string' ? item.name.trim() : '';
        const database = typeof item.database === 'string' ? item.database.trim() : '';
        if (!name || !database) continue;

        const server = typeof item.server === 'string' ? item.server.trim() : 'localhost';
        const port = Number(item.port) || (server.includes('postgres') ? 5432 : 3050);
        const username = typeof item.username === 'string' ? item.username.trim() : 'SYSDBA';
        const type = typeof item.type === 'string' && item.type.trim()
          ? item.type.trim()
          : (database.endsWith('.sqlite') || database.endsWith('.db') ? 'SQLite' : port === 5432 ? 'PostgreSQL' : 'Firebird');

        validList.push({
          id: (typeof item.id === 'string' && item.id.trim()) || ('conn-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2, 6)),
          name,
          type,
          server,
          port,
          username,
          password: typeof item.password === 'string' ? item.password : '',
          database,
          enabled: item.enabled !== undefined ? !!item.enabled : true,
          lastTestSuccessful: item.lastTestSuccessful !== undefined ? item.lastTestSuccessful : true,
          lastTestedAt: item.lastTestedAt || new Date().toISOString(),
          lastLatencyMs: typeof item.lastLatencyMs === 'number' ? item.lastLatencyMs : 14,
        });
      }

      if (!validList.length) {
        return {
          isValid: false,
          connections: [],
          error: t.noConnectionsFoundError,
        };
      }

      return { isValid: true, connections: validList, error: null };
    } catch {
      return {
        isValid: false,
        connections: [],
        error: t.invalidJsonError,
      };
    }
  }, [jsonText, t]);

  if (!isOpen) return null;

  const handleFileDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
    const file = e.dataTransfer.files?.[0];
    if (file) {
      readFile(file);
    }
  };

  const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      readFile(file);
    }
  };

  const readFile = (file: File) => {
    setFileName(file.name);
    setErrorBanner(null);
    const reader = new FileReader();
    reader.onload = event => {
      const content = event.target?.result as string;
      setJsonText(content || '');
    };
    reader.onerror = () => {
      setErrorBanner('Failed to read selected file.');
    };
    reader.readAsText(file);
  };

  const handleExecuteImport = async () => {
    if (!parsedResult.isValid || parsedResult.connections.length === 0) return;

    setIsSubmitting(true);
    setErrorBanner(null);

    try {
      const response = await fetch('/api/connections/import', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({
          connections: parsedResult.connections,
          mode: importMode,
          preserveExistingPasswords,
        }),
      });

      const contentType = response.headers.get('content-type') || '';
      if (!contentType.includes('application/json')) {
        throw new Error(`Server returned HTTP ${response.status}`);
      }

      const data = await response.json();
      if (!response.ok || !data.success) {
        throw new Error(data.error || 'Import failed.');
      }

      onImportSuccess(data.importedCount || parsedResult.connections.length, importMode);
      onClose();
    } catch (err: any) {
      setErrorBanner(err.message || 'An unexpected error occurred during import.');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-stone-900/60 backdrop-blur-xs animate-in fade-in duration-150">
      <div
        className="bg-white rounded-2xl border border-stone-200 shadow-2xl max-w-2xl w-full max-h-[92vh] flex flex-col overflow-hidden text-stone-800"
        dir={language === 'ar' ? 'rtl' : 'ltr'}
      >
        {/* Header */}
        <div className="flex items-center justify-between p-5 border-b border-stone-100 bg-stone-50/50 shrink-0">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-indigo-500/10 text-indigo-700 flex items-center justify-center border border-indigo-500/20">
              <Upload className="w-5 h-5 text-indigo-600" />
            </div>
            <div>
              <h2 className="text-base font-bold text-stone-900 leading-snug">
                {t.importModalTitle}
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
            {t.importConnectionsDesc}
          </p>

          {/* Input Method Tabs */}
          <div className="flex items-center gap-2 p-1 bg-stone-100 rounded-lg border border-stone-200/80">
            <button
              type="button"
              onClick={() => setInputTab('upload')}
              className={`flex-1 py-1.5 px-3 text-xs font-semibold rounded-md transition-all flex items-center justify-center gap-1.5 ${
                inputTab === 'upload'
                  ? 'bg-white text-stone-900 shadow-2xs'
                  : 'text-stone-500 hover:text-stone-800'
              }`}
            >
              <Upload className="w-3.5 h-3.5" />
              <span>{t.uploadTab}</span>
            </button>
            <button
              type="button"
              onClick={() => setInputTab('paste')}
              className={`flex-1 py-1.5 px-3 text-xs font-semibold rounded-md transition-all flex items-center justify-center gap-1.5 ${
                inputTab === 'paste'
                  ? 'bg-white text-stone-900 shadow-2xs'
                  : 'text-stone-500 hover:text-stone-800'
              }`}
            >
              <FileText className="w-3.5 h-3.5" />
              <span>{t.pasteTab}</span>
            </button>
          </div>

          {/* Tab 1: Upload Dropzone */}
          {inputTab === 'upload' && (
            <div>
              <input
                ref={fileInputRef}
                type="file"
                accept=".json,application/json"
                onChange={handleFileSelect}
                className="hidden"
              />
              <div
                onDragOver={e => {
                  e.preventDefault();
                  setIsDragging(true);
                }}
                onDragLeave={() => setIsDragging(false)}
                onDrop={handleFileDrop}
                onClick={() => fileInputRef.current?.click()}
                className={`p-6 rounded-xl border-2 border-dashed text-center cursor-pointer transition-all ${
                  isDragging
                    ? 'border-indigo-500 bg-indigo-50/50 scale-[0.99]'
                    : fileName
                    ? 'border-emerald-300 bg-emerald-50/20'
                    : 'border-stone-300 hover:border-stone-400 bg-stone-50/50 hover:bg-stone-50'
                }`}
              >
                <div className="w-10 h-10 rounded-full bg-stone-100 flex items-center justify-center mx-auto mb-2.5 text-stone-500">
                  <Upload className="w-5 h-5 text-indigo-600" />
                </div>
                {fileName ? (
                  <div>
                    <span className="text-xs font-bold text-emerald-800 flex items-center justify-center gap-1.5">
                      <CheckCircle2 className="w-4 h-4 text-emerald-600" />
                      {fileName}
                    </span>
                    <p className="text-[11px] text-stone-500 mt-1">
                      {language === 'ar' ? 'انقر لاختيار ملف مختلف' : 'Click to select a different file'}
                    </p>
                  </div>
                ) : (
                  <div>
                    <p className="text-xs font-semibold text-stone-800">
                      {t.dropzoneText}
                    </p>
                    <p className="text-[11px] text-stone-400 mt-1">
                      {t.dropzoneHint}
                    </p>
                  </div>
                )}
              </div>
            </div>
          )}

          {/* Tab 2: Paste JSON Textarea */}
          {inputTab === 'paste' && (
            <div>
              <textarea
                value={jsonText}
                onChange={e => {
                  setJsonText(e.target.value);
                  setFileName(null);
                }}
                placeholder={t.pastePlaceholder}
                rows={5}
                className="w-full p-3 rounded-xl border border-stone-300 font-mono text-[11px] text-stone-800 placeholder:text-stone-400 focus:outline-none focus:ring-2 focus:ring-stone-900/10 focus:border-stone-500 transition-all"
              />
            </div>
          )}

          {/* Error Banner */}
          {(errorBanner || parsedResult.error) && (
            <div className="p-3 rounded-xl bg-rose-50 border border-rose-200 text-xs text-rose-800 flex items-start gap-2 animate-in fade-in">
              <AlertCircle className="w-4 h-4 text-rose-600 shrink-0 mt-0.5" />
              <span>{errorBanner || parsedResult.error}</span>
            </div>
          )}

          {/* Valid Preview List */}
          {parsedResult.isValid && parsedResult.connections.length > 0 && (
            <div className="space-y-3 pt-1">
              <div className="flex items-center justify-between">
                <span className="text-xs font-bold text-stone-900 flex items-center gap-1.5">
                  <CheckCircle2 className="w-4 h-4 text-emerald-600" />
                  {t.validJsonFound.replace('{0}', String(parsedResult.connections.length))}
                </span>
                <span className="text-[11px] font-mono text-stone-500">
                  {parsedResult.connections.length} {language === 'ar' ? 'قاعدة' : 'databases'}
                </span>
              </div>

              {/* Connections list preview cards */}
              <div className="max-h-44 overflow-y-auto space-y-2 pr-1 border border-stone-200/90 rounded-xl p-2 bg-stone-50/40">
                {parsedResult.connections.map((conn, idx) => {
                  const existingMatch = existingConnections.find(
                    c => c.id === conn.id || c.name.toLowerCase() === conn.name.toLowerCase()
                  );
                  return (
                    <div
                      key={conn.id || idx}
                      className="p-2.5 rounded-lg bg-white border border-stone-200 text-xs flex items-center justify-between gap-3 shadow-2xs"
                    >
                      <div className="flex items-center gap-2.5 min-w-0">
                        <div className="w-7 h-7 rounded-md bg-stone-100 flex items-center justify-center shrink-0">
                          <Database className="w-3.5 h-3.5 text-stone-600" />
                        </div>
                        <div className="min-w-0">
                          <div className="flex items-center gap-2">
                            <span className="font-bold text-stone-900 truncate">
                              {conn.name}
                            </span>
                            <span className="px-1.5 py-0.2 rounded text-[10px] font-mono bg-stone-100 text-stone-600 border border-stone-200">
                              {conn.type}
                            </span>
                          </div>
                          <div className="text-[11px] text-stone-500 font-mono truncate flex items-center gap-2">
                            <span>{conn.server}:{conn.port}</span>
                            <span>•</span>
                            <span className="truncate">{conn.database}</span>
                          </div>
                        </div>
                      </div>

                      <div className="flex items-center gap-1.5 shrink-0">
                        {conn.password ? (
                          <span
                            className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[10px] font-medium bg-emerald-50 text-emerald-700 border border-emerald-200"
                            title={t.hasPassword}
                          >
                            <Lock className="w-3 h-3" />
                            <span className="hidden sm:inline">{t.hasPassword}</span>
                          </span>
                        ) : (
                          <span
                            className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-stone-100 text-stone-500"
                            title={t.noPassword}
                          >
                            {t.noPassword}
                          </span>
                        )}

                        {existingMatch ? (
                          <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-amber-100 text-amber-800 border border-amber-200">
                            {t.statusUpdate}
                          </span>
                        ) : (
                          <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-blue-100 text-blue-800 border border-blue-200">
                            {t.statusNew}
                          </span>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>

              {/* Import Strategy Options */}
              <div className="pt-2 border-t border-stone-100 space-y-3">
                <span className="text-xs font-bold text-stone-800 block">
                  {t.importMode}
                </span>

                <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
                  <label
                    className={`p-3 rounded-xl border text-xs cursor-pointer transition-all ${
                      importMode === 'merge'
                        ? 'border-indigo-600 bg-indigo-50/30 text-indigo-950 ring-1 ring-indigo-600'
                        : 'border-stone-200 hover:border-stone-300 text-stone-700'
                    }`}
                  >
                    <div className="flex items-start gap-2.5">
                      <input
                        type="radio"
                        name="importMode"
                        value="merge"
                        checked={importMode === 'merge'}
                        onChange={() => setImportMode('merge')}
                        className="mt-0.5 text-indigo-600 focus:ring-indigo-500"
                      />
                      <div>
                        <span className="font-semibold block">{t.modeMerge}</span>
                        <p className="text-[11px] text-stone-500 mt-0.5 leading-snug">
                          {t.modeMergeHint}
                        </p>
                      </div>
                    </div>
                  </label>

                  <label
                    className={`p-3 rounded-xl border text-xs cursor-pointer transition-all ${
                      importMode === 'replace'
                        ? 'border-rose-600 bg-rose-50/30 text-rose-950 ring-1 ring-rose-600'
                        : 'border-stone-200 hover:border-stone-300 text-stone-700'
                    }`}
                  >
                    <div className="flex items-start gap-2.5">
                      <input
                        type="radio"
                        name="importMode"
                        value="replace"
                        checked={importMode === 'replace'}
                        onChange={() => setImportMode('replace')}
                        className="mt-0.5 text-rose-600 focus:ring-rose-500"
                      />
                      <div>
                        <span className="font-semibold block text-rose-900">{t.modeReplace}</span>
                        <p className="text-[11px] text-stone-500 mt-0.5 leading-snug">
                          {t.modeReplaceHint}
                        </p>
                      </div>
                    </div>
                  </label>
                </div>

                {/* Preserve Password Checkbox */}
                <label className="flex items-center gap-2.5 cursor-pointer pt-1">
                  <input
                    type="checkbox"
                    checked={preserveExistingPasswords}
                    onChange={e => setPreserveExistingPasswords(e.target.checked)}
                    className="w-4 h-4 rounded border-stone-300 text-indigo-600 focus:ring-indigo-500"
                  />
                  <span className="text-xs text-stone-600">
                    {t.preserveExistingPasswords}
                  </span>
                </label>
              </div>
            </div>
          )}
        </div>

        {/* Footer Actions */}
        <div className="p-4 border-t border-stone-100 bg-stone-50/70 flex items-center justify-between gap-3 shrink-0">
          <button
            type="button"
            onClick={onClose}
            className="px-3.5 py-2 text-xs font-semibold rounded-lg text-stone-600 hover:bg-stone-200/60 transition-colors"
          >
            {language === 'ar' ? 'إلغاء' : 'Cancel'}
          </button>

          <button
            type="button"
            disabled={!parsedResult.isValid || parsedResult.connections.length === 0 || isSubmitting}
            onClick={handleExecuteImport}
            className="inline-flex items-center gap-2 px-5 py-2 text-xs font-bold rounded-lg bg-stone-900 text-white hover:bg-stone-800 transition-colors shadow-2xs disabled:opacity-40 disabled:cursor-not-allowed"
          >
            {isSubmitting ? (
              <>
                <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                <span>{t.importingBtn}</span>
              </>
            ) : (
              <>
                <Upload className="w-3.5 h-3.5" />
                <span>
                  {t.importBtn.replace(
                    '{0}',
                    String(parsedResult.isValid ? parsedResult.connections.length : 0)
                  )}
                </span>
              </>
            )}
          </button>
        </div>
      </div>
    </div>
  );
};
