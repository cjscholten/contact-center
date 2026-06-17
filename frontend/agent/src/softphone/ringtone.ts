// Synthetische beltoon via WebAudio — geen audiobestand nodig.
// AudioContext mag pas na een user-gesture; de aanmeld-klik telt daarvoor.
let audioCtx: AudioContext | undefined;
let timer: number | undefined;

function ensureAudio(): AudioContext {
  audioCtx ??= new AudioContext();
  if (audioCtx.state === 'suspended') void audioCtx.resume();
  return audioCtx;
}

function burst(ctx: AudioContext): void {
  const now = ctx.currentTime;
  const osc = ctx.createOscillator();
  const gain = ctx.createGain();
  osc.type = 'sine';
  osc.frequency.value = 425; // NL-beltoonfrequentie
  gain.gain.setValueAtTime(0, now);
  gain.gain.linearRampToValueAtTime(0.2, now + 0.05);
  gain.gain.setValueAtTime(0.2, now + 0.95);
  gain.gain.linearRampToValueAtTime(0, now + 1.0);
  osc.connect(gain).connect(ctx.destination);
  osc.start(now);
  osc.stop(now + 1.0);
}

export function startRinging(): void {
  const ctx = ensureAudio();
  burst(ctx);
  timer = window.setInterval(() => burst(ctx), 3000); // 1s toon, 2s stilte
}

export function stopRinging(): void {
  if (timer !== undefined) {
    clearInterval(timer);
    timer = undefined;
  }
}
