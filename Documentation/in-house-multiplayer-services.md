# In-House Multiplayer Services: Investigation and Design

This document answers one question: what would it take to run Megacity Metro's online features
(identity, matchmaking, session browsing, server hosting, relay and voice chat) on infrastructure you own
instead of Unity Gaming Services (UGS). Part A is the investigation of what the game uses today and how deeply.
Part B is the design of the replacement and a migration plan that keeps the game playable at every step.

Companion documents: [netcode.md](netcode.md) (transport and gameplay replication, which stay as they are),
[architecture.md](architecture.md), [known-issues.md](known-issues.md).

---

# Part A — Investigation

## A.1 Headline findings

1. **Gameplay networking does not depend on UGS.** Netcode for Entities talks plain UDP through Unity Transport
   to a dedicated server by IP and port (`ServerConnectionUtils.RequestConnection`). Every UGS call happens
   *before* that connection (to find the endpoint) or *beside* it (voice, roster bookkeeping). Replacing UGS does
   not touch prediction, ghosts, lag compensation or the 150-player tuning.
2. **The real dependencies are five services**: Authentication (anonymous player ids), Multiplayer Services
   sessions (matchmaking, session browser, join), Multiplay Hosting (server lifecycle, SQP query, backfill),
   Vivox (one global voice channel), and the editor-only Deployment package (fleet/queue configuration files).
   Relay is referenced in code but never used: the game is dedicated-server only.
3. **Surface area is small and already half-abstracted.** Twenty scripts reference `Unity.Services.*`; the UI and
   gameplay talk to three façades (`MatchMakingConnector`, `VivoxManager`, `ServerBehaviour`). Replacing the
   façades' internals is the whole client/server job. The larger job is the backend you have to run.
4. **What UGS gives you implicitly** and you would have to provide: global server fleet with regions and
   autoscaling, QoS region selection, DDoS-shielded public endpoints, a matchmaking rule engine with backfill,
   voice with echo cancellation and noise suppression on six platforms, and a hosted dashboard.

## A.2 Service-by-service usage

