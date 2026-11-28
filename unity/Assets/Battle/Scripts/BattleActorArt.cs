using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;
using UnityEngine;

namespace KingdomRhapsody
{
    // The source atlases use the same top-to-bottom N, NE, E, ... convention as Tower Duel.
    public sealed class BattleActorArt : ScriptableObject
    {
        [System.Serializable]
        public sealed class Clip
        {
            public BattlePose pose;
            public Texture2D texture;
            public int frames, fps, cue, columns;
            public int[] cells;
            public bool loop;
        }

        public int tile;
        public float worldSize;
        public Vector2 pivot;
        public Clip[] clips;

        internal Clip Find(BattlePose pose)
        {
            foreach (Clip clip in clips)
                if (clip.pose == pose)
                    return clip;
            return clips[0];
        }
    }

    internal sealed class BattleArt
    {
        private readonly Dictionary<string, BattleActorArt> actors = new Dictionary<string, BattleActorArt>();
        private readonly Dictionary<(BattleActorArt.Clip, int), Sprite> frames =
            new Dictionary<(BattleActorArt.Clip, int), Sprite>();

        internal BattleActorArt Actor(string id)
        {
            if (!actors.TryGetValue(id, out BattleActorArt art))
            {
                art = Resources.Load<BattleActorArt>("Battle/Actors/" + id);
                actors.Add(id, art);
            }
            return art;
        }

        internal ActionTiming Timing(string id, BattlePose pose)
        {
            BattleActorArt.Clip clip = Actor(id)?.Find(pose);
            return clip != null ? new ActionTiming(clip.frames, clip.fps, clip.cue) : ActionTiming.Standard;
        }

        internal Sprite Frame(BattleActorArt art, BattlePose pose, int direction, double elapsed, int playTicks)
        {
            BattleActorArt.Clip clip = art.Find(pose);
            double progress = Math.Max(0, elapsed);
            double sample = clip.loop
                ? progress * clip.fps / BattleMatch.TickRate % clip.frames
                : Math.Min(clip.frames - 1, progress * clip.frames / Math.Max(1, playTicks));
            return GetFrame(art, clip, clip.cells[direction * clip.frames + (int)sample]);
        }

        private Sprite GetFrame(BattleActorArt art, BattleActorArt.Clip clip, int cell)
        {
            var key = (clip, cell);
            if (!frames.TryGetValue(key, out Sprite sprite))
            {
                var rect = new Rect(cell % clip.columns * art.tile, cell / clip.columns * art.tile, art.tile, art.tile);
                sprite = Sprite.Create(clip.texture, rect, art.pivot, art.tile, 0, SpriteMeshType.FullRect);
                frames.Add(key, sprite);
            }
            return sprite;
        }

        internal void Clear()
        {
            foreach (Sprite sprite in frames.Values)
                if (sprite)
                    Object.Destroy(sprite);
            frames.Clear();
            actors.Clear();
        }
    }
}
