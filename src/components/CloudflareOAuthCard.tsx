import React, { useState } from 'react';
import { Cloud, ShieldCheck, ChevronDown, ChevronUp } from 'lucide-react';
import type { OAuthConfig } from '../types';
import { translations } from '../localization/translations';

interface CloudflareOAuthCardProps {
  config: OAuthConfig;
  language: 'en' | 'ar';
  onSave: (newConfig: Partial<OAuthConfig>) => Promise<void>;
}

export const CloudflareOAuthCard: React.FC<CloudflareOAuthCardProps> = ({
  config,
  language,
  onSave,
}) => {
  const t = translations[language];
  const [isOpen, setIsOpen] = useState(false);
  const [teamDomain, setTeamDomain] = useState(config.teamDomain);
  const [audience, setAudience] = useState(config.audience);
  const [saving, setSaving] = useState(false);

  const toggleOAuth = async () => {
    setSaving(true);
    try {
      await onSave({
        enabled: !config.enabled,
        teamDomain,
        audience,
      });
    } finally {
      setSaving(false);
    }
  };

  const handleBlurSave = async () => {
    if (teamDomain !== config.teamDomain || audience !== config.audience) {
      await onSave({ teamDomain, audience });
    }
  };

  return (
    <div className="bg-white rounded-xl border border-stone-200 overflow-hidden shadow-xs transition-all">
      <div
        onClick={() => setIsOpen(!isOpen)}
        className="p-4 flex items-center justify-between cursor-pointer hover:bg-stone-50 transition-colors"
      >
        <div className="flex items-center gap-3">
          <div className="p-2 rounded-lg bg-orange-50 text-orange-600 border border-orange-100">
            <Cloud className="w-4 h-4" />
          </div>
          <div>
            <div className="flex items-center gap-2">
              <h3 className="text-sm font-bold text-stone-900">{t.cloudflareOAuth}</h3>
              {config.enabled ? (
                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-50 text-emerald-700 border border-emerald-200">
                  <ShieldCheck className="w-3 h-3" />
                  {t.enabled}
                </span>
              ) : (
                <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-stone-100 text-stone-600 border border-stone-200">
                  {t.notConfigured}
                </span>
              )}
            </div>
            <p className="text-xs text-stone-500 mt-0.5">{t.cloudflareOAuthDesc}</p>
          </div>
        </div>

        <button className="text-stone-400 hover:text-stone-600 p-1">
          {isOpen ? <ChevronUp className="w-4 h-4" /> : <ChevronDown className="w-4 h-4" />}
        </button>
      </div>

      {isOpen && (
        <div className="p-4 pt-2 border-t border-stone-100 bg-stone-50/50 space-y-3">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-semibold text-stone-600 mb-1">
                {t.teamDomain}
              </label>
              <input
                type="text"
                placeholder="my-team.cloudflareaccess.com"
                value={teamDomain}
                onChange={e => setTeamDomain(e.target.value)}
                onBlur={handleBlurSave}
                className="w-full text-xs font-mono px-3 py-2 rounded-lg border border-stone-300 bg-white text-stone-900 focus:outline-none focus:ring-1 focus:ring-stone-400"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold text-stone-600 mb-1">
                {t.audience}
              </label>
              <input
                type="text"
                placeholder="Access application audience tag"
                value={audience}
                onChange={e => setAudience(e.target.value)}
                onBlur={handleBlurSave}
                className="w-full text-xs font-mono px-3 py-2 rounded-lg border border-stone-300 bg-white text-stone-900 focus:outline-none focus:ring-1 focus:ring-stone-400"
              />
            </div>
          </div>

          <div className="flex justify-end pt-1">
            <button
              onClick={toggleOAuth}
              disabled={saving}
              className={`px-4 py-1.5 text-xs font-semibold rounded-lg transition-colors shadow-2xs ${
                config.enabled
                  ? 'bg-stone-200 text-stone-800 hover:bg-stone-300'
                  : 'bg-stone-900 text-white hover:bg-stone-800'
              }`}
            >
              {config.enabled ? t.disable : t.enable}
            </button>
          </div>
        </div>
      )}
    </div>
  );
};
