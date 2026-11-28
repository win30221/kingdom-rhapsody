using System;
using System.Collections.Generic;

namespace KingdomRhapsody
{
    internal sealed class PendingBattleCommands
    {
        private struct Command
        {
            internal int Sequence, Slot, Cost;
            internal bool Acknowledged;
        }

        private const int Limit = 5;
        private readonly List<Command> commands = new List<Command>(Limit);
        internal bool Full => commands.Count >= Limit;
        internal int ProcessedSequence { get; private set; }

        internal void Add(int sequence, int slot, int cost) =>
            commands.Add(new Command { Sequence = sequence, Slot = slot, Cost = cost });

        internal void Advance(int processedSequence)
        {
            ProcessedSequence = processedSequence;
            for (int i = commands.Count - 1; i >= 0; i--)
                if (commands[i].Acknowledged && commands[i].Sequence <= ProcessedSequence)
                    commands.RemoveAt(i);
        }

        internal double Energy(double serverEnergy)
        {
            foreach (Command command in commands)
                if (command.Sequence > ProcessedSequence)
                    serverEnergy -= command.Cost;
            return Math.Max(0, serverEnergy);
        }

        internal bool HasSlot(int slot)
        {
            foreach (Command command in commands)
                if (command.Slot == slot && command.Sequence > ProcessedSequence)
                    return true;
            return false;
        }

        internal bool Acknowledge(int sequence, PlayResult result, out int slot)
        {
            for (int i = 0; i < commands.Count; i++)
            {
                Command command = commands[i];
                if (command.Sequence != sequence || command.Acknowledged)
                    continue;
                slot = command.Slot;
                if (result != PlayResult.Accepted || sequence <= ProcessedSequence)
                    commands.RemoveAt(i);
                else
                {
                    // A coalesced browser snapshot can arrive after its acknowledgement.
                    command.Acknowledged = true;
                    commands[i] = command;
                }
                return true;
            }
            slot = -1;
            return false;
        }

        internal void Clear() => commands.Clear();
    }
}
