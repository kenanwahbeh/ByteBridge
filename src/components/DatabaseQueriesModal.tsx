import React, { useState, useMemo } from 'react';
import {
  X,
  Terminal,
  Search,
  Copy,
  Check,
  Clock,
  Zap,
  Server,
  AlertCircle,
  Database,
  ArrowRight,
  ShieldCheck,
  Filter,
} from 'lucide-react';
import type { DatabaseConfig, RequestLogItem } from '../types';
import { translations } from '../localization/translations';

interface DatabaseQueriesModalProps {
  isOpen: boolean;
  connection: DatabaseConfig;
  language: 'en' | 'ar';
  logs: RequestLogItem[];
  onClose: () => void;
}

export const DatabaseQueriesModal: React.FC<DatabaseQueriesModalProps> = ({
  isOpen,
  connection,
  language,
  logs,
  onClose,
}) => {
  const t = translations[language];
  const [searchQuery, setSearchQuery] = useState('');
  const [filterErrorOnly, setFilterErrorOnly] = useState(false);
  const [copiedId, setCopiedId] = useState<string | null>(null);

  // Filter logs specifically belonging to this database
  const dbQueries = useMemo(() => {
    if (!logs || logs.length === 0) return [];
    const connNameLower = connection.name.toLowerCase();
    const connIdLower = connection.id.toLowerCase();

    return logs.filter((log) => {
      if (!log.database) return false;
      const logDbLower = log.database.toLowerCase();
      return logDbLower === connNameLower || logDbLower === connIdLower;
    });
  }, [logs, connection.name, connection.id]);

  // Apply search query and status filter
  const filteredQueries = useMemo(() => {
    return dbQueries.filter((log) => {
      if (filterErrorOnly && (log.status < 400 && !log.error)) {
        return false;
      }
      if (!searchQuery.trim()) return true;
      const q = searchQuery.toLowerCase();
      const sqlMatch = log.sql ? log.sql.toLowerCase().includes(q) : false;
      const ipMatch = log.clientIp ? log.clientIp.toLowerCase().includes(q) : false;
      const errorMatch = log.error ? log.error.toLowerCase().includes(q) : false;
      return sqlMatch || ipMatch || errorMatch;
    });
  }, [dbQueries, searchQuery, filterErrorOnly]);

  // Stats calculation
  const totalCount = dbQueries.length;
  const errorCount = dbQueries.filter((l) => l.status >= 400 || !!l.error).length;
  const avgLatency = useMemo(() => {
    if (dbQueries.length === 0) return 0;
    const sum = dbQueries.reduce((acc, curr) => acc + (curr.elapsedMs || 0), 0);
    return Math.round(sum / dbQueries.length);
  }, [dbQueries]);

  if (!isOpen) return null;

  const handleCopySql = (text: string, id: string) => {
    navigator.clipboard.writeText(text);
    setCopiedId(id);
    setTimeout(() => setCopiedId(null), 2000);
  };

  const dbType =
    connection.type ||
    (connection.database.endsWith('.sqlite') || connection.database.endsWith('.db')
      ? 'SQLite'
      : connection.port === 5432
      ? 'PostgreSQL'
      : connection.port === 3306
      ? 'MySQL'
      : 'Firebird');

  const sampleCurl = `curl -X POST http://127.0.0.1:8080/query \\
  -H "X-API-Key: YOUR_API_KEY" \\
  -H "Content-Type: application/json" \\
  -d '{
    "database": "${connection.name}",
    "sql": "SELECT 1 AS STATUS",
    "maxRows": 50
  }'`;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-stone-900/40 backdrop-blur-xs p-3 sm:p-4 overflow-y-auto">
      <div
        className="bg-white rounded-2xl max-w-2xl w-full max-h-[90vh] flex flex-col shadow-2xl border border-stone-200 animate-in fade-in zoom-in-95 duration-150 overflow-hidden"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Modal Header */}
        <div className="p-5 border-b border-stone-100 flex items-start justify-between gap-4 bg-stone-50/50 shrink-0">
          <div className="flex items-center gap-3">
            <div className="p-2.5 rounded-xl bg-stone-900 text-white shadow-xs">
              <Terminal className="w-5 h-5" />
            </div>
            <div>
              <div className="flex items-center gap-2 flex-wrap">
                <h2 className="text-lg font-bold text-stone-900 leading-tight">
                  {language === 'ar'
                    ? `استعلامات ${connection.name}`
                    : `Queries for ${connection.name}`}
                </h2>
                <span className="px-2 py-0.5 rounded text-[11px] font-mono font-semibold bg-stone-200 text-stone-800">
                  {dbType}
                </span>
                <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-100 text-emerald-800 flex items-center gap-1">
                  <ShieldCheck className="w-3 h-3" />
                  {language === 'ar' ? 'قراءة فقط (لا يوجد تعديل)' : 'Read-Only (No Modification)'}
                </span>
              </div>
              <p className="text-xs text-stone-500 mt-0.5 font-mono">
                {connection.server}:{connection.port} • {connection.database}
              </p>
            </div>
          </div>

          <button
            onClick={onClose}
            className="p-1.5 rounded-lg text-stone-400 hover:text-stone-700 hover:bg-stone-200 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Stats Strip */}
        <div className="px-5 py-2.5 bg-stone-100/60 border-b border-stone-200/60 flex items-center justify-between text-xs text-stone-600 flex-wrap gap-2 shrink-0">
          <div className="flex items-center gap-4 flex-wrap">
            <span className="font-semibold text-stone-800">
              {language === 'ar' ? 'إجمالي الاستعلامات:' : 'Total Queries:'}{' '}
              <strong className="text-stone-950 font-mono">{totalCount}</strong>
            </span>
            <span className="text-stone-300">•</span>
            <span>
              {language === 'ar' ? 'متوسط الاستجابة:' : 'Avg Latency:'}{' '}
              <strong className="text-stone-950 font-mono">{avgLatency}ms</strong>
            </span>
            <span className="text-stone-300">•</span>
            <span>
              {language === 'ar' ? 'أخطاء:' : 'Errors:'}{' '}
              <strong className={errorCount > 0 ? 'text-rose-600 font-mono' : 'text-emerald-700 font-mono'}>
                {errorCount}
              </strong>
            </span>
          </div>

          <div className="text-[11px] font-medium text-stone-500">
            {language === 'ar'
              ? 'يتم قبول استعلامات SELECT فقط عبر /query'
              : 'SELECT queries only via /query'}
          </div>
        </div>

        {/* Filter Bar */}
        <div className="p-4 border-b border-stone-100 flex items-center gap-2 shrink-0 bg-white">
          <div className="relative flex-1">
            <Search className="w-4 h-4 text-stone-400 absolute left-3 top-1/2 -translate-y-1/2 rtl:left-auto rtl:right-3" />
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder={
                language === 'ar'
                  ? 'ابحث في نص الاستعلام، أو عنوان IP...'
                  : 'Search SQL query text, client IP...'
              }
              className="w-full text-xs rounded-lg border border-stone-200 pl-9 pr-3 py-2 rtl:pl-3 rtl:pr-9 focus:outline-none focus:ring-1 focus:ring-stone-400 bg-stone-50"
            />
            {searchQuery && (
              <button
                onClick={() => setSearchQuery('')}
                className="absolute right-2.5 top-1/2 -translate-y-1/2 rtl:right-auto rtl:left-2.5 text-stone-400 hover:text-stone-600 p-0.5"
              >
                <X className="w-3.5 h-3.5" />
              </button>
            )}
          </div>

          {errorCount > 0 && (
            <button
              onClick={() => setFilterErrorOnly(!filterErrorOnly)}
              className={`px-3 py-2 text-xs font-semibold rounded-lg border transition-colors flex items-center gap-1.5 ${
                filterErrorOnly
                  ? 'bg-rose-50 border-rose-300 text-rose-700'
                  : 'bg-stone-50 border-stone-200 text-stone-600 hover:bg-stone-100'
              }`}
            >
              <AlertCircle className="w-3.5 h-3.5" />
              <span>{language === 'ar' ? 'الأخطاء فقط' : 'Errors only'}</span>
            </button>
          )}
        </div>

        {/* Query List Area */}
        <div className="flex-1 overflow-y-auto p-4 space-y-3 min-h-[220px]">
          {filteredQueries.length === 0 ? (
            <div className="py-8 px-4 text-center">
              <div className="w-12 h-12 rounded-full bg-stone-100 flex items-center justify-center mx-auto mb-3 text-stone-400">
                <Terminal className="w-6 h-6" />
              </div>
              <h3 className="text-sm font-bold text-stone-800">
                {totalCount === 0
                  ? language === 'ar'
                    ? 'لا توجد استعلامات منفذة لقاعدة البيانات هذه حتى الآن'
                    : 'No queries executed for this database yet'
                  : language === 'ar'
                  ? 'لا توجد نتائج تطابق بحثك'
                  : 'No queries match your search'}
              </h3>
              <p className="text-xs text-stone-500 mt-1 max-w-md mx-auto">
                {totalCount === 0
                  ? language === 'ar'
                    ? 'يمكن للتطبيقات والخدمات إرسال استعلامات قراءة (SELECT) باستخدام نقطة النهاية /query ومفتاح API.'
                    : 'Applications can query this database using POST /query with an authenticated API key.'
                  : language === 'ar'
                  ? 'جرّب تعديل كلمات البحث أو إلغاء فلتر الأخطاء.'
                  : 'Try adjusting your search terms or clearing filters.'}
              </p>

              {totalCount === 0 && (
                <div className="mt-4 max-w-md mx-auto text-left rtl:text-right">
                  <div className="text-[11px] font-semibold text-stone-600 mb-1 flex items-center justify-between">
                    <span>{language === 'ar' ? 'مثال curl للاستعلام:' : 'Sample curl query:'}</span>
                    <button
                      onClick={() => handleCopySql(sampleCurl, 'sample-curl')}
                      className="text-stone-500 hover:text-stone-900 inline-flex items-center gap-1 text-[10px]"
                    >
                      {copiedId === 'sample-curl' ? (
                        <>
                          <Check className="w-3 h-3 text-emerald-600" />
                          <span className="text-emerald-700">Copied</span>
                        </>
                      ) : (
                        <>
                          <Copy className="w-3 h-3" />
                          <span>Copy</span>
                        </>
                      )}
                    </button>
                  </div>
                  <pre className="p-2.5 rounded-lg bg-stone-900 text-stone-100 font-mono text-[11px] overflow-x-auto whitespace-pre leading-relaxed">
                    <code>{sampleCurl}</code>
                  </pre>
                </div>
              )}
            </div>
          ) : (
            filteredQueries.map((log) => {
              const isErr = log.status >= 400 || !!log.error;
              const timeString = new Date(log.at).toLocaleTimeString(
                language === 'ar' ? 'ar-SA' : 'en-US',
                { hour: '2-digit', minute: '2-digit', second: '2-digit' }
              );

              return (
                <div
                  key={log.id}
                  className={`rounded-xl border p-3.5 transition-all space-y-2.5 ${
                    isErr
                      ? 'bg-rose-50/40 border-rose-200'
                      : 'bg-white border-stone-200 shadow-2xs hover:border-stone-300'
                  }`}
                >
                  {/* Top metadata row */}
                  <div className="flex items-center justify-between gap-2 flex-wrap">
                    <div className="flex items-center gap-2">
                      <span
                        className={`px-2 py-0.5 rounded text-[10px] font-mono font-bold ${
                          isErr
                            ? 'bg-rose-100 text-rose-800'
                            : 'bg-emerald-100 text-emerald-800'
                        }`}
                      >
                        {log.status || (isErr ? 500 : 200)}
                      </span>
                      <span className="font-mono text-xs font-semibold text-stone-700">
                        {log.method} {log.path}
                      </span>
                    </div>

                    <div className="flex items-center gap-2.5 text-[11px] text-stone-500 font-mono">
                      <span className="flex items-center gap-1">
                        <Clock className="w-3 h-3 text-stone-400" />
                        {timeString}
                      </span>
                      <span className="text-stone-300">•</span>
                      <span className="flex items-center gap-1">
                        <Zap className="w-3 h-3 text-stone-400" />
                        {log.elapsedMs}ms
                      </span>
                      <span className="text-stone-300">•</span>
                      <span>IP: {log.clientIp}</span>
                    </div>
                  </div>

                  {/* SQL Statement Box */}
                  {log.sql && (
                    <div className="relative group">
                      <pre className="p-2.5 rounded-lg bg-stone-900 text-stone-100 font-mono text-xs overflow-x-auto whitespace-pre-wrap leading-relaxed selection:bg-stone-700">
                        <code>{log.sql}</code>
                      </pre>
                      <button
                        onClick={() => handleCopySql(log.sql!, log.id)}
                        className="absolute right-2 top-2 opacity-0 group-hover:opacity-100 transition-opacity p-1.5 rounded bg-stone-800 hover:bg-stone-700 text-stone-300 shadow-xs"
                        title={language === 'ar' ? 'نسخ نص الاستعلام' : 'Copy SQL'}
                      >
                        {copiedId === log.id ? (
                          <Check className="w-3.5 h-3.5 text-emerald-400" />
                        ) : (
                          <Copy className="w-3.5 h-3.5" />
                        )}
                      </button>
                    </div>
                  )}

                  {/* Result row or Error display */}
                  <div className="flex items-center justify-between text-xs pt-1 border-t border-stone-100">
                    {log.error ? (
                      <span className="text-rose-600 font-medium flex items-center gap-1">
                        <AlertCircle className="w-3.5 h-3.5 shrink-0" />
                        {log.error}
                      </span>
                    ) : (
                      <span className="text-stone-600 font-mono text-[11px]">
                        {log.rows !== undefined
                          ? `${log.rows} ${language === 'ar' ? 'صف مسترجع' : 'rows returned'}`
                          : language === 'ar'
                          ? 'استعلام ناجح'
                          : 'Success'}
                      </span>
                    )}

                    <span className="text-[10px] text-stone-400 font-mono">
                      ID: {log.id.slice(0, 8)}
                    </span>
                  </div>
                </div>
              );
            })
          )}
        </div>

        {/* Modal Footer */}
        <div className="p-4 border-t border-stone-200/80 bg-stone-50 flex items-center justify-between shrink-0">
          <div className="text-[11px] text-stone-500 flex items-center gap-1.5">
            <span className="w-1.5 h-1.5 rounded-full bg-emerald-500" />
            <span>
              {language === 'ar'
                ? 'لا يوجد خيار تعديل — البوابة للاستعلامات فقط'
                : 'No modification option — Gateway is strictly for queries'}
            </span>
          </div>

          <button
            onClick={onClose}
            className="px-4 py-2 text-xs font-semibold rounded-lg bg-stone-900 text-white hover:bg-stone-800 transition-colors shadow-2xs"
          >
            {t.done}
          </button>
        </div>
      </div>
    </div>
  );
};
