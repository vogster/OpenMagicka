# ADR-0002. Network play: our own protocol, host authority, transport abstraction

- **Status:** accepted
- **Date:** 2026-10-23
- **Tasks:** OM-58, OM-59, OM-30

## Context

Network co-op (up to 4 players) and versus must be kept. How the original works
([Entities, AI and network](../engine/ENTITIES_AI.md) §12):

- Steam P2P transport (old API), up to 4 players;
- reliable messages go through the host as a star; clients send snapshots of their own avatar
  directly to each other (mesh), 30 Hz, extrapolated by RTT/2;
- the host runs AI, spawning, triggers and damage to NPCs; the client decides its own avatar's
  movement and hits (`DamageRequest`);
- 68 packet types, 20 `ActionType`s;
- the network `Handle` of an entity is its index in a global list. It matches across machines
  only because the pools are created in the same order;
- variable logic time step, no tick numbers.

## Decision

### Compatibility

**No compatibility with the original client.** Only OpenMagicka clients play with each other.
Compatibility would mean repeating the index-based IDs, the old Steam P2P and the sync bugs.

### Model

1. **Host authority over the shared world:** AI, spawning, triggers and level scripts, damage
   to NPCs, physics objects, level state.
2. **The client decides for its own wizard:** movement without delay and the element queue.
   Hits are sent to the host as requests; the host applies the damage and broadcasts the result.
3. **Fixed tick** of 60 Hz ([ADR-0001](0001-stack.md)), tick number in every packet.
4. **Snapshots** at 30 Hz with interpolation (~100 ms buffer), fields quantized after the
   original's `EntityFeatures`. Other wizards are shown interpolated, not extrapolated.
5. **Two channels:** unreliable (snapshots) and reliable ordered (events, spawns, triggers,
   dialogs, scene changes).
6. **Explicit network IDs** (`NetId`) assigned by the host instead of pool indices.
7. **Joining a game in progress:** the host sends a full world snapshot (entities, trigger
   counters and timers, scene state), then regular snapshots.
8. **Star topology through the host** for all traffic, including avatar snapshots: simpler with
   NAT and against cheating; the extra hop is acceptable for co-op.
9. **Protocol version and content hash** in the handshake: only the same OpenMagicka version
   with the same assets can connect.

### Transport

An `INetTransport` interface (connect, two channels, RTT, disconnect) with implementations:

| Implementation | Purpose |
|---|---|
| **UDP: LiteNetLib or GameNetworkingSockets** (to be chosen in OM-58) | main: LAN, connect by IP, P2P with NAT punch-through |
| **Our own lobby and relay server** (open source, anyone can host it) | finding games, NAT punch, relay when P2P fails |
| **Steamworks** (optional, later) | lobbies and friend invites. Running under the original's AppID 42910 is a legal grey area; to be decided separately |
| **Loopback** | local play and tests: single player goes through the same code as network play |

## Consequences

- The network code is written from scratch for our model; the original is a reference, not a spec.
- `EntityManager` works with `NetId` and the fixed tick from day one (OM-30, OM-19).
- Single player is a host with a loopback transport, so the network code is exercised all the time.
- A small server project (lobby + relay) is part of the repository.
- Steamworks remains an open question and does not block M6.
