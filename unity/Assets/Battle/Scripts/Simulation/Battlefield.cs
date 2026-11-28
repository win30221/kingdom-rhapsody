using System;
using System.Collections.Generic;
using System.Numerics;

namespace KingdomRhapsody
{
    internal static class Battlefield
    {
        internal const int Columns = 18, Rows = 32;
        internal const float HalfWidth = Columns / 2f, HalfHeight = Rows / 2f;
        // RiftCourts_2D_Layout.json uses art-centered units. Convert into the battle grid.
        internal const float ArtHalfWidth = 17.6f, ArtHalfHeight = 24;
        internal const float ArtScaleX = HalfWidth / 9.1f, ArtScaleY = HalfHeight / 21.45f;
        internal const float Shore = 5.7f * ArtScaleY;
        internal const float GuardDetection = 6, GuardLeash = 4.5f;
        internal const float HazardInterval = 8, HazardWarning = 1.5f;
        internal const float HazardHalfWidth = 1.9f * ArtScaleX, HazardHalfHeight = 2.5f * ArtScaleY;
        internal const float RiftHalfWidth = .75f;

        private const float LandingEdge = 3.2f * ArtScaleY, LandingHalfWidth = 2.08f * ArtScaleX;
        private static readonly (float Left, float Right)[] bridges =
        {
            (-8.25f * ArtScaleX, -5.85f * ArtScaleX),
            (-HazardHalfWidth, HazardHalfWidth),
            (5.85f * ArtScaleX, 8.25f * ArtScaleX),
        };
        private static readonly Vector2[] patrol = MakePatrol();
        private static readonly float[] patrolDistance = MeasurePatrol();

        internal static int Lane(int column) => column / 6;
        internal static bool Contains(Vector2 point) =>
            Math.Abs(point.X) < HalfWidth && Math.Abs(point.Y) < HalfHeight;

        internal static int Column(Vector2 point) => (int)Math.Floor(point.X + HalfWidth);
        internal static int Row(Vector2 point) => (int)Math.Floor(HalfHeight - point.Y);
        internal static Vector2 Cell(int column, int row) => new(column + .5f - HalfWidth, HalfHeight - row - .5f);

        internal static Vector2 SpawnPoint(int column, int row, float radius)
        {
            Vector2 point = Clamp(Cell(column, row), radius);
            if (Math.Abs(point.Y) >= Shore && Math.Abs(point.Y) < Shore + radius)
                point.Y = Math.Sign(point.Y) * (Shore + radius);
            return point;
        }

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

            Vector2 point = Cell(column, row);
            if (point.Y * (seat == 0 ? -1 : 1) >= Shore)
                return true;

            int ownRow = seat == 0 ? row : Rows - 1 - row;
            if (ownRow < 9 || ownRow > 20 || !IsWalkable(SpawnPoint(column, row, .5f), .5f))
                return false;
            return Lane(column) == 0 && leftUnlocked || Lane(column) == 2 && rightUnlocked;
        }

        internal static bool InHazard(Vector2 point) =>
            Math.Abs(point.X) <= HazardHalfWidth && Math.Abs(point.Y) <= HazardHalfHeight;

        internal static Vector2 GuardHome(int seat, int side) => new(
            (side == 0 ? -7 : 7) * ArtScaleX, (seat == 0 ? -7.24f : 7.24f) * ArtScaleY);

        internal static float BridgeCenter(int lane) => (bridges[lane].Left + bridges[lane].Right) / 2;

        internal static bool IsWalkable(Vector2 point, float radius = 0) =>
            Math.Abs(point.X) <= HalfWidth - radius && Math.Abs(point.Y) <= HalfHeight - radius &&
            (Math.Abs(point.Y) >= Shore + radius || BridgeAt(point, radius) >= 0);

        private static (float Left, float Right) BridgeBounds(int bridge, float y, float radius) =>
            bridge == 1 && Math.Abs(y) >= LandingEdge + radius
                ? (-LandingHalfWidth, LandingHalfWidth) : bridges[bridge];

        private static int BridgeAt(Vector2 point, float radius)
        {
            if (Math.Abs(point.Y) >= Shore + radius)
                return -1;
            for (int i = 0; i < bridges.Length; i++)
            {
                var (left, right) = BridgeBounds(i, point.Y, radius);
                if (point.X >= left + radius && point.X <= right - radius)
                    return i;
            }
            return -1;
        }

        private static bool CrossesBand(Vector2 bottom, Vector2 top, float edge, out float left, out float right)
        {
            left = right = 0;
            if (bottom.Y >= edge || top.Y <= -edge)
                return false;

            float start = bottom.X, end = top.X;
            if (bottom.Y < -edge)
                start += (top.X - bottom.X) * ((-edge - bottom.Y) / (top.Y - bottom.Y));
            if (top.Y > edge)
                end = bottom.X + (top.X - bottom.X) * ((edge - bottom.Y) / (top.Y - bottom.Y));
            left = Math.Min(start, end);
            right = Math.Max(start, end);
            return true;
        }

