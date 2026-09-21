import React, { useState } from 'react';
import { X, Globe, Power } from 'lucide-react';
import { translations } from '../localization/translations';

interface SettingsModalProps {
  isOpen: boolean;
  language: 'en' | 'ar';
  autoStart: boolean;
  onClose: () => void;
  onSave: (settings: { language: 'en' | 'ar'; autoStart: boolean }) => Promise<void>;
}

export const SettingsModal: React.FC<SettingsModalProps> = ({
  isOpen,
  language,
  autoStart,
  onClose,
  onSave,
}) => {
  const t = translations[language];
  const [selectedLang, setSelectedLang] = useState<'en' | 'ar'>(language);
  const [selectedAutoStart, setSelectedAutoStart] = useState<boolean>(autoStart);
  const [saving, setSaving] = useState(false);

  if (!isOpen) return null;

  const handleDone = async () => {
    setSaving(true);
    try {
      await onSave({
        language: selectedLang,
        autoStart: selectedAutoStart,
      });
      onClose();
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-stone-900/40 backdrop-blur-xs p-4">
      <div className="bg-white rounded-2xl max-w-md w-full p-6 shadow-2xl border border-stone-200 space-y-5 animate-in fade-in zoom-in-95 duration-150">
        <div className="flex items-center justify-between pb-3 border-b border-stone-100">
          <h2 className="text-xl font-bold text-stone-900">{t.settingsTitle}</h2>
          <button
            onClick={onClose}
            className="p-1 rounded-lg text-stone-400 hover:text-stone-700 hover:bg-stone-100 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        <div className="space-y-4 text-xs font-sans">
          {/* Language Selection */}
          <div className="p-4 rounded-xl border border-stone-200 bg-stone-50/50 space-y-2">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Globe className="w-4 h-4 text-stone-500" />
                <span className="font-semibold text-stone-800">{t.language}</span>
              </div>
              <select
                value={selectedLang}
                onChange={e => setSelectedLang(e.target.value as 'en' | 'ar')}
                className="text-xs font-medium px-3 py-1.5 rounded-lg border border-stone-300 bg-white text-stone-900 focus:outline-none"
              >
                <option value="en">English</option>
                <option value="ar">العربية (Arabic)</option>
              </select>
            </div>
            <p className="text-[11px] text-stone-500">{t.languageHint}</p>
          </div>

          {/* Auto Start Selection */}
          <div className="p-4 rounded-xl border border-stone-200 bg-stone-50/50 space-y-2">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Power className="w-4 h-4 text-stone-500" />
                <span className="font-semibold text-stone-800">{t.autoStart}</span>
              </div>
              <input
                type="checkbox"
                checked={selectedAutoStart}
                onChange={e => setSelectedAutoStart(e.target.checked)}
                className="w-4 h-4 rounded text-stone-900 focus:ring-stone-500"
              />
            </div>
            <p className="text-[11px] text-stone-500">{t.autoStartHint}</p>
          </div>
        </div>

        <div className="flex justify-end pt-3 border-t border-stone-100">
          <button
            onClick={handleDone}
            disabled={saving}
            className="px-6 py-2 rounded-lg bg-stone-900 text-white hover:bg-stone-800 text-xs font-semibold shadow-xs disabled:opacity-50"
          >
            {saving ? 'Saving...' : t.done}
          </button>
        </div>
      </div>
    </div>
  );
};
