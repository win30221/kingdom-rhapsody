using System;
using UnityEngine;

namespace KingdomRhapsody.Editor
{
    internal static class LoopPoseInbetweens
    {
        private const byte OutlineAlpha = 96;
        private const float Diagonal = 1.4142136f;

        internal static void Fill(Color32[][] frames, int start, int count, int side)
        {
            var from = new Pose(frames[start], side);
            var to = new Pose(frames[(start + count) % frames.Length], side);
            if (from.Right <= from.Left || to.Right <= to.Left)
                return;

            for (int i = 1; i < count; i++)
                frames[(start + i) % frames.Length] = Between(from, to, side, (float)i / count);
        }

        private static Color32[] Between(Pose from, Pose to, int side, float progress)
        {
            var output = new Color32[side * side];
            float left = Mix(from.Left, to.Left, progress), right = Mix(from.Right, to.Right, progress);
            for (int x = 0; x < side; x++)
            {
                // Follow the pose's width and each column's height as limbs move.
                float across = (x - left) / (right - left);
                float ax = Mix(from.Left, from.Right, across), bx = Mix(to.Left, to.Right, across);
                from.Column(ax, out float lowA, out float highA);
                to.Column(bx, out float lowB, out float highB);
                float low = Mix(lowA, lowB, progress), high = Mix(highA, highB, progress);
                for (int y = 0; y < side; y++)
                {
                    float up = (y - low) / Math.Max(1, high - low);
                    float ay = Mix(lowA, highA, up), by = Mix(lowB, highB, up);
                    float fromDistance = from.Distance(ax, ay), toDistance = to.Distance(bx, by);
                    float distance = Mix(fromDistance, toDistance, progress);
                    byte alpha = (byte)(255 * Math.Clamp(distance + .5f, 0, 1));
                    if (alpha == 0)
                        continue;

                    float fromWeight = (1 - progress) * Math.Clamp(fromDistance + .5f, 0, 1);
                    float toWeight = progress * Math.Clamp(toDistance + .5f, 0, 1);
                    Color32 color = Color32.Lerp(from.ColorAt(ax, ay), to.ColorAt(bx, by), toWeight / (fromWeight + toWeight));
                    color.a = alpha;
                    output[y * side + x] = color;
                }
            }
            return output;
        }

        private static float Mix(float a, float b, float fraction) => a + (b - a) * fraction;

        private sealed class Pose
        {
            internal readonly int Left, Right;
            private readonly int side;
            private readonly int[] bottom, top, nearest;
            private readonly Color32[] pixels;
            private readonly float[] signed;

            internal Pose(Color32[] pixels, int side)
            {
                this.pixels = pixels;
                this.side = side;
                Left = side;
                Right = -1;
                bottom = new int[side];
                top = new int[side];
                var inside = new bool[pixels.Length];
                Array.Fill(bottom, side);
                Array.Fill(top, -1);
                for (int y = 0; y < side; y++)
                    for (int x = 0; x < side; x++)
                    {
                        int index = y * side + x;
                        if (pixels[index].a < OutlineAlpha)
                            continue;
                        inside[index] = true;
                        Left = Math.Min(Left, x);
                        Right = Math.Max(Right, x);
                        bottom[x] = Math.Min(bottom[x], y);
                        top[x] = y;
                    }
                if (Right <= Left)
                    return;

                for (int x = Left + 1; x < Right; x++)
                    if (top[x] < 0)
                    {
                        top[x] = top[x - 1];
                        bottom[x] = bottom[x - 1];
                    }

                // A signed distance gives the rendered frame one alpha outline.
                // Nearest opaque colors keep edge samples from darkening.
                nearest = new int[pixels.Length];
                float[] toInside = Distances(inside, true, side, nearest);
                float[] toOutside = Distances(inside, false, side, null);
                signed = new float[pixels.Length];
                for (int i = 0; i < signed.Length; i++)
                    signed[i] = inside[i] ? toOutside[i] - .5f : .5f - toInside[i];
            }

            internal void Column(float x, out float low, out float high)
            {
                x = Math.Clamp(x, Left, Right);
                int a = (int)x, b = Math.Min(Right, a + 1);
                low = Mix(bottom[a], bottom[b], x - a);
                high = Math.Max(low + 2, Mix(top[a], top[b], x - a));
            }

            internal float Distance(float x, float y)
            {
                float sx = Math.Clamp(x, 0, side - 1), sy = Math.Clamp(y, 0, side - 1);
                int x0 = (int)sx, y0 = (int)sy;
                int x1 = Math.Min(side - 1, x0 + 1), y1 = Math.Min(side - 1, y0 + 1);
                float a = Mix(signed[y0 * side + x0], signed[y0 * side + x1], sx - x0);
                float b = Mix(signed[y1 * side + x0], signed[y1 * side + x1], sx - x0);
                return Mix(a, b, sy - y0) - Math.Abs(sx - x) - Math.Abs(sy - y);
            }

            internal Color32 ColorAt(float x, float y)
            {
                float sx = Math.Clamp(x, 0, side - 1), sy = Math.Clamp(y, 0, side - 1);
                int x0 = (int)sx, y0 = (int)sy;
                int x1 = Math.Min(side - 1, x0 + 1), y1 = Math.Min(side - 1, y0 + 1);
                Color32 a = Color32.Lerp(pixels[nearest[y0 * side + x0]], pixels[nearest[y0 * side + x1]], sx - x0);
                Color32 b = Color32.Lerp(pixels[nearest[y1 * side + x0]], pixels[nearest[y1 * side + x1]], sx - x0);
                return Color32.Lerp(a, b, sy - y0);
            }
        }

        private static float[] Distances(bool[] inside, bool seed, int side, int[] nearest)
        {
            var distances = new float[inside.Length];
            for (int i = 0; i < distances.Length; i++)
            {
                distances[i] = inside[i] == seed ? 0 : side * 2;
                if (nearest != null)
                    nearest[i] = i;
            }

            // Two chamfer sweeps propagate the nearest inside or outside pixel.
            for (int step = 1; step >= -1; step -= 2)
            {
                int first = step == 1 ? 0 : side - 1;
                for (int y = first; y >= 0 && y < side; y += step)
                    for (int x = first; x >= 0 && x < side; x += step)
                    {
                        int index = y * side + x;
                        Relax(x - step, y, 1);
                        Relax(x - 1, y - step, Diagonal);
                        Relax(x, y - step, 1);
                        Relax(x + 1, y - step, Diagonal);

                        void Relax(int nx, int ny, float weight)
                        {
                            if (nx < 0 || ny < 0 || nx >= side || ny >= side)
                                return;
                            int adjacent = ny * side + nx;
                            float distance = distances[adjacent] + weight;
                            if (distance >= distances[index])
                                return;
                            distances[index] = distance;
                            if (nearest != null)
                                nearest[index] = nearest[adjacent];
                        }
                    }
            }
            return distances;
        }
    }
}
