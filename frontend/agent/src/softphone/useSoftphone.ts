import { useCallback, useRef, useState, type RefObject } from 'react';
import { Web } from 'sip.js';
import { startRinging, stopRinging } from './ringtone';

export type CallState = 'idle' | 'ringing' | 'in_call';
export type ConnectionState = 'disconnected' | 'connecting' | 'registered' | 'failed';

export interface Softphone {
  connectionState: ConnectionState;
  callState: CallState;
  connect: (wsUrl: string, user: string, password: string, iceServers?: RTCIceServer[]) => Promise<void>;
  disconnect: () => Promise<void>;
  answer: () => Promise<void>;
  hangup: () => Promise<void>;
  /** Neemt het eerstvolgende inkomende gesprek automatisch op (voor een eigen pickup). */
  armAutoAnswer: () => void;
  /** Annuleert een openstaande auto-answer (bv. als de pickup mislukte). */
  disarmAutoAnswer: () => void;
}

// Als er na een pickup binnen dit venster geen gesprek binnenkomt, vervalt de auto-answer weer —
// zodat een later, los inkomend gesprek niet per ongeluk automatisch wordt opgenomen.
const AUTO_ANSWER_WINDOW_MS = 15_000;

/** Beheert de SIP.js SimpleUser (WebRTC-registratie + gesprek) en de beltoon. */
export function useSoftphone(audioRef: RefObject<HTMLAudioElement | null>): Softphone {
  const userRef = useRef<Web.SimpleUser | null>(null);
  const [connectionState, setConnectionState] = useState<ConnectionState>('disconnected');
  const [callState, setCallState] = useState<CallState>('idle');
  const autoAnswerRef = useRef(false);
  const autoAnswerTimerRef = useRef<number | null>(null);

  const disarmAutoAnswer = useCallback(() => {
    autoAnswerRef.current = false;
    if (autoAnswerTimerRef.current !== null) {
      clearTimeout(autoAnswerTimerRef.current);
      autoAnswerTimerRef.current = null;
    }
  }, []);

  const armAutoAnswer = useCallback(() => {
    autoAnswerRef.current = true;
    if (autoAnswerTimerRef.current !== null) clearTimeout(autoAnswerTimerRef.current);
    autoAnswerTimerRef.current = window.setTimeout(() => {
      autoAnswerRef.current = false;
      autoAnswerTimerRef.current = null;
    }, AUTO_ANSWER_WINDOW_MS);
  }, []);

  const connect = useCallback(
    async (wsUrl: string, user: string, password: string, iceServers: RTCIceServer[] = []) => {
      const audio = audioRef.current;
      if (!audio) throw new Error('Audio-element niet beschikbaar');

      // SIP-domein (aor) uit de WS-URL halen, zodat het klopt met de host achter ws:// of wss://.
      const sipHost = new URL(wsUrl).hostname;
      setConnectionState('connecting');
      const su = new Web.SimpleUser(wsUrl, {
        aor: `sip:${user}@${sipHost}`,
        userAgentOptions: {
          authorizationUsername: user,
          authorizationPassword: password,
          // TURN/STUN voor NAT-traversal (thuiswerkers); leeg = alleen host-kandidaten.
          sessionDescriptionHandlerFactoryOptions: {
            peerConnectionConfiguration: { iceServers },
          },
        },
        media: { remote: { audio } },
      });
      su.delegate = {
        onCallReceived: () => {
          if (autoAnswerRef.current) {
            disarmAutoAnswer();
            void su.answer(); // eigen pickup: direct opnemen, geen beltoon (onCallAnswered zet in_call)
            return;
          }
          setCallState('ringing');
          startRinging();
        },
        onCallAnswered: () => {
          stopRinging();
          setCallState('in_call');
        },
        onCallHangup: () => {
          stopRinging();
          setCallState('idle');
        },
        onServerDisconnect: () => setConnectionState('failed'),
      };

      try {
        await su.connect();
        await su.register();
        userRef.current = su;
        setConnectionState('registered');
      } catch (e) {
        setConnectionState('failed');
        throw e;
      }
    },
    [audioRef, disarmAutoAnswer],
  );

  const disconnect = useCallback(async () => {
    stopRinging();
    disarmAutoAnswer();
    const su = userRef.current;
    userRef.current = null;
    setCallState('idle');
    setConnectionState('disconnected');
    if (su) {
      try { await su.unregister(); } catch { /* verbinding mogelijk al weg */ }
      try { await su.disconnect(); } catch { /* idem */ }
    }
  }, [disarmAutoAnswer]);

  const answer = useCallback(async () => {
    await userRef.current?.answer();
  }, []);

  const hangup = useCallback(async () => {
    await userRef.current?.hangup();
  }, []);

  return { connectionState, callState, connect, disconnect, answer, hangup, armAutoAnswer, disarmAutoAnswer };
}
