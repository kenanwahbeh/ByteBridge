import React, { useState } from 'react';
import { Terminal, Copy, Check, ExternalLink, Shield } from 'lucide-react';
import type { GatewayConfig } from '../types';

interface ApiQuickstartProps {
  config: GatewayConfig;
}

export const ApiQuickstart: React.FC<ApiQuickstartProps> = ({ config }) => {
  const [copiedIdx, setCopiedIdx] = useState<number | null>(null);

  const snippets = [
    {
      title: '1. Health Check (Unauthenticated)',
      desc: 'Verify that the gateway is running and answering requests.',
      cmd: `curl ${config.baseUrl}/health`,
    },
    {
      title: '2. List Databases (Authenticated)',
      desc: 'Retrieve configured databases and their online statuses.',
      cmd: `curl -H "X-API-Key: ${config.apiKey}" ${config.baseUrl}/databases`,
    },
    {
      title: '3. Run SQL Query (/query)',
      desc: 'Execute SELECT or WITH statements with named parameter bindings.',
      cmd: `curl -X POST ${config.baseUrl}/query \\\n  -H "X-API-Key: ${config.apiKey}" \\\n  -H "Content-Type: application/json" \\\n  -d '{\n    "database": "Sales",\n    "sql": "SELECT ID, NAME, BALANCE FROM CUSTOMERS WHERE ID = @id",\n    "parameters": { "id": 101 },\n    "maxRows": 100\n  }'`,
    },
    {
      title: '4. Execute Write Statement (/execute)',
      desc: 'Execute INSERT, UPDATE, or DELETE statements safely.',
      cmd: `curl -X POST ${config.baseUrl}/execute \\\n  -H "X-API-Key: ${config.apiKey}" \\\n  -H "Content-Type: application/json" \\\n  -d '{\n    "database": "Sales",\n    "sql": "UPDATE CUSTOMERS SET BALANCE = @bal WHERE ID = @id",\n    "parameters": { "id": 101, "bal": 15000.00 }\n  }'`,
    },
    {
      title: '5. Cloudflare Dual Tunnel (Data on :8080, Control Panel on :3000)',
      desc: 'Connect database traffic on 8080 and remote server administration on 3000 simultaneously through Cloudflare.',
      cmd: `# Step 1: Run Cloudflare Tunnel pointing to Data Gateway (:8080)\ncloudflared tunnel run --url http://127.0.0.1:8080\n\n# Step 2: Run Cloudflare Tunnel pointing to Web Control Panel (:3000)\ncloudflared tunnel run --url http://127.0.0.1:3000\n\n# Or single config.yml:\n# ingress:\n#   - hostname: db-gateway.example.com\n#     service: http://127.0.0.1:8080\n#   - hostname: panel.example.com\n#     service: http://127.0.0.1:3000`,
    },
  ];

  const handleCopy = (text: string, idx: number) => {
    navigator.clipboard.writeText(text);
    setCopiedIdx(idx);
    setTimeout(() => setCopiedIdx(null), 2500);
  };

  return (
    <div className="bg-white rounded-xl border border-stone-200 p-6 shadow-xs space-y-6">
      <div className="flex items-center gap-2.5 pb-2 border-b border-stone-100">
        <Terminal className="w-5 h-5 text-stone-700" />
        <div>
          <h2 className="text-base font-bold text-stone-900">HTTP API Reference & curl Examples</h2>
          <p className="text-xs text-stone-500">
            Copy and paste these commands into your terminal or script to query the gateway directly.
          </p>
        </div>
      </div>

      <div className="space-y-4">
        {snippets.map((snip, idx) => (
          <div key={idx} className="space-y-1.5">
            <div className="flex items-center justify-between">
              <div>
                <h3 className="text-xs font-bold text-stone-800">{snip.title}</h3>
                <p className="text-[11px] text-stone-500">{snip.desc}</p>
              </div>
              <button
                onClick={() => handleCopy(snip.cmd, idx)}
                className="flex items-center gap-1 text-[11px] font-semibold text-stone-600 hover:text-stone-900 px-2 py-1 rounded bg-stone-100 hover:bg-stone-200 transition-colors"
              >
                {copiedIdx === idx ? (
                  <>
                    <Check className="w-3.5 h-3.5 text-emerald-600" />
                    <span className="text-emerald-700">Copied</span>
                  </>
                ) : (
                  <>
                    <Copy className="w-3.5 h-3.5 text-stone-400" />
                    <span>Copy</span>
                  </>
                )}
              </button>
            </div>
            <pre className="p-3 rounded-lg bg-stone-900 text-stone-100 text-xs font-mono overflow-x-auto whitespace-pre leading-relaxed selection:bg-stone-700">
              <code>{snip.cmd}</code>
            </pre>
          </div>
        ))}
      </div>
    </div>
  );
};
