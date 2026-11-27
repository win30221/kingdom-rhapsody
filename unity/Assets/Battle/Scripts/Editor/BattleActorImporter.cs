using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace KingdomRhapsody.Editor
{
    [ScriptedImporter(1, "battleactor")]
    public sealed class BattleActorImporter : ScriptedImporter
    {
        [Serializable]
        private sealed class Source
        {
            public string id;
            public int size, rows, columns, pixelsPerUnit;
            public float pivotX, pivotY;
            public SourceClip[] clips;
        }

        [Serializable]
        private sealed class SourceClip
        {
            public string path, pose;
            public int frames, fps, cue;
            public bool loop;
        }

        public override void OnImportAsset(AssetImportContext context)
        {
            Source source = JsonUtility.FromJson<Source>(File.ReadAllText(context.assetPath));
            var art = ScriptableObject.CreateInstance<BattleActorArt>();
            art.name = source.id;
            art.tile = source.size > 256 ? 120 : 96;
            art.worldSize = source.size / (float)source.pixelsPerUnit;
            art.rows = source.rows;
            art.pivot = new Vector2(source.pivotX, source.pivotY);
            art.clips = new BattleActorArt.Clip[source.clips.Length];

            for (int i = 0; i < source.clips.Length; i++)
            {
                SourceClip detail = source.clips[i];
                string path = CharacterArtSource.Resolve(detail.path);
                var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!ImageConversion.LoadImage(raw, File.ReadAllBytes(path), false) ||
                        raw.width != source.size * source.columns || raw.height != source.size * source.rows)
                        throw new InvalidDataException("Unexpected directional atlas dimensions in " + detail.path);

                    Texture2D runtime = Downsample(raw, source.size, art.tile);
                    runtime.name = art.name + " " + detail.pose;
                    context.AddObjectToAsset(detail.pose, runtime);
                    art.clips[i] = new BattleActorArt.Clip
                    {
                        pose = (BattlePose)Enum.Parse(typeof(BattlePose), detail.pose),
                        texture = runtime,
                        frames = detail.frames,
                        fps = detail.fps,
                        cue = detail.cue,
                        loop = detail.loop
                    };
                }
                finally { DestroyImmediate(raw); }
            }
            context.AddObjectToAsset("actor", art);
            context.SetMainObject(art);
        }

        private static Texture2D Downsample(Texture2D raw, int sourceTile, int tile)
        {
            int width = raw.width / sourceTile * tile, height = raw.height / sourceTile * tile;
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] source = raw.GetPixels32();
            var output = new Color32[width * height];

            // Each sample is confined to its frame. Alpha-weighted colors keep thin
            // weapons and transparent edges from picking up a dark or white halo.
            for (int y = 0; y < height; y++)
            {
                int y0 = y * sourceTile / tile;
                int y1 = (y + 1) * sourceTile / tile;
                for (int x = 0; x < width; x++)
                {
                    int x0 = x * sourceTile / tile;
                    int x1 = (x + 1) * sourceTile / tile;
                    int red = 0, green = 0, blue = 0, alpha = 0;
                    for (int sy = y0; sy < y1; sy++)
                        for (int sx = x0; sx < x1; sx++)
                        {
                            Color32 sample = source[sy * raw.width + sx];
                            alpha += sample.a;
                            red += sample.r * sample.a;
                            green += sample.g * sample.a;
                            blue += sample.b * sample.a;
                        }
                    if (alpha > 0)
                        output[y * width + x] = new Color32((byte)(red / alpha), (byte)(green / alpha),
                            (byte)(blue / alpha), (byte)(alpha / ((y1 - y0) * (x1 - x0))));
                }
            }

            result.SetPixels32(output);
            result.Apply(false, true);
            return result;
        }
    }
}