| Service | Package | Where it is called | What the game gets from it | Needed in replacement? |
|---|---|---|---|---|
| **Authentication** | `com.unity.services.authentication` 3.7.4 | `UGS/Authentication/PlayerAuthentication.cs` (`SignInAnonymouslyAsync` with a hashed profile), `ClientManager.LoginAsync` (sign-in / `SwitchProfile`), `VivoxManager.Start` (re-sign-in for Vivox tokens), server: `ServerAuthenticationService.SignInFromServerAsync` / `SignInWithServiceAccountAsync` in `ServerManager` | A stable `PlayerId` string (`UASId`) sent to the server in `SetPlayerInfoRequest`, stored in `PlayerConnectedElement`/`PlayerUASID`, used to remove the player from the session on disconnect. Access tokens for sessions and Vivox. | Yes: an identity that matchmaking, sessions and voice can trust. |
| **Multiplayer Services (sessions)** | `com.unity.services.multiplayer` 2.3.1 (pulls `qos`, `wire`, `deployment`, `authentication`, `core`) | `ClientManager.MatchmakeAsync` (`MatchmakeSessionAsync`, queue `default`), `ClientManager.JoinSessionAsync` (`JoinSessionByIdAsync`), `MatchMakingConnector.ListSessions` (`QuerySessionsAsync`), `ClientManager.LeaveSessionAsync`, `CustomEntitiesNetworkHandler` (`INetworkHandler` that connects the client world to the session's endpoint), `CustomDriverConstructor` (Direct/Relay driver) | Match found → `ip:port`; session list for the browser (id, player count, max); join-by-id; leave. The SDK's own connection is then discarded by `RequestConnection` (see known issue N3). | Yes: endpoint discovery (matchmaking + browser) and player-count bookkeeping. |
| **Multiplay Hosting** | same package, server side | `ServerManager.StartAsync`: `StartMultiplaySessionManagerAsync` (allocation callbacks, SQP responder, backfill config `WithBackfillingConfiguration(true,true,true,30,1)`), `CreateSessionAsync` for local service-account runs; `SessionEventManager.RemovePlayer` ← `PlayerLeftEventSystem`; `CommandLineConfig` parses `-port`, `--api-key-id`, `--api-key-secret`; `ServerInGame.OnCreate` listens on that port | Server registration, allocation lifecycle, server query (SQP on port 9010), idle shutdown via fleet `minAvailable`, backfill into a running match, player roster per session. | Yes: a fleet manager plus an in-game agent. |
| **Matchmaker (legacy API)** | `Unity.Services.Matchmaker` (via multiplayer package) | `UGS/Client/ClientMatchmaker.cs` only | Nothing; dead code since the move to sessions. | No. Delete. |
| **Relay** | `Unity.Networking.Transport.Relay` (UTP) + session SDK | `CustomDriverConstructor` / `CustomEntitiesNetworkHandler` handle `NetworkType.Relay` | Nothing in practice; server sessions use `WithDirectNetwork()`. | Only if you add listen-server/peer hosting. |
| **Vivox** | `com.unity.services.vivox` 16.11.0 | `VivoxManager` (init, credentials), `VivoxSession` (`LoginAsync` with display name, logout), `VivoxChannel` (`JoinGroupChannelAsync("MultipleUserChannel", AudioOnly)`), `VivoxDevicesVolume` (mute, ±50 volume range), `VivoxPlayerList` (participant events, `SpeechDetected`), `VivoxPanel`, `UIAudioSettings` (speaker/mic sliders), `InitializedVivoxSystem` (login once the replicated name exists) | One global audio channel for the whole server, push-to-talk style mute button (`V`), per-device volume, "who is speaking" label. | Yes: voice with mute, volume and speaking indication. |
| **Deployment** | `com.unity.services.deployment` 1.7.2 (editor) | `Assets/Settings/UGS/GSH/*.gsh|.mmq|.mme|.ddef`, `Assets/Tutorials` (IET) | One-click upload of the Linux server build, fleet (`megacity-metro-fleet`, North America, `minAvailable 0`, `maxServers 1`, 2 cores / 7000 MiB), build configuration (`-ip $$ip$$ -port $$port$$ -queryport $$query_port$$`), matchmaker queue (`default`, 150-player team relaxing to 1 after 10 s, backfill on, pool timeout 60 s). | Replace with your CI/CD and fleet config. |
| **QoS**, **Wire** | transitive | none directly | Region latency measurement and realtime session events inside the SDK. | QoS yes (region pick), Wire no (your own push channel). |
| **Multiplayer Center**, **Player Accounts settings** | `com.unity.multiplayer.center`, `Resources/UnityPlayerAccountSettings.asset` | none in code | Editor guidance; unused asset. | No. |

Assets involved: `Assets/Prefabs/UGS/Vivox.prefab` (instantiated by `PlayerInfoController` in multiplayer),
`Assets/Prefabs/UGS/GameServerServices.prefab` (orphan: one missing-script component, not referenced),
`Assets/Resources/GameServiceData.asset` (`MaxPlayers 150`, `ServerName "Megacity Multiplayer Server"`,
`GameType Arena`, `Map Megacity`, `ServerStartupBufferMS 5000`), `Assets/Resources/MultiplayerServerSettings.asset`
(connection timeout 20 s, server list `127.0.0.1:7979`), `ProjectSettings/UnityConnectSettings.asset` (cloud
project link; `Application.cloudProjectId` is checked by `MatchMakingConnector` and `VivoxManager` and both
disable themselves when it is empty).

Assembly dependencies on UGS: `Unity.MegacityMetro.Mix`, `.Client`, `.Server`, `.Components` (references
`Unity.Services.Vivox`, `Unity.Services.Core`, `VivoxUnity` although no component script uses them),
`Unity.Services.UGS.Authentication`, `Unity.Services.UGS.Client`, `Unity.UgsTutorials`.

## A.3 Runtime flows that must keep working

```
Client menu   : sign in -> (Find Match | browse sessions -> pick) -> endpoint -> RequestConnection(ip, port) -> Main scene
Client in game: once NetworkStreamInGame -> SetPlayerInfoRequest { Name, UASId, IsClient, Platform }
                once PlayerName replicated -> voice login(displayName) -> join global channel -> mute button / sliders
Server boot   : parse CLI -> authenticate as a server -> register / wait for allocation -> listen on port
Server run    : answer queries (players, max, name, build) -> add players to roster on RPC -> remove on disconnect
Server idle   : fleet removes servers with no players (minAvailable 0) ; backfill sends new tickets to a non-full server
```

Behavioural requirements extracted from the code (these define "done" for the replacement):

| Id | Requirement | Source |
|---|---|---|
| R1 | A player id that is stable for the session and unique per device/profile; a free-text display name (alphanumerics, `-`, `_`). | `PlayerAuthentication`, `MultiplayerMenu.FilterNonAlphanumeric` |
| R2 | "Find Match": return an `ip:port` of a server in the best region with a free slot, starting a new server when none has room; wait up to 60 s. | `ClientManager.MatchmakeAsync`, `.mmq` pool timeout |
| R3 | Session browser: list joinable servers with id/name, current and max players; join by id; manual refresh. | `SessionBrowser`, `MatchMakingConnector.ListSessions/JoinSession` |
| R4 | Server registers itself, reports player count, is queryable, is told when it is allocated, and is shut down when idle. | `ServerManager`, `.gsh` fleet config |
| R5 | Backfill: a server with free slots keeps receiving players until full (150). | `WithBackfillingConfiguration`, `.mmq backfillEnabled` |
| R6 | Player leave is reported so the session count stays correct. | `PlayerLeftEventSystem` |
| R7 | Voice: one channel per server, join after name is known, mute/unmute mic, speaker and mic volume 0–100, speaking indicator with display name, disabled in single player and on servers. | `Vivox*`, `UIAudioSettings` |
| R8 | Connection timeout 20 s with a modal; return to menu on disconnect; leaving a session on return to menu. | `CheckServerConnectionTimeoutSystem`, `MainMenu.Start` |
| R9 | Headless thin clients can matchmake automatically for load tests. | `MainMenu.PollForAutomaticMatchmakeFlow` |
| R10 | Local development: run a server with credentials from the command line and connect to `127.0.0.1:7979`. | `multiplayer-setup.md`, `ServerManager` service-account path |

---

# Part B — Design of the in-house solution

## B.1 Principles

- **Keep Netcode for Entities and Unity Transport exactly as they are.** The backend's only job toward gameplay is
  to hand the client an endpoint and a join token, and to run the server process.
- **Dedicated servers only** (as today). This removes the need for relay/NAT traversal entirely; relay is covered
  in B.7 as an optional extension.
- **Thin in-game clients, thick backend.** Game-side code is HTTP/JSON plus one UDP voice stream. All matching,
  allocation and scaling logic lives in services you can redeploy without a game patch.
- **Interfaces first.** Put every service behind a C# interface, keep the UGS implementation as one provider
  during the transition, and switch providers with a scripting define.

## B.2 Target architecture

```
                      +------------------------------------------------------------+
                      |                      Backend (yours)                       |
  Game client         |  Identity   Directory/Sessions   Matchmaker   Fleet Mgr    |      Game servers
  --------------      |  (JWT)      (server registry,    (tickets,    (allocate,   |      ------------------
  MenuServices  --HTTPS->  /auth     browse, join token)  backfill)    scale, idle  |      HostingAgent (in game)
  QosProbe     --UDP echo-> region probes                             shutdown)   |<-HTTPS-  register / heartbeat
  VoiceClient  --UDP---------------------------------------------------------------+-------> VoiceServer (SFU) or
                      |                   Postgres + Redis + object storage          |         in-band on game server
                      +------------------------------------------------------------+
  Netcode (UTP, UDP) ---------------------------------------------------------------------->  ServerWorld :7979 (unchanged)
```

| Component | Responsibility | Replaces |
|---|---|---|
| Identity | Anonymous (device) accounts, display names, JWT access/refresh tokens, server-to-server tokens. | Authentication |
| Directory / Sessions | Registry of live servers (region, build, players, max, state), session list for the browser, join tokens, player join/leave bookkeeping. | Sessions, SQP, Wire |
| Matchmaker | Ticket queue; picks a server with room in the best region or asks Fleet Manager for a new one; backfill is just "servers with room are candidates". | Matchmaker queue/pool/rules |
| Fleet Manager | Starts/stops server processes or containers per region, scales on demand, kills idle servers, rolls out builds. | Multiplay Hosting + Deployment |
| Hosting Agent (in the game server) | Registers, heartbeats, receives allocation, reports players, requests shutdown. | `ServerManager` + Multiplay SDK |
| QoS probe (client) + regional echo endpoints | Measure RTT per region before matchmaking. | `com.unity.services.qos` |
| Voice | Capture, encode, transport, mix, play back; mute/volume; speaking events. | Vivox |
| Relay (optional) | Transparent UDP forwarder for peer-hosted sessions. | Relay |
| Ops | Build upload, config, logs, metrics, dashboards. | Dashboard, Deployment window |

### Build, buy-open-source, or keep

| Capability | Build yourself | Self-hosted open source | Notes |
|---|---|---|---|
| Identity + Directory + Matchmaker | One service (Go, ASP.NET Core or Node), Postgres, Redis. 2–4 weeks for Megacity's needs because the rules are trivial (fill servers to 150). | **Nakama** (auth, matchmaker, lobbies, RPC; Go/TS server runtime) or **Open Match** (Kubernetes matchmaking framework). | Megacity's matchmaking is "join the fullest non-full server in my region"; a custom service is smaller than configuring Open Match. Nakama fits if you also want accounts, friends, leaderboards later. |
| Fleet management | VM/Docker supervisor with a REST API (simple, fine for one or two regions). | **Agones** on Kubernetes: `GameServer`/`Fleet` CRDs, allocation API, health checks, autoscaling, SDK sidecar. Maps one-to-one to Multiplay's ready/allocated/shutdown lifecycle. | Agones is the closest drop-in for Multiplay semantics. The existing Dockerfile in `multiplayer-setup.md` is the starting image. |
| Voice | In-band Opus over UDP with server-side forwarding (proximity or channel). Full control, integrates with ECS positions. | **LiveKit** (SFU, has a Unity SDK), **mediasoup**, **Janus**, **Murmur** (Mumble). | WebRTC stacks bring echo cancellation and noise suppression; in-band gives you proximity voice and zero extra services. See B.8. |
| Relay | ~300-line UDP forwarder. | **coturn** (TURN) only with a TURN-capable transport, which UTP is not. | Not needed for dedicated servers. |

## B.3 Identity

- `POST /v1/auth/anonymous { deviceId, displayName }` → `{ playerId, accessToken (15 min), refreshToken (30 d) }`.
  `deviceId` is a random GUID stored in `user_preferences.json` (same `PersistentDataManager`), which reproduces
  today's per-install anonymous identity. Later: `POST /v1/auth/link` for platform accounts.
- Servers authenticate with a **service token** injected by the Fleet Manager (environment variable or
  `--server-token`), replacing `--api-key-id/--api-key-secret`.
- The game server must **stop trusting the client-sent `UASId`**: today `SetPlayerInfoRequest.UASId` is whatever
  the client says. With in-house identity the server learns the player id from the join token (B.4) and the RPC
  carries only cosmetic data (name, platform).

## B.4 Directory, sessions and join tokens

Server agent API (called from the game server):

```
POST /v1/servers/register   { region, buildId, publicIp, gamePort, maxPlayers, name, map, gameType }  -> { serverId, serverToken }
PUT  /v1/servers/{id}/heartbeat { state: Ready|Allocated|Draining, players, tickMs, uptime }      every 5 s
POST /v1/servers/{id}/players/{playerId}/joined | /left                                          (replaces SessionEventManager.RemovePlayer)
POST /v1/servers/{id}/shutdown  { reason }                                                        (idle, build retired)
```

Client API:

```
GET  /v1/sessions?region=eu&state=Ready,Allocated       -> [{ id, name, players, maxPlayers, region, map }]   (session browser)
POST /v1/sessions/{id}/join                              -> { ip, port, joinToken }                            (Connect)
```

- A **session** is simply a registered server that is accepting players; no separate lobby object is needed for
  Megacity's drop-in arena. Player counts come from heartbeats and join/left events (missed heartbeats for
  15 s → server marked dead and removed from the list).
