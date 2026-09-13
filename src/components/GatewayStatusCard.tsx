import React, { useState } from 'react';
import { Copy, KeyRound, Power, Check, AlertCircle } from 'lucide-react';
import type { GatewayConfig } from '../types';
import { translations } from '../localization/translations';

interface GatewayStatusCardProps {
  config: GatewayConfig;
  serviceState: 'running' | 'stopped' | 'pending';
  language: 'en' | 'ar';
  onRotateKey: () => Promise<void>;
  onToggleGateway: () => Promise<void>;
  onUpdatePort: (port: number) => Promise<void>;
}

export const GatewayStatusCard: React.FC<GatewayStatusCardProps> = ({
  config,
  serviceState,
  language,
  onRotateKey,
  onToggleGateway,
  onUpdatePort,
}) => {
  const t = translations[language];
  const [copied, setCopied] = useState(false);
  const [portValue, setPortValue] = useState(config.port.toString());
  const [isRotating, setIsRotating] = useState(false);
  const [showKeyConfirm, setShowKeyConfirm] = useState(false);

  const isAnswering = serviceState === 'running' && config.autoStart;

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(config.apiKey);
      setCopied(true);
      setTimeout(() => setCopied(false), 2500);
    } catch (e) {
      console.error('Failed to copy', e);
    }
  };

  const handlePortBlur = async () => {
    const p = parseInt(portValue, 10);
    if (!isNaN(p) && p > 0 && p <= 65535 && p !== config.port) {
      await onUpdatePort(p);
    } else {
      setPortValue(config.port.toString());
    }
  };

  const confirmRotate = async () => {
    setIsRotating(true);
    try {
      await onRotateKey();
      setShowKeyConfirm(false);
    } finally {
      setIsRotating(false);
    }
  };

  return (
    <div className="bg-white rounded-xl border border-stone-200 p-5 shadow-xs transition-all">
      <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-4">
        {/* Left Status Area */}
        <div className="space-y-1.5 flex-1 min-w-0">
          <div className="flex items-center gap-2">
            <h2 className="text-sm font-bold uppercase tracking-wider text-stone-700">
              {t.gatewayApi}
            </h2>
          </div>

          <div className="flex items-center gap-2 flex-wrap">
            {isAnswering ? (
              <span className="inline-flex items-center gap-1.5 text-sm font-semibold text-emerald-700">
                <span className="w-2.5 h-2.5 rounded-full bg-emerald-500 animate-pulse" />
                {t.answering.replace('{0}', config.baseUrl)}
              </span>
            ) : !config.autoStart ? (
              <span className="inline-flex items-center gap-1.5 text-sm font-semibold text-stone-500">
                <span className="w-2.5 h-2.5 rounded-full bg-stone-400" />
                {t.turnedOff}
              </span>
            ) : (
              <span className="inline-flex items-center gap-1.5 text-sm font-semibold text-rose-600">
                <span className="w-2.5 h-2.5 rounded-full bg-rose-500" />
                {t.notRunning}
              </span>
            )}

            <span className="text-stone-300">|</span>

            <span className="text-xs font-medium text-stone-500">
              {serviceState === 'running' ? t.serviceRunning : t.serviceStopped}
            </span>
          </div>

          <p className="text-xs text-stone-600 font-mono select-all bg-stone-50 border border-stone-200 rounded px-2 py-1 max-w-xl">
            {isAnswering
              ? t.tunnelHint.replace('{0}', config.baseUrl)
              : t.turnedOffHint}
          </p>
        </div>

        {/* Right Controls Area */}
        <div className="flex items-center flex-wrap gap-2 pt-2 lg:pt-0">
          <div className="flex items-center gap-1.5 bg-stone-50 border border-stone-200 rounded-lg px-2.5 py-1">
            <span className="text-xs font-semibold text-stone-500">{t.port}</span>
            <input
              type="number"
              min="1"
              max="65535"
              value={portValue}
              onChange={e => setPortValue(e.target.value)}
              onBlur={handlePortBlur}
              disabled={config.autoStart}
              className={`w-16 text-xs font-mono font-semibold text-stone-800 bg-transparent border-none focus:outline-none ${
                config.autoStart ? 'opacity-60 cursor-not-allowed' : ''
              }`}
            />
          </div>

          <button
            onClick={handleCopy}
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg border border-stone-300 bg-white text-stone-700 hover:bg-stone-50 transition-colors shadow-2xs"
            title="Copy API Key"
          >
            {copied ? (
              <>
                <Check className="w-3.5 h-3.5 text-emerald-600" />
                <span className="text-emerald-700">Copied!</span>
              </>
            ) : (
              <>
                <Copy className="w-3.5 h-3.5 text-stone-500" />
                <span>{t.copyApiKey}</span>
              </>
            )}
          </button>

          <button
            onClick={() => setShowKeyConfirm(true)}
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-lg border border-stone-300 bg-white text-stone-700 hover:bg-stone-50 transition-colors shadow-2xs"
            title="Rotate API Key"
          >
            <KeyRound className="w-3.5 h-3.5 text-stone-500" />
            <span>{t.newKey}</span>
          </button>

          <button
            onClick={onToggleGateway}
            className={`flex items-center gap-1.5 px-4 py-1.5 text-xs font-semibold rounded-lg transition-colors shadow-2xs ${
              config.autoStart
                ? 'bg-rose-50 text-rose-700 border border-rose-200 hover:bg-rose-100'
                : 'bg-emerald-600 text-white hover:bg-emerald-700'
            }`}
          >
            <Power className="w-3.5 h-3.5" />
            <span>{config.autoStart ? t.turnOff : t.turnOn}</span>
          </button>
        </div>
      </div>

      {/* Confirmation Modal for Key Rotation */}
      {showKeyConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-stone-900/40 backdrop-blur-xs p-4">
          <div className="bg-white rounded-xl max-w-md w-full p-6 shadow-xl border border-stone-200 space-y-4">
            <div className="flex items-start gap-3">
              <div className="p-2 rounded-lg bg-amber-50 text-amber-600 border border-amber-200">
                <AlertCircle className="w-5 h-5" />
              </div>
              <div>
                <h3 className="text-base font-bold text-stone-900">{t.newKey}</h3>
                <p className="text-xs text-stone-600 mt-1 leading-relaxed">
                  {t.newKeyConfirm}
                </p>
              </div>
            </div>

            <div className="flex items-center justify-end gap-2 pt-2 border-t border-stone-100">
              <button
                onClick={() => setShowKeyConfirm(false)}
                className="px-3 py-1.5 text-xs font-semibold rounded-lg border border-stone-300 text-stone-700 hover:bg-stone-100"
              >
                {t.cancel}
              </button>
              <button
                onClick={confirmRotate}
                disabled={isRotating}
                className="px-4 py-1.5 text-xs font-semibold rounded-lg bg-stone-900 text-white hover:bg-stone-800 disabled:opacity-50"
              >
                {isRotating ? 'Rotating...' : t.newKey}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
