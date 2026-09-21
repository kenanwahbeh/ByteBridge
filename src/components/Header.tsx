import React from 'react';
import { Plus, Settings, Globe, Shield, RefreshCw, Server } from 'lucide-react';
import { translations } from '../localization/translations';

interface HeaderProps {
  language: 'en' | 'ar';
  onLanguageChange: (lang: 'en' | 'ar') => void;
  onAddData: () => void;
  onOpenSettings: () => void;
  onRefresh: () => void;
  isRefreshing?: boolean;
  isWebServerMode?: boolean;
  onScrollToWebServer?: () => void;
}

export const Header: React.FC<HeaderProps> = ({
  language,
  onLanguageChange,
  onAddData,
  onOpenSettings,
  onRefresh,
  isRefreshing,
  isWebServerMode,
  onScrollToWebServer,
}) => {
  const t = translations[language];

  return (
    <header className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 pb-6 border-b border-stone-200">
      <div className="flex items-center gap-3">
        <div className={`w-10 h-10 rounded-lg flex items-center justify-center font-bold tracking-tight shadow-sm transition-colors ${
          isWebServerMode 
            ? 'bg-blue-600 text-white ring-2 ring-blue-300' 
            : 'bg-stone-900 text-stone-50'
        }`}>
          {isWebServerMode ? (
            <Server className="w-5 h-5 text-white animate-pulse" />
          ) : (
            <Shield className="w-5 h-5 text-emerald-400" />
          )}
        </div>
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-2xl font-bold tracking-tight text-stone-900 leading-none">
              {t.appTitle}
            </h1>
            {isWebServerMode && (
              <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-bold bg-blue-100 text-blue-800 border border-blue-300">
                Cloudflare Dual Tunnel
              </span>
            )}
          </div>
          <p className="text-xs text-stone-700 mt-1 font-medium">
            {t.appSubtitle}
          </p>
        </div>
      </div>

      <div className="flex items-center flex-wrap gap-2">
        {onScrollToWebServer && (
          <button
            onClick={onScrollToWebServer}
            className={`flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-md border transition-all shadow-xs ${
              isWebServerMode
                ? 'bg-blue-50 text-blue-700 border-blue-200 hover:bg-blue-100'
                : 'bg-white text-stone-700 border-stone-300 hover:bg-stone-100'
            }`}
          >
            <Server className="w-3.5 h-3.5 text-blue-600" />
            <span>{isWebServerMode ? (language === 'ar' ? 'خادم ويب نشط' : 'Web Server Active') : (language === 'ar' ? 'تحويل لخادم ويب' : 'Web Server Mode')}</span>
          </button>
        )}

        <button
          onClick={onRefresh}
          className="p-2 rounded-md border border-stone-300 bg-white text-stone-700 hover:bg-stone-100 transition-colors shadow-xs"
          title="Refresh"
        >
          <RefreshCw className={`w-4 h-4 ${isRefreshing ? 'animate-spin' : ''}`} />
        </button>

        <div className="flex items-center rounded-md border border-stone-300 bg-white p-0.5 shadow-xs">
          <Globe className="w-3.5 h-3.5 ml-2 mr-1 text-stone-400" />
          <button
            onClick={() => onLanguageChange('en')}
            className={`px-2 py-1 text-xs font-semibold rounded ${
              language === 'en' ? 'bg-stone-900 text-white' : 'text-stone-600 hover:text-stone-900'
            }`}
          >
            EN
          </button>
          <button
            onClick={() => onLanguageChange('ar')}
            className={`px-2 py-1 text-xs font-semibold rounded ${
              language === 'ar' ? 'bg-stone-900 text-white' : 'text-stone-600 hover:text-stone-900'
            }`}
          >
            العربية
          </button>
        </div>

        <button
          onClick={onOpenSettings}
          className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-md border border-stone-300 bg-white text-stone-700 hover:bg-stone-100 transition-colors shadow-xs"
        >
          <Settings className="w-3.5 h-3.5 text-stone-500" />
          <span>{t.settings}</span>
        </button>

        <button
          onClick={onAddData}
          className="flex items-center gap-1.5 px-4 py-2 text-xs font-semibold rounded-md bg-stone-900 text-stone-50 hover:bg-stone-800 transition-colors shadow-xs"
        >
          <Plus className="w-4 h-4" />
          <span>{t.addData}</span>
        </button>
      </div>
    </header>
  );
};
