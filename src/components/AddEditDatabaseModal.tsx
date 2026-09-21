import React, { useState, useEffect } from 'react';
import { X, CheckCircle2, XCircle, Activity, Lock } from 'lucide-react';
import type { DatabaseConfig } from '../types';
import { translations } from '../localization/translations';

interface AddEditDatabaseModalProps {
  isOpen: boolean;
  editingConnection?: DatabaseConfig | null;
  language: 'en' | 'ar';
  onClose: () => void;
  onSave: (data: Partial<DatabaseConfig>) => Promise<void>;
  onTest: (data: Partial<DatabaseConfig>) => Promise<{ success: boolean; latencyMs?: number; message?: string }>;
}

export const AddEditDatabaseModal: React.FC<AddEditDatabaseModalProps> = ({
  isOpen,
  editingConnection,
  language,
  onClose,
  onSave,
  onTest,
}) => {
  const t = translations[language];

  const [name, setName] = useState('');
  const [server, setServer] = useState('localhost');
  const [port, setPort] = useState('3050');
  const [username, setUsername] = useState('SYSDBA');
  const [password, setPassword] = useState('');
  const [database, setDatabase] = useState('');
  const [testing, setTesting] = useState(false);
  const [saving, setSaving] = useState(false);
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  useEffect(() => {
    if (editingConnection) {
      setName(editingConnection.name);
      setServer(editingConnection.server);
      setPort(String(editingConnection.port));
      setUsername(editingConnection.username);
      setPassword(editingConnection.password || '');
      setDatabase(editingConnection.database);
    } else {
      setName('');
      setServer('localhost');
      setPort('3050');
      setUsername('SYSDBA');
      setPassword('');
      setDatabase('');
    }
    setTestResult(null);
    setErrorMsg(null);
  }, [editingConnection, isOpen]);

  if (!isOpen) return null;

  const handleTestConnection = async () => {
    setErrorMsg(null);
    setTestResult(null);
    setTesting(true);
    try {
      const res = await onTest({
        server,
        port: parseInt(port, 10) || 3050,
        username,
        password,
        database,
      });
      setTestResult({
        success: res.success,
        message: res.success
          ? t.connectionSuccessful.replace('{0}', String(res.latencyMs || 10))
          : t.connectionFailed.replace('{0}', res.message || 'Check credentials'),
      });
    } catch (err: any) {
      setTestResult({
        success: false,
        message: err.message || 'Connection failed',
      });
    } finally {
      setTesting(false);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) {
      setErrorMsg('Please enter a Connection Name.');
      return;
    }
    if (!database.trim()) {
      setErrorMsg('Please enter a Database alias or file path.');
      return;
    }

    setSaving(true);
    try {
      await onSave({
        id: editingConnection?.id,
        name: name.trim(),
        server: server.trim() || 'localhost',
        port: parseInt(port, 10) || 3050,
        username: username.trim() || 'SYSDBA',
        password,
        database: database.trim(),
        enabled: editingConnection ? editingConnection.enabled : true,
      });
      onClose();
    } catch (err: any) {
      setErrorMsg(err.message || 'Failed to save connection.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-stone-900/40 backdrop-blur-xs p-4 overflow-y-auto">
      <div className="bg-white rounded-2xl max-w-lg w-full p-6 shadow-2xl border border-stone-200 space-y-5 animate-in fade-in zoom-in-95 duration-150">
        <div className="flex items-center justify-between pb-3 border-b border-stone-100">
          <h2 className="text-xl font-bold text-stone-900">
            {editingConnection ? t.edit : t.addData}
          </h2>
          <button
            onClick={onClose}
            className="p-1 rounded-lg text-stone-400 hover:text-stone-700 hover:bg-stone-100 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4 text-xs font-sans">
          <div>
            <label className="block font-semibold text-stone-700 mb-1">
              {t.connectionName}
            </label>
            <input
              type="text"
              required
              placeholder="Sales, Inventory, Warehouse..."
              value={name}
              onChange={e => setName(e.target.value)}
              className="w-full px-3 py-2 rounded-lg border border-stone-300 text-stone-900 text-sm focus:outline-none focus:ring-2 focus:ring-stone-900/10 focus:border-stone-500"
            />
          </div>

          <div className="grid grid-cols-3 gap-3">
            <div className="col-span-2">
              <label className="block font-semibold text-stone-700 mb-1">
                {t.server}
              </label>
              <input
                type="text"
                value={server}
                onChange={e => setServer(e.target.value)}
                className="w-full px-3 py-2 rounded-lg border border-stone-300 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-stone-900/10 focus:border-stone-500"
              />
            </div>
            <div>
              <label className="block font-semibold text-stone-700 mb-1">
                {t.port}
              </label>
              <input
                type="number"
                value={port}
                onChange={e => setPort(e.target.value)}
                className="w-full px-3 py-2 rounded-lg border border-stone-300 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-stone-900/10 focus:border-stone-500"
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block font-semibold text-stone-700 mb-1">
                {t.username}
              </label>
              <input
                type="text"
                value={username}
                onChange={e => setUsername(e.target.value)}
                className="w-full px-3 py-2 rounded-lg border border-stone-300 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-stone-900/10 focus:border-stone-500"
              />
            </div>
            <div>
              <label className="block font-semibold text-stone-700 mb-1">
                {t.password}
              </label>
              <div className="relative">
                <input
                  type="password"
                  placeholder="••••••••"
                  value={password}
                  onChange={e => setPassword(e.target.value)}
                  className="w-full px-3 py-2 rounded-lg border border-stone-300 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-stone-900/10 focus:border-stone-500"
                />
                <Lock className="w-3.5 h-3.5 text-stone-400 absolute right-3 top-3 pointer-events-none" />
              </div>
            </div>
          </div>

          <div>
            <label className="block font-semibold text-stone-700 mb-1">
              {t.database}
            </label>
            <input
              type="text"
              required
              placeholder="C:\data\sales.fdb or employee"
              value={database}
              onChange={e => setDatabase(e.target.value)}
              className="w-full px-3 py-2 rounded-lg border border-stone-300 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-stone-900/10 focus:border-stone-500"
            />
            <p className="text-[11px] text-stone-500 mt-1">{t.dbAliasOrPath}</p>
          </div>

          {/* Test connection alert banner */}
          {testResult && (
            <div
              className={`p-3 rounded-lg text-xs font-medium flex items-center gap-2 ${
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

          {errorMsg && (
            <div className="p-2.5 rounded-lg bg-rose-50 text-rose-700 text-xs font-medium border border-rose-200">
              {errorMsg}
            </div>
          )}

          <div className="flex items-center justify-between pt-4 border-t border-stone-100">
            <button
              type="button"
              onClick={handleTestConnection}
              disabled={testing}
              className="flex items-center gap-1.5 px-3 py-2 rounded-lg border border-stone-300 text-stone-700 hover:bg-stone-50 text-xs font-semibold shadow-2xs disabled:opacity-50"
            >
              <Activity className={`w-3.5 h-3.5 ${testing ? 'animate-spin' : ''}`} />
              <span>{testing ? t.testing : t.testConnection}</span>
            </button>

            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={onClose}
                className="px-4 py-2 rounded-lg border border-stone-300 text-stone-700 hover:bg-stone-50 text-xs font-semibold"
              >
                {t.cancel}
              </button>
              <button
                type="submit"
                disabled={saving}
                className="px-5 py-2 rounded-lg bg-stone-900 text-white hover:bg-stone-800 text-xs font-semibold shadow-xs disabled:opacity-50"
              >
                {saving ? 'Saving...' : t.save}
              </button>
            </div>
          </div>
        </form>
      </div>
    </div>
  );
};
