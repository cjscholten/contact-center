import { useCallback, useRef, useState, type RefObject } from 'react';
import { Web } from 'sip.js';
import { startRinging, stopRinging } from './ringtone';

export type CallState = 'idle' | 'ringing' | 'in_call';
export type ConnectionState = 'disconnected' | 'connecting' | 'registered' | 'failed';

export interface Softphone {
  connectionState: ConnectionState;
  callState: CallState;
  connect: (host: string, user: string, password: string) => Promise<void>;
  disconnect: () => Promise<void>;
  answer: () => Promise<void>;
  hangup: () => Promise<void>;
}

/** Beheert de SIP.js SimpleUser (WebRTC-registratie + gesprek) en de beltoon. */
export function useSoftphone(audioRef: RefObject<HTMLAudioElement | null>): Softphone {
  const userRef = useRef<Web.SimpleUser | null>(null);
  const [connectionState, setConnectionState] = useState<ConnectionState>('disconnected');
  const [callState, setCallState] = useState<CallState>('idle');

  const connect = useCallback(
    async (host: string, user: string, password: string) => {
      const audio = audioRef.current;
      if (!audio) throw new Error('Audio-element niet beschikbaar');

      setConnectionState('connecting');
      const su = new Web.SimpleUser(`ws://${host}:8088/ws`, {
        aor: `sip:${user}@${host}`,
        userAgentOptions: { authorizationUsername: user, authorizationPassword: password },
        media: { remote: { audio } },
      });
      su.delegate = {
        onCallReceived: () => {
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
    [audioRef],
  );

  const disconnect = useCallback(async () => {
    stopRinging();
    const su = userRef.current;
    userRef.current = null;
    setCallState('idle');
    setConnectionState('disconnected');
    if (su) {
      try { await su.unregister(); } catch { /* verbinding mogelijk al weg */ }
      try { await su.disconnect(); } catch { /* idem */ }
    }
  }, []);

  const answer = useCallback(async () => {
    await userRef.current?.answer();
  }, []);

  const hangup = useCallback(async () => {
    await userRef.current?.hangup();
  }, []);

  return { connectionState, callState, connect, disconnect, answer, hangup };
}