- `joinToken` is a short-lived JWT `{ playerId, displayName, serverId, exp }` signed with a key the server
  knows. The game server verifies it in Netcode's **connection approval** flow (`RequireConnectionApproval` on
  `NetworkStreamDriver`, an `IApprovalRpcCommand` carrying the token, available since Netcode 1.2) and rejects
  unknown or expired tokens before the ghost is spawned. This replaces the implicit trust the Multiplay allocation
  gave you and also fixes the "name and id by assertion" weakness.
- The server query protocol (SQP on 9010) is replaced by heartbeats. If you want to keep an on-box query for
  tooling, SQP is a small documented UDP protocol and `go-svrquery` keeps working against a re-implementation.

## B.5 Matchmaking

Megacity's rules are intentionally simple (one 150-player arena per server, backfill on, "start another server
when the first one is full"). Design the service around that and keep room for teams later:

```
POST /v1/matchmaking/tickets { players: [playerId], regionLatencies: { eu: 24, na: 110 }, queue: "default" } -> { ticketId }
GET  /v1/matchmaking/tickets/{id}                -> { status: Searching|Found|Failed|Cancelled, assignment?: { ip, port, joinToken } }
DELETE /v1/matchmaking/tickets/{id}
WS   /v1/matchmaking/tickets/{id}/events          (optional push instead of polling every second)
```