        internal static bool CanTravel(Vector2 from, Vector2 to, float radius)
        {
            if (!IsWalkable(from, radius) || !IsWalkable(to, radius))
                return false;
            Vector2 bottom = from.Y <= to.Y ? from : to, top = from.Y <= to.Y ? to : from;
            if (!CrossesBand(bottom, top, Shore + radius, out float left, out float right))
                return true;

            if ((left >= bridges[0].Left + radius && right <= bridges[0].Right - radius) ||
                (left >= bridges[2].Left + radius && right <= bridges[2].Right - radius))
                return true;

            return left >= -LandingHalfWidth + radius && right <= LandingHalfWidth - radius &&
                (!CrossesBand(bottom, top, LandingEdge + radius, out left, out right) ||
                (left >= -HazardHalfWidth + radius && right <= HazardHalfWidth - radius));
        }

        private static Vector2 BridgeGate(int lane, int side, float radius) =>
            new(BridgeCenter(lane), side * (Shore + radius + .01f));

        private static Vector2 NearestGround(Vector2 point, float radius)
        {
            point = Clamp(point, radius);
            if (IsWalkable(point, radius))
                return point;

            Vector2 best = new Vector2(point.X, (point.Y >= 0 ? 1 : -1) * (Shore + radius));
            float distance = Vector2.DistanceSquared(best, point);
            for (int lane = 0; lane < bridges.Length; lane++)
            {
                var (left, right) = BridgeBounds(lane, point.Y, radius);
                Vector2 onBridge = new Vector2(Math.Clamp(point.X, left + radius, right - radius), point.Y);
                float score = Vector2.DistanceSquared(onBridge, point);
                if (score < distance)
                {
                    best = onBridge;
                    distance = score;
                }
            }
            return best;
        }

        internal static Vector2 NextWaypoint(Vector2 from, Vector2 goal, float radius, int preferredBridge)
        {
            goal = NearestGround(goal, radius);
            int fromBridge = BridgeAt(from, radius), goalBridge = BridgeAt(goal, radius);
            int fromSide = from.Y >= 0 ? 1 : -1, goalSide = goal.Y >= 0 ? 1 : -1;
            bool acrossCourts = fromBridge < 0 && goalBridge < 0 && fromSide != goalSide;
            if ((!acrossCourts || preferredBridge < 0) && CanTravel(from, goal, radius))
                return goal;

            if (fromBridge >= 0)
            {
                float center = BridgeCenter(fromBridge);
                if (fromBridge == goalBridge)
                    return new Vector2(center, Math.Abs(from.X - center) > .0001f ? from.Y : goal.Y);
                int exitSide = goalBridge < 0 ? goalSide : (from.Y + goal.Y >= 0 ? 1 : -1);
                Vector2 exit = BridgeGate(fromBridge, exitSide, radius);
                return CanTravel(from, exit, radius) ? exit : new Vector2(center, from.Y);
            }
            if (goalBridge >= 0)
            {
                Vector2 gate = BridgeGate(goalBridge, fromSide, radius);
                return Vector2.DistanceSquared(from, gate) < .000001f ? new Vector2(gate.X, goal.Y) : gate;
            }
            if (!acrossCourts)
                return goal;

            int crossing = preferredBridge;
            if (crossing < 0)
            {
                float shortest = float.PositiveInfinity;
                for (int lane = 0; lane < bridges.Length; lane++)
                {
                    float distance = Vector2.Distance(from, BridgeGate(lane, fromSide, radius)) +
                        Vector2.Distance(goal, BridgeGate(lane, goalSide, radius));
                    if (distance < shortest)
                    {
                        shortest = distance;
                        crossing = lane;
                    }
                }
            }
            Vector2 entry = BridgeGate(crossing, fromSide, radius);
            return Vector2.DistanceSquared(from, entry) < .000001f ? BridgeGate(crossing, goalSide, radius) : entry;
        }

        internal static Vector2 LimitMovement(Vector2 from, Vector2 to, float radius)
        {
            to = Clamp(to, radius);
            if (CanTravel(from, to, radius))
                return to;

            // Stop at the last point on stone along this step, including collision pushes.
            Vector2 safe = from, blocked = to;
            for (int i = 0; i < 10; i++)
            {
                Vector2 mid = (safe + blocked) * .5f;
                if (CanTravel(from, mid, radius))
                    safe = mid;
                else
                    blocked = mid;
            }
            return safe;
        }

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
