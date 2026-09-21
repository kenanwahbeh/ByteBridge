import React, { useState } from 'react';
import { Play, Database, Code, CheckCircle2, AlertTriangle, Clock, Layers, FileJson, Table } from 'lucide-react';
import type { DatabaseConfig, QueryResponse, ExecuteResponse } from '../types';
import { translations } from '../localization/translations';

interface QueryConsoleProps {
  connections: DatabaseConfig[];
  apiKey: string;
  language: 'en' | 'ar';
}

export const QueryConsole: React.FC<QueryConsoleProps> = ({
  connections,
  apiKey,
  language,
}) => {
  const t = translations[language];

  const onlineConnections = connections.filter(c => c.enabled);
  const [selectedDb, setSelectedDb] = useState<string>(
    onlineConnections[0]?.name || connections[0]?.name || ''
  );
  const [mode, setMode] = useState<'query' | 'execute'>('query');
  const [sql, setSql] = useState(
    'SELECT ID, NAME, EMAIL, BALANCE, COUNTRY FROM CUSTOMERS WHERE ID = @id'
  );
  const [paramsJson, setParamsJson] = useState('{\n  "id": 101\n}');
  const [loading, setLoading] = useState(false);
  const [queryResult, setQueryResult] = useState<QueryResponse | null>(null);
  const [executeResult, setExecuteResult] = useState<ExecuteResponse | null>(null);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [viewMode, setViewMode] = useState<'table' | 'json'>('table');

  const presets = [
    {
      name: 'All Customers',
      db: 'Sales',
      mode: 'query' as const,
      sql: 'SELECT ID, NAME, EMAIL, BALANCE, COUNTRY FROM CUSTOMERS',
      params: '{}',
    },
    {
      name: 'Customer by @id',
      db: 'Sales',
      mode: 'query' as const,
      sql: 'SELECT ID, NAME, BALANCE FROM CUSTOMERS WHERE ID = @id',
      params: '{\n  "id": 101\n}',
    },
    {
      name: 'All Orders',
      db: 'Sales',
      mode: 'query' as const,
      sql: 'SELECT ID, CUSTOMER_ID, ORDER_NUM, TOTAL_AMOUNT, STATUS FROM ORDERS',
      params: '{}',
    },
    {
      name: 'All Products',
      db: 'Inventory',
      mode: 'query' as const,
      sql: 'SELECT ID, SKU, NAME, STOCK_QTY, UNIT_PRICE, CATEGORY FROM PRODUCTS',
      params: '{}',
    },
    {
      name: 'Update Customer Balance',
      db: 'Sales',
      mode: 'execute' as const,
      sql: 'UPDATE CUSTOMERS SET BALANCE = @balance WHERE ID = @id',
      params: '{\n  "id": 101,\n  "balance": 15200.00\n}',
    },
  ];

  const handleRun = async () => {
    if (!selectedDb) {
      setErrorMsg('Please select a target database.');
      return;
    }

    let parsedParams: Record<string, any> = {};
    if (paramsJson.trim()) {
      try {
        parsedParams = JSON.parse(paramsJson);
      } catch (e: any) {
        setErrorMsg('Invalid Parameters JSON: ' + e.message);
        return;
      }
    }

    setLoading(true);
    setErrorMsg(null);
    setQueryResult(null);
    setExecuteResult(null);

    try {
      const endpoint = mode === 'query' ? '/query' : '/execute';
      const res = await fetch(endpoint, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-API-Key': apiKey,
        },
        body: JSON.stringify({
          database: selectedDb,
          sql,
          parameters: parsedParams,
          maxRows: 1000,
        }),
      });

      const data = await res.json();
      if (!res.ok) {
        throw new Error(data.error || `HTTP ${res.status}`);
      }

      if (mode === 'query') {
        setQueryResult(data as QueryResponse);
      } else {
        setExecuteResult(data as ExecuteResponse);
      }
    } catch (err: any) {
      setErrorMsg(err.message || 'Error occurred while querying gateway.');
    } finally {
      setLoading(false);
    }
  };

  const applyPreset = (preset: (typeof presets)[0]) => {
    setSelectedDb(preset.db);
    setMode(preset.mode);
    setSql(preset.sql);
    setParamsJson(preset.params);
    setErrorMsg(null);
  };

  return (
    <div className="bg-white rounded-xl border border-stone-200 p-6 shadow-xs space-y-5">
      <div>
        <h2 className="text-base font-bold text-stone-900">{t.queryConsoleTitle}</h2>
        <p className="text-xs text-stone-500 mt-0.5">{t.queryConsoleDesc}</p>
      </div>

      {/* Preset buttons */}
      <div className="flex items-center gap-1.5 flex-wrap">
        <span className="text-xs font-semibold text-stone-400 mr-1">Sample Queries:</span>
        {presets.map((p, idx) => (
          <button
            key={idx}
            onClick={() => applyPreset(p)}
            className="text-[11px] font-medium px-2.5 py-1 rounded-md bg-stone-100 hover:bg-stone-200 text-stone-700 transition-colors border border-stone-200"
          >
            {p.name}
          </button>
        ))}
      </div>

      {/* Console Controls */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
        {/* Database Selector */}
        <div>
          <label className="block text-xs font-semibold text-stone-700 mb-1 flex items-center gap-1.5">
            <Database className="w-3.5 h-3.5 text-stone-400" />
            <span>Target Database</span>
          </label>
          <select
            value={selectedDb}
            onChange={e => setSelectedDb(e.target.value)}
            className="w-full text-xs font-medium px-3 py-2 rounded-lg border border-stone-300 bg-white text-stone-900 focus:outline-none focus:ring-1 focus:ring-stone-400"
          >
            {connections.map(c => (
              <option key={c.id} value={c.name}>
                {c.name} {c.enabled ? '(Online)' : '(Offline)'}
              </option>
            ))}
          </select>
        </div>

        {/* Mode Selector */}
        <div>
          <label className="block text-xs font-semibold text-stone-700 mb-1 flex items-center gap-1.5">
            <Layers className="w-3.5 h-3.5 text-stone-400" />
            <span>Endpoint Mode</span>
          </label>
          <div className="grid grid-cols-2 rounded-lg border border-stone-300 p-0.5 bg-stone-50">
            <button
              onClick={() => setMode('query')}
              className={`text-xs py-1.5 font-semibold rounded-md transition-colors ${
                mode === 'query'
                  ? 'bg-white text-stone-900 shadow-xs'
                  : 'text-stone-500 hover:text-stone-800'
              }`}
            >
              /query (Read)
            </button>
            <button
              onClick={() => setMode('execute')}
              className={`text-xs py-1.5 font-semibold rounded-md transition-colors ${
                mode === 'execute'
                  ? 'bg-white text-stone-900 shadow-xs'
                  : 'text-stone-500 hover:text-stone-800'
              }`}
            >
              /execute (Write)
            </button>
          </div>
        </div>

        {/* Action Button */}
        <div className="flex items-end">
          <button
            onClick={handleRun}
            disabled={loading || !selectedDb}
            className="w-full flex items-center justify-center gap-2 px-4 py-2 rounded-lg bg-stone-900 text-white hover:bg-stone-800 text-xs font-semibold shadow-xs disabled:opacity-50 transition-colors"
          >
            <Play className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : 'fill-white'}`} />
            <span>{loading ? t.running : t.runQuery}</span>
          </button>
        </div>
      </div>

      {/* SQL & Parameters Input Area */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-3">
        <div className="lg:col-span-2">
          <div className="flex items-center justify-between mb-1">
            <label className="text-xs font-semibold text-stone-700 flex items-center gap-1.5">
              <Code className="w-3.5 h-3.5 text-stone-400" />
              <span>SQL Statement</span>
            </label>
            <span className="text-[10px] text-stone-400">
              {mode === 'query' ? 'SELECT or WITH statements only' : 'INSERT / UPDATE / DELETE'}
            </span>
          </div>
          <textarea
            rows={4}
            value={sql}
            onChange={e => setSql(e.target.value)}
            className="w-full font-mono text-xs p-3 rounded-lg border border-stone-300 text-stone-900 bg-stone-50/50 focus:bg-white focus:outline-none focus:ring-1 focus:ring-stone-400"
            placeholder="SELECT * FROM ..."
          />
        </div>

        <div>
          <div className="flex items-center justify-between mb-1">
            <label className="text-xs font-semibold text-stone-700">
              {t.parametersJson}
            </label>
            <span className="text-[10px] text-stone-400">Named parameters (@key)</span>
          </div>
          <textarea
            rows={4}
            value={paramsJson}
            onChange={e => setParamsJson(e.target.value)}
            className="w-full font-mono text-xs p-3 rounded-lg border border-stone-300 text-stone-900 bg-stone-50/50 focus:bg-white focus:outline-none focus:ring-1 focus:ring-stone-400"
            placeholder='{ "id": 101 }'
          />
        </div>
      </div>

      {/* Error Alert */}
      {errorMsg && (
        <div className="p-3 rounded-lg bg-rose-50 border border-rose-200 text-rose-800 text-xs font-medium flex items-start gap-2">
          <AlertTriangle className="w-4 h-4 text-rose-600 shrink-0 mt-0.5" />
          <span>{errorMsg}</span>
        </div>
      )}

      {/* Output Results View */}
      {(queryResult || executeResult) && (
        <div className="border border-stone-200 rounded-xl overflow-hidden bg-stone-50/30">
          <div className="flex items-center justify-between px-4 py-2.5 border-b border-stone-200 bg-stone-100/70">
            <div className="flex items-center gap-3">
              <span className="text-xs font-bold text-stone-800">{t.resultsTitle}</span>
              {queryResult && (
                <span className="inline-flex items-center gap-1.5 text-xs text-stone-600">
                  <CheckCircle2 className="w-3.5 h-3.5 text-emerald-600" />
                  <span>
                    {t.rowsReturned
                      .replace('{0}', String(queryResult.rowCount))
                      .replace('{1}', String(queryResult.elapsedMs))}
                  </span>
                </span>
              )}
              {executeResult && (
                <span className="inline-flex items-center gap-1.5 text-xs text-stone-600">
                  <CheckCircle2 className="w-3.5 h-3.5 text-emerald-600" />
                  <span>
                    {t.rowsAffected
                      .replace('{0}', String(executeResult.rowsAffected))
                      .replace('{1}', String(executeResult.elapsedMs))}
                  </span>
                </span>
              )}
            </div>

            <div className="flex items-center gap-1">
              <button
                onClick={() => setViewMode('table')}
                className={`p-1.5 rounded-md text-xs font-medium transition-colors ${
                  viewMode === 'table' ? 'bg-white shadow-2xs text-stone-900' : 'text-stone-500'
                }`}
                title="Table View"
              >
                <Table className="w-3.5 h-3.5" />
              </button>
              <button
                onClick={() => setViewMode('json')}
                className={`p-1.5 rounded-md text-xs font-medium transition-colors ${
                  viewMode === 'json' ? 'bg-white shadow-2xs text-stone-900' : 'text-stone-500'
                }`}
                title="JSON View"
              >
                <FileJson className="w-3.5 h-3.5" />
              </button>
            </div>
          </div>

          {/* Table or JSON Render */}
          <div className="p-4 overflow-x-auto max-h-80">
            {viewMode === 'json' ? (
              <pre className="text-xs font-mono text-stone-800 whitespace-pre-wrap">
                {JSON.stringify(queryResult || executeResult, null, 2)}
              </pre>
            ) : queryResult ? (
              <table className="w-full text-left border-collapse text-xs font-mono">
                <thead>
                  <tr className="border-b border-stone-200 bg-stone-100/50">
                    {queryResult.columns.map((col, i) => (
                      <th key={i} className="px-3 py-2 font-semibold text-stone-700">
                        {col}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-stone-200/60">
                  {queryResult.rows.map((row, rIdx) => (
                    <tr key={rIdx} className="hover:bg-stone-100/40">
                      {row.map((val, cIdx) => (
                        <td key={cIdx} className="px-3 py-1.5 text-stone-800 truncate max-w-xs">
                          {val === null ? (
                            <span className="text-stone-400 italic">null</span>
                          ) : typeof val === 'boolean' ? (
                            val ? (
                              'true'
                            ) : (
                              'false'
                            )
                          ) : (
                            String(val)
                          )}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            ) : (
              <div className="text-xs font-mono text-stone-700 py-2">
                Rows Affected: <strong className="text-stone-900">{executeResult?.rowsAffected}</strong>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
};
