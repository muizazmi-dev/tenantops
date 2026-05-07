'use client';

import { useEffect, useRef, useState } from 'react';

interface Particle {
  x: number; y: number;
  vx: number; vy: number;
  text: string;
  size: number;
  opacity: number;
  opacityDir: number;
}

// Super Mario Bros Overworld Theme  — [frequency_hz, duration_s], 0 = rest
const q = 0.15; // eighth note at ~200 bpm
const MARIO_NOTES: [number, number][] = [
  // Intro riff
  [659.25, q], [659.25, q], [0, q],
  [659.25, q], [0, q],
  [523.25, q], [659.25, q * 2],
  [783.99, q * 4], [0, q * 2], [392.00, q * 4], [0, q * 2],
  // Main melody A
  [523.25, q * 2], [392.00, q], [0, q], [329.63, q * 2], [0, q * 4],
  [440.00, q * 2], [0, q], [493.88, q * 2], [0, q],
  [466.16, q], [440.00, q * 2],
  [392.00, q * 1.33], [659.25, q * 1.33], [783.99, q * 1.33],
  [880.00, q * 2], [0, q],
  [698.46, q], [783.99, q], [0, q],
  [659.25, q * 2], [0, q],
  [523.25, q], [587.33, q], [493.88, q * 2], [0, q * 2],
  // Repeat A
  [523.25, q * 2], [392.00, q], [0, q], [329.63, q * 2], [0, q * 4],
  [440.00, q * 2], [0, q], [493.88, q * 2], [0, q],
  [466.16, q], [440.00, q * 2],
  [392.00, q * 1.33], [659.25, q * 1.33], [783.99, q * 1.33],
  [880.00, q * 2], [0, q],
  [698.46, q], [783.99, q], [0, q],
  [659.25, q * 2], [0, q],
  [523.25, q], [587.33, q], [493.88, q * 2], [0, q * 4],
];

