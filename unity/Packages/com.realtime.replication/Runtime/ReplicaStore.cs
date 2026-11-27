using System;
using System.Collections.Generic;
using Realtime.Core;

namespace Realtime.Replication
{
    public sealed class ReplicaStore<T> : IReplicaStore<T> where T : class
    {
        private readonly Func<T,string> key, life;
        private readonly Func<T,T> clone;
        private readonly int capacity;
        private readonly Dictionary<string,T> values = new Dictionary<string,T>(StringComparer.Ordinal);
        private readonly Dictionary<string,T> staging = new Dictionary<string,T>(StringComparer.Ordinal);
        private readonly List<string> added = new List<string>(), updated = new List<string>(), removed = new List<string>();
        private uint generation;
        private long tick = -1;
        public IReadOnlyDictionary<string,T> Values => values;
        public IReadOnlyList<string> Added => added;
        public IReadOnlyList<string> Updated => updated;
        public IReadOnlyList<string> Removed => removed;
        public ReplicaStore(Func<T,string> key, Func<T,string> life, Func<T,T> clone, int capacity = 1024)
        { this.key=key??throw new ArgumentNullException(nameof(key)); this.life=life??throw new ArgumentNullException(nameof(life)); this.clone=clone??throw new ArgumentNullException(nameof(clone)); if(capacity<1)throw new ArgumentOutOfRangeException(nameof(capacity)); this.capacity=capacity; }
        public bool Apply(uint nextGeneration, long nextTick, T[] source)
        {
            if (source==null || source.Length>capacity || nextTick<0 || nextGeneration<generation || nextGeneration==generation && nextTick<=tick) return false;
            // Stage and validate the entire roster before mutating active state.
            var next = staging;
            try {
                foreach(var item in source) {
                    if(item==null) return false; string id=key(item);
                    if(string.IsNullOrEmpty(id) || next.ContainsKey(id))return false;
                    var copy=clone(item); if(copy==null || ReferenceEquals(copy,item))throw new InvalidOperationException("Replica model must clone its mutable state"); next.Add(id,copy);
                }
                added.Clear(); updated.Clear(); removed.Clear();
                bool reset=nextGeneration!=generation;
                foreach(var old in values) if(reset || !next.ContainsKey(old.Key) || life(old.Value)!=life(next[old.Key]))removed.Add(old.Key);
                foreach(var item in next) {
                    if(reset || !values.TryGetValue(item.Key,out var old) || life(old)!=life(item.Value))added.Add(item.Key);else updated.Add(item.Key);
                }
                // Keep the published Values dictionary stable for callers that retained its view.
                values.Clear(); foreach(var item in next)values.Add(item.Key,item.Value);
                generation=nextGeneration; tick=nextTick; return true;
            } finally {
                // Release clones after success, invalid rosters and callback exceptions, retaining capacity.
                next.Clear();
            }
        }
        public void Clear() { values.Clear();added.Clear();updated.Clear();removed.Clear();generation=0;tick=-1; }
    }
}
