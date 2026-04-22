'use client';
import { useState } from 'react';

type Citation = { documentId: string; title: string; snippet: string };
type ChatMsg = { role: 'user' | 'assistant'; content: string; citations?: Citation[] };

export default function ChatPage() {
  const [messages, setMessages] = useState<ChatMsg[]>([]);
  const [input, setInput] = useState('');
  const [busy, setBusy] = useState(false);

  async function send() {
    if (!input.trim() || busy) return;
    const userMsg: ChatMsg = { role: 'user', content: input };
    setMessages(m => [...m, userMsg]);
    setInput('');
    setBusy(true);
    try {
      const res = await fetch('/api/chat', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ message: userMsg.content })
      });
      if (!res.ok) {
        const errText = res.status === 401 ? 'Please sign in first.' : `Error: ${res.status}`;
        setMessages(m => [...m, { role: 'assistant', content: errText }]);
        return;
      }
      const data = await res.json();
      setMessages(m => [...m, { role: 'assistant', content: data.answer, citations: data.citations }]);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <h1>Chat with your tenant data</h1>
      <p>Answers are grounded in your tenant's knowledge base only. RAG via Azure OpenAI + AI Search (tenant-filtered).</p>

      <div className="card" style={{ minHeight: 300 }}>
        {messages.length === 0 && <p><em>Ask something about refunds, service hours, or anything in your KB.</em></p>}
        {messages.map((m, i) => (
          <div key={i} style={{ marginBottom: 12 }}>
            <strong>{m.role === 'user' ? 'You' : 'Assistant'}:</strong>
            <div style={{ whiteSpace: 'pre-wrap' }}>{m.content}</div>
            {m.citations && m.citations.length > 0 && (
              <div className="citation">
                Sources:
                <ul>
                  {m.citations.map((c, j) => (
                    <li key={j}>
                      <code>[{j + 1}]</code> <strong>{c.title}</strong> — {c.snippet}
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        ))}
      </div>

      <div className="card">
        <textarea value={input} onChange={e => setInput(e.target.value)}
                  placeholder="Ask your tenant's knowledge base..."
                  onKeyDown={e => { if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) send(); }} />
        <div style={{ marginTop: 8, display: 'flex', justifyContent: 'space-between' }}>
          <small>Ctrl/Cmd+Enter to send</small>
          <button onClick={send} disabled={busy}>{busy ? 'Thinking…' : 'Send'}</button>
        </div>
      </div>
    </div>
  );
}
