using System;
using Unity.Mathematics;
using UnityEngine;

namespace CsgTest
{
    public struct FaceCut : IComparable<FaceCut>, IEquatable<FaceCut>
    {
        public static Comparison<FaceCut> Comparer { get; } = (x, y) => Math.Sign(x.Angle - y.Angle);

        public static FaceCut ExcludeAll => new FaceCut(new float2(-1f, 0f),
            float.PositiveInfinity, float.NegativeInfinity, float.PositiveInfinity);

        public static FaceCut ExcludeNone => new FaceCut(new float2(1f, 0f),
            float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity);

        public static FaceCut operator -(FaceCut cut)
        {
            return new FaceCut(-cut.Normal, -cut.Distance, -cut.Max, -cut.Min);
        }

        public static FaceCut operator +(FaceCut cut, float offset)
        {
            return new FaceCut(cut.Normal, cut.Distance + offset, float.NegativeInfinity, float.PositiveInfinity);
        }

        public static FaceCut operator -(FaceCut cut, float offset)
        {
            return new FaceCut(cut.Normal, cut.Distance - offset, float.NegativeInfinity, float.PositiveInfinity);
        }

        public readonly float2 Normal;
        public readonly float Angle;
        public readonly float Distance;

        public float Min;
        public float Max;

        public float3? MinNormal;

        public bool ExcludesAll => float.IsPositiveInfinity(Distance);
        public bool ExcludesNone => float.IsNegativeInfinity(Distance);

        public FaceCut(float2 normal, float distance, float min, float max) => (Normal, Angle, Distance, Min, Max, MinNormal) = (normal, math.atan2(normal.y, normal.x), distance, min, max, null);
        
        public float3 GetPoint(in (float3 origin, float3 tu, float3 tv) basis)
        {
            var minFinite = !float.IsNegativeInfinity(Min);
            var maxFinite = !float.IsPositiveInfinity(Max);

            return GetPoint(basis, minFinite && maxFinite
                ? (Min + Max) * 0.5f
                : minFinite
                    ? Min + 1f
                    : maxFinite
                        ? Max - 1f
                        : 0f);
        }

        public float3 GetPoint(in (float3 origin, float3 tu, float3 tv) basis, float along)
        {
            var pos = Normal * Distance + new float2(-Normal.y, Normal.x) * math.clamp(along,
                float.IsNegativeInfinity(Min) ? -1024f : Min,
                float.IsPositiveInfinity(Max) ? 1024f : Max);

            return basis.origin + basis.tu * pos.x + basis.tv * pos.y;
        }

        public int CompareTo(FaceCut other)
        {
            return Angle.CompareTo(other.Angle);
        }

        public override string ToString()
        {
            return $"{{ Normal: {Normal}, Distance: {Distance} }}";
        }

        public bool Equals(FaceCut other)
        {
            return Normal.Equals(other.Normal) && Distance.Equals(other.Distance);
        }

        public bool ApproxEquals(FaceCut other,
            float normalEpsilon = BspSolid.Epsilon,
            float offsetEpsilon = BspSolid.Epsilon * 10f)
        {
            return
                math.abs(Normal.x - other.Normal.x) <= normalEpsilon &&
                math.abs(Normal.y - other.Normal.y) <= normalEpsilon &&
                math.abs(Distance - other.Distance) <= offsetEpsilon;
        }

        public override bool Equals(object obj)
        {
            return obj is FaceCut other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Normal.GetHashCode() * 397) ^ Distance.GetHashCode();
            }
        }

        public FaceCut Transform(in float4x4 matrix,
            (float3 origin, float3 tu, float3 tv) oldBasis,
            (float3 origin, float3 tu, float3 tv) newBasis)
        {
            if (float.IsNegativeInfinity(Min) || float.IsPositiveInfinity(Max))
            {
                throw new NotImplementedException();
            }

            var oldTangent = oldBasis.tu * -Normal.y + oldBasis.tv * Normal.x;
            var newTangent = math.normalizesafe(math.mul(matrix, new float4(oldTangent, 0f)).xyz);

            var minPos3 = math.transform(matrix, GetPoint(oldBasis, Min));
            var maxPos3 = math.transform(matrix, GetPoint(oldBasis, Max));
            var midPos3 = (minPos3 + maxPos3) * 0.5f;

            var normal = new float2(
                math.dot(newBasis.tv, newTangent),
                -math.dot(newBasis.tu, newTangent));

            var midPos2 = new float2(
                math.dot(newBasis.tu, midPos3),
                math.dot(newBasis.tv, midPos3));

            var min = math.dot(minPos3, newTangent);
            var max = math.dot(maxPos3, newTangent);

            return new FaceCut(normal, math.dot(normal, midPos2),
                math.min(min, max), math.max(min, max));
        }

        public void DrawDebug(BspPlane plane, Color color)
        {
            DrawDebug(plane.GetBasis(), color);
        }

        public void DrawDebug(in (float3 origin, float3 tu, float3 tv) basis, Color color)
        {
            var min = GetPoint(basis, Min);
            var max = GetPoint(basis, Max);

            var mid = (min + max) * 0.5f;
            var norm = Normal.x * basis.tu + Normal.y * basis.tv;

            Debug.DrawLine(min, max, color);
            Debug.DrawLine(mid, mid + norm, color);
        }

        public void DrawGizmos(BspPlane plane)
        {
            DrawGizmos(plane.GetBasis());
        }

        public void DrawGizmos(in (float3 origin, float3 tu, float3 tv) basis)
        {
            var min = GetPoint(basis, Min);
            var max = GetPoint(basis, Max);

            var mid = (min + max) * 0.5f;
            var norm = Normal.x * basis.tu + Normal.y * basis.tv;

            Gizmos.DrawLine(min, max);
            Gizmos.DrawLine(mid, mid + norm * math.length(max - min) * 0.1f);
        }
    }
}
