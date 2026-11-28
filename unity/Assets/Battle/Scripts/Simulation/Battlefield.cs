using System;
using System.Collections.Generic;
using System.Numerics;

namespace KingdomRhapsody
{
    internal static class Battlefield
    {
        internal const int Columns = 18, Rows = 32;
        internal const float HalfWidth = Columns / 2f, HalfHeight = Rows / 2f;
        internal const float GuardDetection = 6, GuardLeash = 4.5f;
        internal const float HazardInterval = 8, HazardWarning = 1.5f;
        internal const float RiftHalfWidth = .75f;

        private static readonly Vector2[] patrol = MakePatrol();
        private static readonly float[] patrolDistance = MeasurePatrol();

        internal static int Lane(int column) => column / 6;
        internal static bool Contains(Vector2 point) =>
            Math.Abs(point.X) < HalfWidth && Math.Abs(point.Y) < HalfHeight;

        internal static int Column(Vector2 point) => (int)Math.Floor(point.X + HalfWidth);
        internal static int Row(Vector2 point) => (int)Math.Floor(HalfHeight - point.Y);
        internal static Vector2 Cell(int column, int row) => new(column + .5f - HalfWidth, HalfHeight - row - .5f);

        internal static bool BlocksSpawn(Vector2 position, float radius, int col, int row, Vector2 center, float spawnRadius)
        {
            float clearance = radius + spawnRadius;
            return (Column(position) == col && Row(position) == row) ||
                Vector2.DistanceSquared(position, center) <= clearance * clearance;
        }

        internal static bool MayDeploy(int seat, int column, int row, bool leftUnlocked, bool rightUnlocked)
        {
            if (column < 0 || column >= Columns || row < 0 || row >= Rows)
                return false;

            int ownRow = seat == 0 ? row : Rows - 1 - row;
            if (ownRow >= 21 && ownRow <= 25)
                return true;
            if (ownRow < 9 || ownRow > 20)
                return false;
            return Lane(column) == 0 && leftUnlocked || Lane(column) == 2 && rightUnlocked;
        }

        internal static bool InHazard(Vector2 point) => Math.Abs(point.X) <= 3 && Math.Abs(point.Y) <= 2;

        internal static Vector2 GuardHome(int seat, int side) => new(side == 0 ? -7.5f : 7.5f, seat == 0 ? -11.5f : 11.5f);

        internal static Vector2 Clamp(Vector2 point, float radius) => new(
            Math.Clamp(point.X, -HalfWidth + radius, HalfWidth - radius),
            Math.Clamp(point.Y, -HalfHeight + radius, HalfHeight - radius));

        internal static Vector2 InsideLeash(Vector2 point, Vector2 home)
        {
            Vector2 offset = point - home;
            return offset.LengthSquared() > GuardLeash * GuardLeash ? home + Vector2.Normalize(offset) * GuardLeash : point;
        }

        internal static Vector2 Patrol(int seat, float distance)
        {
            distance %= patrolDistance[patrolDistance.Length - 1];
            int segment = Array.BinarySearch(patrolDistance, distance);
            if (segment < 0)
                segment = ~segment - 1;
            float progress = (distance - patrolDistance[segment]) / (patrolDistance[segment + 1] - patrolDistance[segment]);
            Vector2 point = Vector2.Lerp(patrol[segment], patrol[segment + 1], progress);
            return seat == 0 ? point : -point;
        }

        private static Vector2[] MakePatrol()
        {
            const float radius = 1.5f;
            var path = new List<Vector2> { new(0, -14.5f) };
            var corners = new Vector2[] { new(4.5f, -13), new(4.5f, -12), new(-4.5f, -12), new(-4.5f, -13) };
            for (int corner = 0; corner < corners.Length; corner++)
                for (int step = 0; step <= 8; step++)
                {
                    float angle = (corner - 1 + step / 8f) * MathF.PI / 2;
                    path.Add(corners[corner] + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
                }
            path.Add(path[0]);
            return path.ToArray();
        }

        private static float[] MeasurePatrol()
        {
            var distance = new float[patrol.Length];
            for (int i = 1; i < patrol.Length; i++)
                distance[i] = distance[i - 1] + Vector2.Distance(patrol[i - 1], patrol[i]);
            return distance;
        }
    }
}