Matching loop (every 250 ms per region):

1. Candidates = servers in the ticket's best region (lowest latency under a cap, else next best) with
   `state != Draining` and `players + reserved < maxPlayers`. Prefer the fullest (consolidates players, mirrors
   Multiplay backfill).
2. If no candidate: ask Fleet Manager to allocate one (`POST /v1/fleet/allocate { region, buildId }`), keep the
   ticket `Searching`; reserve a slot when the server reports `Ready`.
3. Assign, reserve the slot for 30 s (the `.mmq` 10 s relaxation plus connect time), hand out a join token.
4. Ticket timeout 60 s → `Failed` with a reason the UI can show (`MatchMakingUI.UpdateConnectionStatus`).

QoS: before creating a ticket the client sends 3 UDP echo packets to each region's probe endpoint
(`GET /v1/regions` lists them) and reports the median RTT. This is what `com.unity.services.qos` did.

Thin-client load tests (R9) call the same endpoints; `MainMenu.PollForAutomaticMatchmakeFlow` keeps working
because it only touches `MatchMakingConnector`.

## B.6 Fleet management and the in-game Hosting Agent

Lifecycle the game server must implement (identical to Multiplay's, so `ServerManager`'s shape survives):

```
Boot -> Register (Ready, 0 players) -> [Allocated by matchmaker or first browser join] -> Running
     -> players == 0 for IdleTimeout (e.g. 5 min, replaces fleet minAvailable 0 behaviour) -> Draining -> Shutdown
     -> on SIGTERM: Draining (stop accepting), notify players (optional RPC), exit when empty or after grace period
```