export default function UIOverlay() {
  const canvasRef    = useRef<HTMLCanvasElement>(null);
  const particlesRef = useRef<Particle[]>([]);
  const rafRef       = useRef<number>(0);
  const audioCtxRef  = useRef<AudioContext | null>(null);
  const isPlayingRef = useRef(false);
  const noteIdxRef   = useRef(0);
  const timerRef     = useRef<ReturnType<typeof setTimeout> | null>(null);

  const [theme,      setTheme]      = useState<'dark' | 'light'>('dark');
  const [playing,    setPlaying]    = useState(false);
  const [showPrompt, setShowPrompt] = useState(false);

  // ── Theme init (read localStorage before first paint is handled by the
  //    inline script in layout.tsx; here we just sync React state) ────────
  useEffect(() => {
    const saved = localStorage.getItem('tenantops-theme') as 'dark' | 'light' | null;
    const t = saved ?? 'dark';
    setTheme(t);
    document.documentElement.setAttribute('data-theme', t);
  }, []);

  const toggleTheme = () => {
    setTheme(prev => {
      const next = prev === 'dark' ? 'light' : 'dark';
      localStorage.setItem('tenantops-theme', next);
      document.documentElement.setAttribute('data-theme', next);
      return next;
    });
  };

  // ── Particle field ────────────────────────────────────────────────────
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const init = () => {
      canvas.width  = window.innerWidth;
      canvas.height = window.innerHeight;
      particlesRef.current = Array.from({ length: 28 }, () => ({
        x:          Math.random() * canvas.width,
        y:          Math.random() * canvas.height,
        vx:         (Math.random() - 0.5) * 0.35,
        vy:         (Math.random() - 0.5) * 0.35,
        text:       Math.random() < 0.5 ? 'ISV' : 'PTC',
        size:       ([14, 20, 26] as number[])[Math.floor(Math.random() * 3)],
        opacity:    Math.random() * 0.22 + 0.06,
        opacityDir: Math.random() < 0.5 ? 1 : -1,
      }));
    };

    const onResize = () => init();
    window.addEventListener('resize', onResize);
    init();

    const tick = () => {
      const ctx = canvas.getContext('2d');
      if (!ctx) return;
      ctx.clearRect(0, 0, canvas.width, canvas.height);

      for (const p of particlesRef.current) {
        p.x += p.vx;
        p.y += p.vy;
        if (p.x < -60 || p.x > canvas.width  + 60) p.vx *= -1;
        if (p.y < -30  || p.y > canvas.height + 30) p.vy *= -1;

        p.opacity += p.opacityDir * 0.0008;
        if (p.opacity >= 0.28) { p.opacity = 0.28; p.opacityDir = -1; }
        if (p.opacity <= 0.05) { p.opacity = 0.05; p.opacityDir =  1; }

        ctx.save();
        ctx.globalAlpha = p.opacity;
        ctx.font        = `bold ${p.size}px 'Courier New', monospace`;
        ctx.fillStyle   = '#00d4ff';
        ctx.fillText(p.text, p.x, p.y);
        ctx.restore();
      }

      rafRef.current = requestAnimationFrame(tick);
    };
    tick();

    return () => {
      window.removeEventListener('resize', onResize);
      cancelAnimationFrame(rafRef.current);
    };
  }, []);

  // ── Music scheduler (stored in a ref so it always has fresh state) ─────
  const scheduleRef = useRef<() => void>(() => {});
  scheduleRef.current = () => {
    if (!isPlayingRef.current || !audioCtxRef.current) return;
    const ctx           = audioCtxRef.current;
    const [freq, dur]   = MARIO_NOTES[noteIdxRef.current];

    if (freq > 0) {
      const osc  = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.connect(gain);
      gain.connect(ctx.destination);
      osc.type          = 'square';
      osc.frequency.value = freq;
      gain.gain.setValueAtTime(0.07, ctx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + dur * 0.88);
      osc.start(ctx.currentTime);
      osc.stop(ctx.currentTime + dur);
    }

    noteIdxRef.current = (noteIdxRef.current + 1) % MARIO_NOTES.length;
    timerRef.current   = setTimeout(() => scheduleRef.current(), dur * 1000);
  };

  const startMusic = async () => {
    if (!audioCtxRef.current || audioCtxRef.current.state === 'closed') {
      audioCtxRef.current = new AudioContext();
    }
    const ctx = audioCtxRef.current;
    if (ctx.state === 'suspended') await ctx.resume();
    isPlayingRef.current = true;
    noteIdxRef.current   = 0;
    setPlaying(true);
    setShowPrompt(false);
    scheduleRef.current();
  };

  const stopMusic = () => {
    isPlayingRef.current = false;
    setPlaying(false);
    if (timerRef.current) clearTimeout(timerRef.current);
  };

  const toggleMusic = async () => {
    if (playing) stopMusic(); else await startMusic();
  };

  // ── Attempt autoplay on mount; prompt if browser blocks it ────────────
  useEffect(() => {
    const tryAutoplay = async () => {
      try {
        const ctx = new AudioContext();
        audioCtxRef.current = ctx;
        if (ctx.state === 'suspended') {
          setShowPrompt(true);
        } else {
          isPlayingRef.current = true;
          noteIdxRef.current   = 0;
          setPlaying(true);
          scheduleRef.current();
        }
      } catch {
        setShowPrompt(true);
      }
    };

    const t = setTimeout(tryAutoplay, 600);
    return () => {
      clearTimeout(t);
      isPlayingRef.current = false;
      if (timerRef.current) clearTimeout(timerRef.current);
      audioCtxRef.current?.close();
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Render ────────────────────────────────────────────────────────────
  return (
    <>
      {/* Particle canvas — sits between body bg and page content */}
      <canvas
        ref={canvasRef}
        style={{ position: 'fixed', inset: 0, zIndex: 1, pointerEvents: 'none' }}
      />

      {/* Autoplay prompt banner */}
      {showPrompt && (
        <div className="autoplay-prompt">
          <span>♪ Background music ready</span>
          <button onClick={startMusic} style={{ padding: '4px 14px', fontSize: 11 }}>
            [ Enable ]
          </button>
          <button
            onClick={() => setShowPrompt(false)}
            style={{
              padding: '4px 10px', fontSize: 11,
              color: 'var(--muted)', borderColor: 'var(--muted)', boxShadow: 'none',
            }}
          >
            [ Dismiss ]
          </button>
        </div>
      )}

      {/* Floating controls — top-left */}
      <div className="floating-controls">
        <button
          onClick={toggleMusic}
          title={playing ? 'Pause music' : 'Play music'}
          className={`ctrl-btn${playing ? ' ctrl-btn--cyan' : ''}`}
          style={{ borderColor: 'var(--cyan)', color: 'var(--cyan)' }}
        >
          {playing ? '⏸' : '♪'}
        </button>
        <button
          onClick={toggleTheme}
          title={`Switch to ${theme === 'dark' ? 'light' : 'dark'} mode`}
          className="ctrl-btn"
          style={{ borderColor: 'var(--purple)', color: 'var(--purple)' }}
        >
          {theme === 'dark' ? '☀' : '◑'}
        </button>
      </div>
    </>
  );
}
