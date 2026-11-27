using System.Collections.Generic;
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
            public int frames, fps, cue;
            public bool loop;
        }

        public int tile, rows;
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
        private readonly Dictionary<(BattleActorArt.Clip, int, int), Sprite> frames =
            new Dictionary<(BattleActorArt.Clip, int, int), Sprite>();

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
            double progress = System.Math.Max(0, elapsed);
            int frame;
            if (clip.loop)
                frame = (int)(progress * clip.fps / BattleMatch.TickRate) % clip.frames;
            else
                frame = Mathf.Clamp((int)(progress * clip.frames / Mathf.Max(1, playTicks)), 0, clip.frames - 1);

            var key = (clip, direction, frame);
            if (!frames.TryGetValue(key, out var sprite))
            {
                var rect = new Rect(frame * art.tile, (art.rows - 1 - direction) * art.tile, art.tile, art.tile);
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
