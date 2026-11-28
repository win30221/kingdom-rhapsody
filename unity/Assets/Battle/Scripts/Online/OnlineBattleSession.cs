using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Realtime.Transport;
using UnityEngine;
using Vector2 = System.Numerics.Vector2;

namespace KingdomRhapsody
{
    internal sealed class OnlineBattleSession : BattleSession, IDisposable
    {
        private readonly MessageTransport transport = new MessageTransport();
        private readonly Queue<byte[]> outgoing = new Queue<byte[]>();
        private readonly PendingBattleCommands pending = new PendingBattleCommands();
        private readonly List<BattleEffect> effects = new List<BattleEffect>();
        private WireBattleState state;
        private BattleSceneFrame scene;
        private Task<bool> sending;
        private string matchId;
        private int seat, sequence;
        private bool disposed;

        internal event Action<BattleSceneFrame, double, bool> Snapshot;
        internal event Action<int, PlayResult> CommandResult;
        internal event Action<string> Status;

        internal static double Now => MessageTransport.ClockMS / 1000;
        internal override int Seat => seat;
        internal override int Tick => state?.tick ?? 0;
        internal override int Winner => state?.winner ?? BattleMatch.Playing;
        internal override int Territory => state?.territory ?? 0;
        internal override int SprintTicksLeft => state?.cooldown ?? 0;
        internal override IReadOnlyList<BattleEffect> Effects => effects;
        internal override bool CanSprint => state != null && state.canSprint && !pending.Full && !pending.HasSlot(-1) &&
            Energy + 1e-8 >= BattleMatch.SprintCost;
        internal override BattleCard NextCard => BattleCatalog.Find(state?.nextCard);
        internal override BattleCard Hand(int slot) => state != null ? BattleCatalog.Find(state.hand[slot]) : null;

        internal override double Energy => pending.Energy(state?.energy ?? 0);

        internal void Connect(string address, BattleLoadout loadout)
        {
            if (!TryEndpoint(address, out Uri endpoint))
                throw new ArgumentException("Unsupported game server address.", nameof(address));
#if UNITY_WEBGL && !UNITY_EDITOR
            transport.BrowserRuntimeURL = Application.streamingAssetsPath.TrimEnd('/') + "/KingdomRhapsody/transport.js";
#endif
            int attempt = transport.BeginAttempt();
            transport.OpenBinary(attempt, endpoint.AbsoluteUri, Encode(new BattlePacket
            {
                type = "queue", version = BattlePacket.Version, loadout = WireLoadout.From(loadout)
            }), coalesceBattleSnapshots: true);
        }

        internal static bool TryEndpoint(string address, out Uri endpoint)
        {
            endpoint = null;
            if (string.IsNullOrWhiteSpace(address) || address.Length > 350 ||
                !Uri.TryCreate(address.Trim(), UriKind.Absolute, out Uri input) || !string.IsNullOrEmpty(input.UserInfo) ||
                string.IsNullOrEmpty(input.Host))
                return false;
            string scheme = input.Scheme switch { "http" => "ws", "https" => "wss", "ws" => "ws", "wss" => "wss", _ => null };
            if (scheme == null)
                return false;
            endpoint = new UriBuilder(input)
            {
                Scheme = scheme, Path = "/ws", Query = "", Fragment = ""
            }.Uri;
            return true;
        }

        internal void Pump()
        {
            if (disposed)
                return;
            while (transport.TryReadPacket(out var packet))
            {
                if (packet.ErrorCode != null)
                {
                    Fail(packet.ErrorCode);
                    return;
                }
                BattlePacket envelope;
                try
                {
                    envelope = JsonUtility.FromJson<BattlePacket>(Encoding.UTF8.GetString(packet.Data));
                    Receive(envelope, packet.ReceivedMS / 1000);
                }
                catch (Exception error) when (error is ArgumentException || error is InvalidOperationException ||
                    error is NullReferenceException || error is FormatException)
                {
                    Fail("invalid_server_message");
                    return;
                }
                if (disposed)
                    return;
            }

            if (sending != null && sending.IsCompleted)
            {
                if (sending.Status == TaskStatus.RanToCompletion && sending.Result)
                    outgoing.Dequeue();
                sending = null;
            }
            if (sending == null && outgoing.Count > 0)
                sending = transport.TrySendBytes(outgoing.Peek());
        }

