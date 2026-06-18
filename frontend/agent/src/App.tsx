import { useEffect, useRef, useState } from 'react';
import { Center } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useSoftphone } from './softphone/useSoftphone';
import { useAgentStatus } from './agent/useAgentStatus';
import { useContactCenterHub } from './realtime/useContactCenterHub';
import { agentApi } from './api/agentApi';
import { rememberHost } from './config';
import { LoginForm } from './components/LoginForm';
import { ZetaDeskShell } from './components/ZetaDeskShell';

function fail(title: string, e: unknown): void {
  notifications.show({ color: 'red', title, message: e instanceof Error ? e.message : String(e) });
}

export default function App() {
  const audioRef = useRef<HTMLAudioElement>(null);
  const sp = useSoftphone(audioRef);
  const [agentName, setAgentName] = useState<string | null>(null);
  const [onHold, setOnHold] = useState(false);
  const status = useAgentStatus(agentName);
  const { waiting } = useContactCenterHub(agentName !== null);

  // Wachtstand resetten zodra het gesprek eindigt.
  useEffect(() => {
    if (sp.callState === 'idle') setOnHold(false);
  }, [sp.callState]);

  const login = async (host: string, user: string, password: string) => {
    try {
      await sp.connect(host, user, password);
      await agentApi.login(user);
      rememberHost(host);
      setAgentName(user);
    } catch (e) {
      await sp.disconnect();
      fail('Aanmelden mislukt', e);
    }
  };

  const logout = async () => {
    if (agentName) {
      try { await agentApi.logout(agentName); } catch { /* backend weg; SIP toch afsluiten */ }
    }
    await sp.disconnect();
    setAgentName(null);
  };

  const toggleHold = async () => {
    if (!agentName) return;
    try {
      if (onHold) await agentApi.unhold(agentName);
      else await agentApi.hold(agentName);
      setOnHold(!onHold);
    } catch (e) {
      fail('Wachtstand mislukt', e);
    }
  };

  const finishWrapUp = async () => {
    if (!agentName) return;
    try { await agentApi.finishWrapUp(agentName); } catch (e) { fail('Afronden mislukt', e); }
  };

  return (
    <>
      <audio ref={audioRef} autoPlay />
      {agentName ? (
        <ZetaDeskShell
          agentName={agentName}
          status={status}
          callState={sp.callState}
          onHold={onHold}
          waiting={waiting}
          onAnswer={() => void sp.answer()}
          onHangup={() => void sp.hangup()}
          onToggleHold={() => void toggleHold()}
          onFinishWrapUp={() => void finishWrapUp()}
          onLogout={() => void logout()}
        />
      ) : (
        <Center mih="100vh" p="md">
          <LoginForm onLogin={login} />
        </Center>
      )}
    </>
  );
}
