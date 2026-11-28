using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace KingdomRhapsody.Editor
{
    [ScriptedImporter(3, "battleactor")]
    public sealed class BattleActorImporter : ScriptedImporter
    {
        #pragma warning disable CS0649 // JsonUtility fills these import descriptors.
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
            public int frames, fps, cue, edgeAlpha;
            public bool loop, inbetween;
            public Erasure[] erase;
        }

        [Serializable]
        private sealed class Erasure
        {
            public int row, first, count, x, y, width, height;

            internal bool Applies(int direction, int frame) => direction == row && frame >= first && frame < first + count;
            internal bool Contains(int px, int py) => px >= x && px < x + width && py >= y && py < y + height;
        }
        #pragma warning restore CS0649

        public override void OnImportAsset(AssetImportContext context)
        {
            Source source = JsonUtility.FromJson<Source>(File.ReadAllText(context.assetPath));
            var art = ScriptableObject.CreateInstance<BattleActorArt>();
            art.name = source.id;
            art.tile = source.size > 256 ? 120 : 96;
            art.worldSize = source.size / (float)source.pixelsPerUnit;
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
                        raw.width != source.size * source.columns || raw.height != source.size * source.rows ||
                        detail.frames < 1 || detail.frames > source.columns)
                        throw new InvalidDataException("Unexpected directional atlas dimensions in " + detail.path);

                    Texture2D runtime = Pack(raw, source, detail, art.tile, out int columns, out int[] cells);
                    runtime.name = art.name + " " + detail.pose;
                    context.AddObjectToAsset(detail.pose, runtime);
                    art.clips[i] = new BattleActorArt.Clip
                    {
                        pose = (BattlePose)Enum.Parse(typeof(BattlePose), detail.pose),
                        texture = runtime,
                        frames = detail.frames,
                        fps = detail.fps,
                        cue = detail.cue,
                        loop = detail.loop,
                        columns = columns,
                        cells = cells
                    };
                }
                finally { DestroyImmediate(raw); }
            }
            context.AddObjectToAsset("actor", art);
            context.SetMainObject(art);
        }

        private static Texture2D Pack(Texture2D raw, Source source, SourceClip clip, int tile, out int columns, out int[] cells)
        {
            Color32[] pixels = raw.GetPixels32();
            var unique = new List<Color32[]>();
            var hashes = new Dictionary<ulong, List<int>>();
            cells = new int[source.rows * clip.frames];
            for (int row = 0; row < source.rows; row++)
            {
                var frames = new Color32[clip.frames][];
                for (int frame = 0; frame < clip.frames; frame++)
                {
                    int x = frame * source.size;
                    int y = (source.rows - 1 - row) * source.size;
                    Erasure[] masks = Array.FindAll(clip.erase ?? Array.Empty<Erasure>(), mask => mask.Applies(row, frame));
                    frames[frame] = Sample(pixels, raw.width, x, y, source.size, tile, Math.Clamp(clip.edgeAlpha, 0, 254), masks);
                }
                if (clip.loop && clip.inbetween)
                    FillLoop(frames, tile);

                for (int frame = 0; frame < clip.frames; frame++)
                {
                    Color32[] sampled = frames[frame];
                    ulong hash = Hash(sampled);
                    if (!hashes.TryGetValue(hash, out List<int> candidates))
                    {
                        candidates = new List<int>();
                        hashes.Add(hash, candidates);
                    }
                    int found = candidates.FindIndex(index => Equal(unique[index], sampled));
                    int tileIndex;
                    if (found < 0)
                    {
                        tileIndex = unique.Count;
                        unique.Add(sampled);
                        candidates.Add(tileIndex);
                    }
                    else
                        tileIndex = candidates[found];
                    cells[row * clip.frames + frame] = tileIndex;
                }
            }

            columns = Math.Min(source.columns, unique.Count);
            int rows = (unique.Count + columns - 1) / columns;
            int width = columns * tile;
            var output = new Color32[width * rows * tile];
            for (int index = 0; index < unique.Count; index++)
            {
                int x = index % columns * tile, y = index / columns * tile;
                for (int dy = 0; dy < tile; dy++)
                    Array.Copy(unique[index], dy * tile, output, (y + dy) * width + x, tile);
            }

            var result = new Texture2D(width, rows * tile, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            result.SetPixels32(output);
            result.Apply(false, true);
            return result;
        }

        private static void FillLoop(Color32[][] frames, int tile)
        {
            var source = (Color32[][])frames.Clone();
            for (int start = 0; start < source.Length; start++)
            {
                if (Equal(source[start], source[(start + source.Length - 1) % source.Length]))
                    continue;
                int count = 1;
                while (count < source.Length && Equal(source[start], source[(start + count) % source.Length]))
                    count++;
                if (count > 1)
                    LoopPoseInbetweens.Fill(frames, start, count, tile);
            }
        }

        private static Color32[] Sample(Color32[] source, int width, int cx, int cy, int size, int tile, int floor, Erasure[] masks)
        {
            var output = new Color32[tile * tile];
            for (int y = 0; y < tile; y++)
            {
                int y0 = y * size / tile, y1 = (y + 1) * size / tile;
                for (int x = 0; x < tile; x++)
                {
                    int x0 = x * size / tile, x1 = (x + 1) * size / tile;
                    int red = 0, green = 0, blue = 0, alpha = 0;
                    for (int sy = y0; sy < y1; sy++)
                        for (int sx = x0; sx < x1; sx++)
                        {
                            Color32 sample = source[(cy + sy) * width + cx + sx];
                            if (sample.a <= floor || IsErased(sx, size - 1 - sy, masks))
                                continue;
                            int a = (sample.a - floor) * 255 / (255 - floor);
                            alpha += a;
                            red += sample.r * a;
                            green += sample.g * a;
                            blue += sample.b * a;
                        }
                    if (alpha > 0)
                        output[y * tile + x] = new Color32((byte)(red / alpha), (byte)(green / alpha),
                            (byte)(blue / alpha), (byte)(alpha / ((y1 - y0) * (x1 - x0))));
                }
            }
            return output;
        }

        private static bool IsErased(int x, int y, Erasure[] masks)
        {
            foreach (Erasure mask in masks)
                if (mask.Contains(x, y))
                    return true;
            return false;
        }

        private static ulong Hash(Color32[] frame)
        {
            ulong hash = 14695981039346656037UL;
            foreach (Color32 pixel in frame)
            {
                uint rgba = pixel.r | (uint)pixel.g << 8 | (uint)pixel.b << 16 | (uint)pixel.a << 24;
                hash = unchecked((hash ^ rgba) * 1099511628211UL);
            }
            return hash;
        }

        private static bool Equal(Color32[] a, Color32[] b)
        {
            for (int i = 0; i < a.Length; i++)
                if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a)
                    return false;
            return true;
        }
    }
}