Two implementation routes:

| Route | How | When |
|---|---|---|
| **Agones on Kubernetes** | Server container with the Agones SDK sidecar; the agent calls `Ready()`, `Health()`, `Allocate`, `Shutdown()`; the Matchmaker uses the Agones allocation service; `Fleet` autoscaler keeps N warm servers per region. | You already run or can run Kubernetes; multiple regions; want autoscaling and rolling builds out of the box. |
| **Custom supervisor** | One small daemon per host (systemd or Docker) exposing `POST /spawn`, `GET /servers`, `DELETE /servers/{id}`; Fleet Manager keeps a table of hosts and their capacity (Megacity needs about 2 cores and 7 GB per 150-player server per the `.gsh` file). | One or two regions, a handful of machines, want to avoid Kubernetes. |

In both cases the game server reads `--backend-url`, `--server-token`, `--region`, `--public-ip`, `-port`
(`CommandLineConfig` already parses `-port`), and the Dockerfile from `multiplayer-setup.md` is the image.
Build upload becomes a CI job (Unity batch build → container image → registry), replacing the Deployment window.

## B.7 Relay (only if you add peer hosting)

Unity Relay's server protocol is proprietary; UTP's `RelayNetworkParameter` only talks to Unity's hosted Relay, so
it cannot point at your own relay. If you ever want player-hosted sessions behind NAT:

- Run a **transparent UDP forwarder**: the Directory allocates a relay port per session; the host and each client
  send to `relay:port`; the relay learns each peer's public address from the first authenticated packet (join
  token in a tiny handshake) and forwards datagrams host↔client. UTP needs no changes because it only sees
  endpoints. Budget: Megacity's 150-player snapshots would make a relay host a bandwidth hot spot; keep relay for
  small listen-server modes only.
