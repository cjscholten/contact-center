import { useCallback, useEffect, useRef, useState } from 'react';
import { notifications } from '@mantine/notifications';
import { useAuth } from 'react-oidc-context';
import { AuthErrorScreen, LoadingScreen, LoginScreen, setAccessToken } from '@zeta/ui';
import { useSoftphone } from './softphone/useSoftphone';
import { useContactCenterHub } from './realtime/useContactCenterHub';
import { agentApi, type DirectoryEntry, type Presence } from './api/agentApi';
import { sipWsUrl } from './config';
import { ZetaDeskShell } from './components/ZetaDeskShell';

function fail(title: string, e: unknown): void {
  notifications.show({ color: 'red', title, message: e instanceof Error ? e.message : String(e) });
}

export default function App() {
  const auth = useAuth();
  const audioRef = useRef<HTMLAudioElement>(null);
  const sp = useSoftphone(audioRef);
  const [agentName, setAgentName] = useState<string | null>(null);
  const [onHold, setOnHold] = useState(false);
  const [consultWith, setConsultWith] = useState<string | null>(null);
  const startedRef = useRef(false);
  const { waiting, agentSnapshot: snapshot } = useContactCenterHub(agentName);

  // Het access-token beschikbaar maken voor de fetch-wrappers + SignalR.
  useEffect(() => {
    setAccessToken(auth.user?.access_token ?? null);
  }, [auth.user]);

  // Wachtstand en overleg resetten zodra het gesprek eindigt (o.a. na voltooien van een overleg).
  // Tijdens render bijwerken op basis van de vorige callState i.p.v. via een effect — dat is het
  // aanbevolen patroon voor afgeleide state en voorkomt een extra render-cyclus.
  const [prevCallState, setPrevCallState] = useState(sp.callState);
  if (sp.callState !== prevCallState) {
    setPrevCallState(sp.callState);
    if (sp.callState === 'idle') {
      setOnHold(false);
      setConsultWith(null);
    }
  }

  // Na Keycloak-login: SIP-gegevens ophalen, softphone registreren, backend-login. Eénmalig per sessie.
  useEffect(() => {
    if (!auth.isAuthenticated || !auth.user || startedRef.current) return;
    startedRef.current = true;
    const username = auth.user.profile.preferred_username;
    if (!username) {
      fail('Aanmelden mislukt', new Error('Token bevat geen preferred_username'));
      return;
    }
    void (async () => {
      try {
        const sip = await agentApi.getSipCredentials();
        const iceServers = await agentApi.getIceServers();
        await sp.connect(sipWsUrl, sip.username, sip.password, iceServers);
        await agentApi.login(username);
        setAgentName(username);
      } catch (e) {
        startedRef.current = false; // mislukt: een nieuwe poging toestaan
        await sp.disconnect();
        fail('Verbinden mislukt', e);
      }
    })();
  }, [auth.isAuthenticated, auth.user, sp]);

  const logout = async () => {
    if (agentName) {
      try { await agentApi.logout(agentName); } catch { /* backend weg; toch afmelden */ }
    }
    await sp.disconnect();
    setAgentName(null);
    startedRef.current = false;
    setAccessToken(null);
    await auth.signoutRedirect();
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
    sp.armAutoAnswer(); // de inkomende leg van déze pickup direct opnemen (één klik)
    try {
      await agentApi.pickup(agentName, callId);
    } catch {
      sp.disarmAutoAnswer(); // pickup mislukt: geen automatische opname
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

  let content: React.ReactNode;
  if (auth.isLoading) {
    content = <LoadingScreen />;
  } else if (auth.error) {
    content = <AuthErrorScreen message={auth.error.message} onRetry={() => void auth.signinRedirect()} />;
  } else if (!auth.isAuthenticated) {
    content = <LoginScreen appName="ZetaDesk" onLogin={() => void auth.signinRedirect()} />;
  } else if (!agentName) {
    content = <LoadingScreen message="Verbinden…" />;
  } else {
    content = (
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
    );
  }

  return (
    <>
      <audio ref={audioRef} autoPlay />
      {content}
    </>
  );
}
