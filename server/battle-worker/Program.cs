using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KingdomRhapsody;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        IncludeFields = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static int Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--catalog" }))
        {
            Write(new { version = BattlePacket.Version, cards = BattleCatalog.Cards.Select(card => card.Id) });
            return 0;
        }

        if (args.Length != 1)
        {
            Console.Error.WriteLine("Pass the directory containing the battleactor files.");
            return 2;
        }

        try
        {
            var timings = LoadTimings(args[0]);
            BattlePacket initial = Read(Console.ReadLine());
            BattleLoadout first = initial?.first?.Validate(), second = initial?.second?.Validate();
            if (initial?.type != "init" || initial.version != BattlePacket.Version ||
                string.IsNullOrEmpty(initial.match) || first == null || second == null)
                return 2;

            using var incoming = new BlockingCollection<BattlePacket>(128);
            _ = Task.Run(() => Receive(incoming));
            var match = new BattleMatch(first, second, (id, pose) => timings[(id, pose)]);
            Run(match, initial.match, incoming);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static Dictionary<(string, BattlePose), ActionTiming> LoadTimings(string directory)
    {
        var timings = new Dictionary<(string, BattlePose), ActionTiming>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.battleactor"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement actor = doc.RootElement;
            string id = actor.GetProperty("id").GetString();
            foreach (JsonElement clip in actor.GetProperty("clips").EnumerateArray())
                timings.Add((id, Enum.Parse<BattlePose>(clip.GetProperty("pose").GetString())),
                    new ActionTiming(clip.GetProperty("frames").GetInt32(), clip.GetProperty("fps").GetInt32(),
                        clip.GetProperty("cue").GetInt32()));
        }

        if (timings.Count == 0)
            throw new InvalidDataException("No battleactor timing metadata found.");
        return timings;
    }

    private static void Receive(BlockingCollection<BattlePacket> inbox)
    {
        try
        {
            string line;
            while ((line = Console.ReadLine()) != null)
            {
                BattlePacket message = Read(line);
                if (message != null)
                    inbox.Add(message);
            }
            inbox.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
            // The room has already completed.
        }
    }

    private static void Run(BattleMatch match, string id, BlockingCollection<BattlePacket> incoming)
    {
        var replies = new List<BattlePacket>(24);
        var newestSequence = new int[2];
        var clock = Stopwatch.StartNew();
        double next = 1d / BattleMatch.TickRate;
        int lastEffect = 0;
        Publish("start", id, match, newestSequence);
        while (match.Winner == BattleMatch.Playing && !incoming.IsCompleted)
        {
            // A stalled process can catch up several ticks while keeping the play loop bounded.
            double now = clock.Elapsed.TotalSeconds;
            if (now < next)
            {
                Thread.Sleep(Math.Max(1, (int)((next - now) * 1000)));
                continue;
            }
            next = Math.Max(next + 1d / BattleMatch.TickRate, now - .20);

            replies.Clear();
            for (int i = 0; i < 32 && incoming.TryTake(out BattlePacket command); i++)
            {
                if (command.match != id || command.seat < 0 || command.seat > 1)
                    continue;
                if (command.type == "disconnect")
                {
                    match.Forfeit(command.seat);
                    break;
                }
                if (command.seq <= newestSequence[command.seat])
                    continue;
                newestSequence[command.seat] = command.seq;
                replies.Add(Apply(match, id, command));
            }

            match.Step();
            SendEffects(match, id, ref lastEffect);
            if (replies.Count > 0 || match.PresentationChanged || match.Winner != BattleMatch.Playing ||
                match.Tick % (BattleMatch.TickRate / BattleMatch.SnapshotRate) == 0)
                Publish("state", id, match, newestSequence);
            foreach (BattlePacket reply in replies)
                Write(reply);
        }
    }

    private static BattlePacket Apply(BattleMatch match, string id, BattlePacket command)
    {
        PlayResult result = PlayResult.Inactive;
        if (command.type == "play")
            result = match.Play(command.seat, command.slot, BattleCatalog.Find(command.card),
                new Vector2(command.x, command.y), new Vector2(command.dx, command.dy));
        else if (command.type == "sprint")
            result = match.TrySprint(command.seat) ? PlayResult.Accepted :
                match.Live && match.Energy(command.seat) + 1e-8 < BattleMatch.SprintCost ? PlayResult.Energy : PlayResult.Inactive;

        return new BattlePacket
        {
            type = "ack", match = id, to = command.seat, seat = command.seat, seq = command.seq, result = (int)result
        };
    }

    private static void Publish(string type, string id, BattleMatch match, int[] processedSequences)
    {
        var scene = new BattleSceneFrame(match);
        for (int seat = 0; seat < 2; seat++)
            Write(new BattlePacket
            {
                type = type, version = BattlePacket.Version, match = id, to = seat, seat = seat,
                replaceable = type == "state" && match.Winner == BattleMatch.Playing,
                state = WireBattleState.From(match, seat, scene, processedSequences[seat])
            });
    }

    private static void SendEffects(BattleMatch match, string id, ref int lastEffect)
    {
        var batch = new List<WireBattleEffect>(32);
        foreach (BattleEffect effect in match.Effects)
        {
            if (effect.Id <= lastEffect)
                continue;
            batch.Add(WireBattleEffect.From(effect));
            lastEffect = effect.Id;
            if (batch.Count == 32)
            {
                Write(new BattlePacket { type = "effects", match = id, to = -1, effects = batch.ToArray() });
                batch.Clear();
            }
        }
        if (batch.Count > 0)
            Write(new BattlePacket { type = "effects", match = id, to = -1, effects = batch.ToArray() });
    }

    private static BattlePacket Read(string json)
    {
        if (json == null)
            return null;
        try { return JsonSerializer.Deserialize<BattlePacket>(json, Json); }
        catch (JsonException) { return null; }
    }

    private static void Write<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, Json));
}