        private void Receive(BattlePacket packet, double received)
        {
            switch (packet.type)
            {
                case "waiting":
                    Status?.Invoke("waiting");
                    return;
                case "error":
                    Fail(packet.code);
                    return;
                case "start":
                    if (state != null || packet.version != BattlePacket.Version || packet.seat < 0 || packet.seat > 1 ||
                        string.IsNullOrEmpty(packet.match))
                        throw new FormatException("Invalid battle start.");
                    matchId = packet.match;
                    seat = packet.seat;
                    UpdateScene(packet, received, true);
                    return;
            }

            if (matchId == null || packet.match != matchId)
                return;
            switch (packet.type)
            {
                case "state":
                    if (packet.seat == seat && packet.state?.Follows(state) == true)
                        UpdateScene(packet, received, false);
                    break;
                case "effects":
                    foreach (WireBattleEffect effect in packet.effects ?? Array.Empty<WireBattleEffect>())
                        if (effect.end > effect.begin && effect.end + 120 >= Tick)
                            effects.Add(effect.Read());
                    break;
                case "ack":
                    var result = (PlayResult)packet.result;
                    if (packet.seat == seat && pending.Acknowledge(packet.seq, result, out int slot))
                        CommandResult?.Invoke(slot, result);
                    break;
            }
        }

        private void UpdateScene(BattlePacket packet, double received, bool initial)
        {
            WireBattleState next = packet.state;
            if (next == null || next.hand == null || next.hand.Length != BattleMatch.HandSize ||
                next.actors == null || next.actors.Length > 2 * BattleMatch.MaxUnitsPerSide ||
                next.tick < 0 || next.winner < BattleMatch.Playing || next.winner > 1 ||
                next.processedSeq < pending.ProcessedSequence || next.processedSeq > sequence ||
                BattleCatalog.Find(next.nextCard) == null)
                throw new FormatException("Invalid battle state.");
            foreach (string id in next.hand)
                if (BattleCatalog.Find(id) == null)
                    throw new FormatException("Invalid battle hand.");

            state = next;
            scene = new BattleSceneFrame(next);
            pending.Advance(next.processedSeq);
            effects.RemoveAll(effect => effect.End + 120 < Tick);
            Snapshot?.Invoke(scene, received, initial);
        }

        private void Fail(string code)
        {
            Dispose();
            if (Winner == BattleMatch.Playing)
                Status?.Invoke(code ?? "connection_closed");
        }

        internal override PlayResult Validate(int slot, BattleCard card, Vector2 point)
        {
            if (disposed || !Live)
                return PlayResult.Inactive;
            if (slot < 0 || slot >= BattleMatch.HandSize || card == null || Hand(slot) != card)
                return PlayResult.CardChanged;
            if (pending.HasSlot(slot) || pending.Full)
                return PlayResult.Pending;
            if (Energy + 1e-8 < card.Cost)
                return PlayResult.Energy;
            if (!Battlefield.Contains(point))
                return PlayResult.Bounds;
            if (card.IsSpell)
                return PlayResult.Accepted;

            int col = Battlefield.Column(point), row = Battlefield.Row(point);
            if (!MayDeploy(Territory, Seat, col, row))
                return PlayResult.Territory;
            Vector2 center = Battlefield.Cell(col, row);
            int count = 0;
            foreach (BattleActorFrame actor in scene.Actors)
            {
                if (actor.Seat == Seat)
                    count++;
                BattleCard model = BattleCatalog.Unit(actor.CardId);
                if (model != null && Battlefield.BlocksSpawn(actor.Position, model.Radius, col, row, center, card.Radius))
                    return PlayResult.Occupied;
            }
            return count >= BattleMatch.MaxUnitsPerSide ? PlayResult.UnitLimit : PlayResult.Accepted;
        }

        internal override PlayResult Play(int slot, BattleCard card, Vector2 point, Vector2 direction)
        {
            PlayResult result = Validate(slot, card, point);
            if (result == PlayResult.Accepted)
            {
                Queue(new BattlePacket
                {
                    type = "play", slot = slot, card = card.Id, x = point.X, y = point.Y, dx = direction.X, dy = direction.Y
                }, slot, card.Cost);
                return PlayResult.Pending;
            }
            return result;
        }

        internal override bool Sprint()
        {
            if (disposed || !Live || !CanSprint)
                return false;
            Queue(new BattlePacket { type = "sprint" }, -1, BattleMatch.SprintCost);
            return true;
        }

        private void Queue(BattlePacket packet, int slot, int cost)
        {
            packet.match = matchId;
            packet.seq = ++sequence;
            pending.Add(packet.seq, slot, cost);
            outgoing.Enqueue(Encode(packet));
        }

        private static byte[] Encode(BattlePacket packet) => Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet));

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            transport.Dispose();
            outgoing.Clear();
            pending.Clear();
        }
    }
}
