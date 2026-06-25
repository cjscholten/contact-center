import { useCallback, useEffect, useRef, useState } from 'react';
import { Center } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useSoftphone } from './softphone/useSoftphone';
import { useAgentSnapshot } from './agent/useAgentSnapshot';
import { useContactCenterHub } from './realtime/useContactCenterHub';
import { agentApi, type DirectoryEntry, type Presence } from './api/agentApi';
import { asteriskHost } from './config';
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
  const [consultWith, setConsultWith] = useState<string | null>(null);
  const snapshot = useAgentSnapshot(agentName);
  const { waiting } = useContactCenterHub(agentName !== null);

  // Wachtstand en overleg resetten zodra het gesprek eindigt (o.a. na voltooien van een overleg).
  useEffect(() => {
    if (sp.callState === 'idle') {
      setOnHold(false);
      setConsultWith(null);
    }
  }, [sp.callState]);

  const login = async (user: string, password: string) => {
    try {
      await sp.connect(asteriskHost, user, password);
      await agentApi.login(user);
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

  const setPresence = async (presence: Presence) => {
    if (!agentName) return;
    try { await agentApi.setPresence(agentName, presence); } catch (e) { fail('Status wijzigen mislukt', e); }
  };

  const pickup = async (callId: string) => {
    if (!agentName) return;
    try {
      await agentApi.pickup(agentName, callId);
    } catch {
      notifications.show({
        color: 'yellow',
        title: 'Aannemen niet gelukt',
        message: 'Dit gesprek is al aangenomen of je hebt al een lopend gesprek.',
      });
    }
  };

  const search = useCallback(
    (query: string) => agentApi.searchDirectory(query, agentName ?? undefined),
    [agentName],
  );

  const transfer = async (entry: DirectoryEntry) => {
    if (!agentName) return;
    try {
      if (entry.kind === 'agent') await agentApi.transferToAgent(agentName, entry.target);
      else await agentApi.coldTransfer(agentName, entry.target);
    } catch {
      notifications.show({
        color: 'yellow',
        title: 'Doorverbinden niet gelukt',
        message: entry.kind === 'agent' ? 'De collega is offline of in gesprek.' : 'Doorverbinden mislukt.',
      });
    }
  };

  // Warm doorverbinden: overleg starten met een collega; daarna voltooien of annuleren.
  const startWarmTransfer = async (entry: DirectoryEntry) => {
    if (!agentName) return;
    try {
      await agentApi.warmTransfer(agentName, entry.target);
      setConsultWith(entry.label);
    } catch {
      notifications.show({
        color: 'yellow',
        title: 'Overleg starten niet gelukt',
        message: 'De collega is offline of al in gesprek.',
      });
    }
  };

  const completeWarmTransfer = async () => {
    if (!agentName) return;
    try {
      await agentApi.completeWarmTransfer(agentName);
      setConsultWith(null);
    } catch {
      notifications.show({
        color: 'yellow',
        title: 'Overleg voltooien niet gelukt',
        message: 'De collega heeft het overleg nog niet aangenomen.',
      });
    }
  };

  const cancelWarmTransfer = async () => {
    if (!agentName) return;
    try { await agentApi.cancelWarmTransfer(agentName); } catch { /* overleg al beëindigd */ }
    setConsultWith(null);
  };

  return (
    <>
      <audio ref={audioRef} autoPlay />
      {agentName ? (
        <ZetaDeskShell
          agentName={agentName}
          status={snapshot?.status ?? 'LoggedOut'}
          presence={snapshot?.presence ?? 'Available'}
          callState={sp.callState}
          onHold={onHold}
          consultWith={consultWith}
          waiting={waiting}
          canPickup={sp.callState === 'idle'}
          onAnswer={() => void sp.answer()}
          onHangup={() => void sp.hangup()}
          onToggleHold={() => void toggleHold()}
          onFinishWrapUp={() => void finishWrapUp()}
          onSetPresence={(p) => void setPresence(p)}
          onPickup={(id) => void pickup(id)}
          onSearch={search}
          onTransfer={(e) => void transfer(e)}
          onWarmTransfer={(e) => void startWarmTransfer(e)}
          onCompleteWarmTransfer={() => void completeWarmTransfer()}
          onCancelWarmTransfer={() => void cancelWarmTransfer()}
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