- Alternatively keep dedicated servers for everything (recommended for this game's scale).

## B.8 Voice chat

What the current UI needs from a voice backend (`IVoiceService`, derived from the `VivoxService` members used):

```csharp
public interface IVoiceService
{
    Task InitializeAsync();
    Task LoginAsync(string playerId, string displayName, string voiceToken);
    Task LogoutAsync();
    Task JoinChannelAsync(string channelId);          // today: one channel per server ("MultipleUserChannel")
    Task LeaveAllChannelsAsync();
    bool IsLoggedIn { get; }
    bool IsInputMuted { get; }
    void MuteInput(bool mute);  void MuteOutput(bool mute);
    void SetInputVolume(int percent);  void SetOutputVolume(int percent);   // UI uses 0..100, Vivox used -50..50
    event Action LoggedIn, LoggedOut, ConnectionRecovering, ConnectionRecovered, ConnectionFailedToRecover;
    event Action<VoiceParticipant> ParticipantAdded, ParticipantRemoved;
    // VoiceParticipant: PlayerId, DisplayName, bool SpeechDetected, event SpeechDetectedChanged
}
```

Option A — **in-band voice on your own UDP path** (fully in-house):

- Capture with `Microphone.Start` (16 kHz mono, 20 ms frames), run a simple voice-activity gate (RMS threshold,
  hangover 200 ms) which also feeds the speaking indicator; encode with **Opus** (Concentus, a pure C# port, or
  libopus as a native plugin) at 16–24 kbps.
- Transport: a second Unity Transport driver on `gamePort + 1` using an unreliable, unordered pipeline (never
  Netcode RPCs, which are reliable and would stall under loss). Packet = `{ senderId, seq, timestamp, opusFrame }`.
- Server forwarding (SFU, no mixing): for every speaker forward to listeners that should hear them. With 150
  players you must filter: by **proximity** (listeners within N metres, read positions from the ECS world the
  server already has; this is a feature Vivox could not give you here), by team/squad, or cap at the K nearest
  speakers per listener (K = 8 is typical). Worst case bandwidth per client is `K × 24 kbps` down, one stream up.
- Playback: per-remote-speaker jitter buffer (60–120 ms), decode, feed an `AudioSource` through a streaming
  `AudioClip` or `OnAudioFilterRead`; spatialize with the ship's position for proximity voice.
- Missing versus Vivox: acoustic echo cancellation and noise suppression. Use WebRTC's audio processing module
  (`webrtc-audio-processing`, a native plugin) or require headsets/push-to-talk (the game already defaults to
  mic muted and uses `V` to toggle).
- Effort: 3–6 weeks for a robust cross-platform implementation including mobile microphone permissions
  (`AndroidPermissionManager` exists).

Option B — **self-hosted WebRTC SFU** (LiveKit, mediasoup, Janus):

- Run the SFU per region; your backend mints a room token per game server (room id = server id). The Unity client
  uses the SFU's Unity SDK (LiveKit ships one; it bundles a WebRTC native plugin per platform). You get AEC, noise
  suppression, adaptive bitrate and large-room scaling for free, and the `IVoiceService` adapter is thin.
- Proximity voice is possible by muting/unmuting subscriptions from gameplay position data, but it is coarser than
  in-band forwarding.
- Effort: 2–4 weeks including deployment, plus the operational cost of a media server tier.

Recommendation: Option B if voice quality on mobile matters more than control; Option A if proximity voice
tied to the simulation is a design goal and you accept owning the audio pipeline. Either way the UI
(`VivoxPanel`, `VivoxPlayerList`, `UIAudioSettings`) only needs the interface above.

## B.9 Client and server code changes (file by file)

### New abstraction layer (`Assets/Scripts/Gameplay/Mix/Multiplayer/Services/`)

| Interface | Methods | UGS provider (today) | In-house provider |
|---|---|---|---|
| `IIdentityService` | `SignInAsync(displayName)`, `PlayerId`, `AccessToken`, `RefreshAsync` | `PlayerAuthentication` + `AuthenticationService` | `IdentityClient` (REST, token cache in `PersistentDataManager`) |
| `ISessionDirectory` | `ListAsync(region)`, `JoinAsync(sessionId)` → `JoinInfo { ip, port, joinToken }`, `LeaveAsync()` | `ClientManager` + `MultiplayerService.QuerySessionsAsync/JoinSessionByIdAsync` | `DirectoryClient` |
| `IMatchmaker` | `FindMatchAsync(queue, latencies, ct)` → `JoinInfo`, `CancelAsync` | `ClientManager.MatchmakeAsync` | `MatchmakerClient` (+ `QosProbe`) |
| `IHostingAgent` (server) | `StartAsync(config)`, `SetReady`, `ReportPlayerJoined/Left(playerId)`, `RequestShutdown`, events `Allocated`, `Drain` | `ServerManager` + `SessionEventManager` | `HostingAgentClient` (REST heartbeat) or `AgonesHostingAgent` |
| `IVoiceService` | B.8 | adapter over `VivoxService` | `InBandVoiceService` or `LiveKitVoiceService` |

Select the provider set with a define (`USE_UGS_SERVICES`) or a `ServicesConfig` ScriptableObject in `Resources`
so both can be built during migration.

### Scripts to change

| File | Change |
|---|---|
| `Mix/UI/MonoBehaviours/Matchmaker/MatchMakingConnector.cs` | Keep as the façade the UI already uses. Replace `ClientManager` with `IIdentityService` + `IMatchmaker` + `ISessionDirectory`. `Matchmake()` and `JoinSession()` end with `ServerConnectionUtils.RequestConnection(ip, port)` as today and stash `joinToken` for the approval RPC. Drop the SDK network handler (removes the double connection, known issue N3). |
| `Client/UI/MonoBehaviours/MainMenu/SessionBrowser.cs` | Bind to a `SessionSummary` DTO instead of `ISessionInfo`. |
| `Mix/Multiplayer/SDK/Client/ClientManager.cs`, `CustomEntitiesNetworkHandler.cs`, `CustomDriverConstructor.cs` | Become the UGS provider implementation, then delete. Move the `MaxPlayerCount * 5` queue fix to `MegacityMetroDriverConstructor` and register it in `NetCodeBootstrap` (known issue N1). |
| `UGS/Authentication/PlayerAuthentication.cs`, `PlayerProfile.cs`, `UGS/Client/ClientMatchmaker.cs` | Replace with `IdentityClient`; delete `ClientMatchmaker`. Rename the `Unity.Services.UGS.*` assemblies to project names. |
| `Client/Player/SetupPlayerInfoSystem.cs` (`SendPlayerInfoSystem`) | Send name and platform only; identity comes from the join token. |
| **New** `Client/Netcode/ConnectionApprovalClientSystem.cs`, `Server/Netcode/ConnectionApprovalServerSystem.cs` | Client sends `JoinApprovalRpc { joinToken }` (`IApprovalRpcCommand`); server verifies the signature, stores `PlayerIdentity { PlayerId, DisplayName }` on the connection entity, approves or disconnects. Enable `RequireConnectionApproval` on the server driver in `ServerInGame.OnCreate`. |
| `Server/Player/SetPlayerInfoSystem.cs` | Read `PlayerIdentity` from the connection instead of `info.UASId`; call `IHostingAgent.ReportPlayerJoined`. |
| `Server/Netcode/VerifyPlayerDisconnectedSystem.cs`, `PlayerLeftEventSystem.cs` | Keep the event; `PlayerLeftEventSystem` calls `IHostingAgent.ReportPlayerLeft` and is no longer `UNITY_SERVER`-only in source (gate at runtime). |
| `Mix/Multiplayer/SDK/Server/ServerBehaviour.cs`, `ServerManager.cs`, `SessionEventManager.cs`, `GameServerInfo_Data.cs` | Become `HostingAgentBehaviour` + provider. `GameServerInfo` fields map to the register payload. Add idle-shutdown timer and SIGTERM handling (`Application.wantsToQuit` / `Application.quitting`). |
| `Server/Netcode/ServerInGame.cs` | Listen on the agent-provided port; publish `PlayerConnectedElement.Length` to the agent each heartbeat. |
| `Mix/Multiplayer/Netcode/CommandLineConfig.cs` | Add `--backend-url`, `--server-token`, `--region`, `--public-ip`, `--idle-timeout`; keep `-port`. |
| `Mix/Multiplayer/Netcode/NetcodeBootstrap.cs` | `TryStartUgs` → `TryStartHostingAgent`; remove `Unity.Services.MultiplayerSDK.*` usings. |
| `Client/Vivox/*` | Rename to `Voice*`; `VoiceManager` owns an `IVoiceService`; `VoiceSession.Login(displayName)` passes a voice token obtained from Identity; `VoiceDevicesVolume` maps 0–100 directly. |
| `Client/Vivox/InitializedVivoxSystem.cs` | Unchanged logic (login when `PlayerName` is known); channel id = server id from `JoinInfo`. |
| `Client/UI/MonoBehaviours/Settings/UIAudioSettings.cs` | Rename Vivox sliders/keys (`VivoxSpeakerVolume` → `VoiceSpeakerVolume` in `UserSettingsData`, migrate the JSON once). |
| `Mix/Player/MonoBehaviours/PlayerInfoController.cs` | Instantiate the voice prefab (renamed `Voice.prefab`) in multiplayer, as today. |
| `Mix/Settings/ScriptableObjects/MultiplayerServerSettings.cs` | Add `BackendUrl`, region list with probe endpoints; keep `ConnectionTimeout`. |
| `Components/Unity.MegacityMetro.Components.asmdef` | Remove the `Unity.Services.Vivox`, `Unity.Services.Core`, `VivoxUnity` references (unused). |
| `Packages/manifest.json` | Remove `com.unity.services.multiplayer`, `.vivox`, `.authentication`, `.core`, `.deployment`, `.wire`, `com.unity.multiplayer.center` (optional), and transitively `qos`. Keep `com.unity.transport`, `com.unity.netcode`, `com.unity.multiplayer.playmode`. |
| Assets | Delete `Assets/Settings/UGS`, `Assets/Tutorials`, `Assets/Resources/UnityPlayerAccountSettings.asset`, `Assets/Prefabs/UGS/GameServerServices.prefab`; turn `GameServiceData.asset` into the server config. Unlink the cloud project in Project Settings → Services (or keep it; nothing reads `cloudProjectId` once the façades stop checking it). |

### Netcode-side details to get right

- Approval must run before `ServerInGame` spawns the ghost: `ServerInGame` already keys on
  `NetworkId` without `NetworkStreamInGame`; with approval enabled a connection only gets a `NetworkId` after
  it is approved, so the spawn logic needs no change.
- Reserve-slot semantics: the Directory counts a joining player from join-token issue (reserved) to
  `ReportPlayerJoined` (joined) to `ReportPlayerLeft`; expire reservations after 30 s so abandoned joins do not
  block backfill.
- Thin clients: give them a join token through the same matchmaker call; `AddThinClientInputSystem` is untouched.
- The server world should expose a tiny metrics endpoint (players, tick time, ghost count) through the agent;
  the Netcode stats panel already computes most of these on the client side (`Utils/NetcodeExtensions`).

## B.10 Migration plan

| Phase | Work | Exit criteria | Effort |
|---|---|---|---|
| 0. Abstraction | Introduce the interfaces in B.9, wrap the existing UGS calls as providers, delete dead code (`ClientMatchmaker`, orphan prefab), register the driver constructor, remove unused asmdef references. | Game behaves exactly as today with `USE_UGS_SERVICES`. | 1–2 weeks |
| 1. Identity + Directory + Agent | Backend service with `/auth`, `/servers`, `/sessions`; `HostingAgent` in the server; connection approval with join tokens; manual server start (Docker on one host); session browser works end to end. | Two clients join a self-hosted server through the browser; player counts correct; idle server exits. | 2–4 weeks |
| 2. Matchmaker + Fleet | Ticket API, region probes, allocation via Agones or the custom supervisor, autoscale and build rollout from CI. | "Find Match" fills servers to 150 with backfill across two regions; 150 thin clients pass a load test. | 2–4 weeks |
| 3. Voice | `IVoiceService` provider (B.8 option A or B), voice token issuance, UI rename. | Mute, volume, speaking indicator and per-server channel work on desktop and one mobile platform. | 2–6 weeks |
| 4. Cut-over | Remove UGS packages and assets, unlink the cloud project, update `multiplayer-setup.md`/`ugs.md`. | Build has no `Unity.Services` assemblies. | 1 week |
| 5. Optional | Relay for peer hosting, accounts linking, moderation (mute/report), analytics. | – | – |

## B.11 Risks and what you take on

- **Operations**: uptime, on-call, DDoS exposure of public UDP servers (put the backend behind a CDN/WAF; game
  servers can only be rate-limited at the host or cloud layer), certificate management, backups for identity data.
- **Security**: sign and expire every token; never let the client assert its id (today it does); rate-limit ticket
  creation; keep the server-to-backend token out of the client build (server-only assembly).
- **Compliance**: voice and identity data (GDPR, COPPA, platform voice policies); provide mute/block/report and a
  data-deletion path.
- **Voice quality**: without a WebRTC audio pipeline, expect echo and noise complaints on open-mic laptops and
  phones; push-to-talk defaults (already the case) mitigate.
- **Regional coverage and cost**: 150-player servers need roughly 2 cores and 7 GB each; idle shutdown and warm
  pools per region are the main cost levers; measure with the thin-client flow before committing to a fleet size.
- **Netcode approval**: verify the approval API against the installed Netcode version (1.14.2) in a spike before
  Phase 1; the fallback is validating the token in `SetPlayerInfoSystem` and disconnecting unknown players.

## B.12 Decision summary

| Capability | Recommendation |
|---|---|
| Transport and replication | Keep Netcode for Entities + Unity Transport unchanged. |
| Identity | Build (anonymous JWT, device-bound), small. |
| Directory + matchmaking | Build a single service; rules are trivial for this game. Nakama if you want a broader social backend later. |
| Hosting | Agones on Kubernetes for multi-region/autoscale; custom Docker supervisor for a small fleet. |
| Voice | Self-hosted WebRTC SFU for quality-first; in-band Opus over UDP for proximity voice and total control. |
| Relay | Skip (dedicated servers); transparent UDP forwarder if peer hosting is ever added. |
| Tooling | CI builds containers; Grafana/Prometheus from agent heartbeats; a small admin page over the Directory API replaces the dashboard. |
