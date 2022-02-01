using System;
using Unity.Mathematics;

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

        public readonly float2 Normal;
        public readonly float Angle;
        public readonly float Distance;

        public float Min;
        public float Max;

        public bool ExcludesAll => float.IsPositiveInfinity(Distance);
        public bool ExcludesNone => float.IsNegativeInfinity(Distance);

        public FaceCut(float2 normal, float distance, float min, float max) => (Normal, Angle, Distance, Min, Max) = (normal, math.atan2(normal.y, normal.x), distance, min, max);
        
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

        public FaceCut GetCompliment(in (float3 origin, float3 tu, float3 tv) oldBasis, in (float3 origin, float3 tu, float3 tv) newBasis)
        {
            return Transform(float4x4.identity, float4x4.Scale(-1f), oldBasis, newBasis);
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

        public FaceCut Transform(in float4x4 matrix, in float4x4 normalMatrix,
            (float3 origin, float3 tu, float3 tv) oldBasis,
            (float3 origin, float3 tu, float3 tv) newBasis)
        {
            var normal3 = Normal.x * oldBasis.tu + Normal.y * oldBasis.tv;
            var pos = oldBasis.origin + normal3 * Distance;

            normal3 = math.normalizesafe(math.mul(normalMatrix, new float4(normal3, 0f)).xyz);
            pos = math.transform(matrix, pos);

            var newNormal = math.normalizesafe(new float2(
                math.dot(newBasis.tu, normal3),
                math.dot(newBasis.tv, normal3)));

            var min = Min;
            var max = Max;

            var tangent = new float2(-newNormal.y, newNormal.x);

            if (!float.IsNegativeInfinity(min))
            {
                var minPos3 = math.transform(matrix, GetPoint(oldBasis, min));
                var minPos = new float2(math.dot(newBasis.tu, minPos3),
                    math.dot(newBasis.tv, minPos3));
                min = math.dot(minPos, tangent);
            }

            if (!float.IsNegativeInfinity(max))
            {
                var maxPos3 = math.transform(matrix, GetPoint(oldBasis, max));
                var maxPos = new float2(math.dot(newBasis.tu, maxPos3),
                    math.dot(newBasis.tv, maxPos3));
                max = math.dot(maxPos, tangent);
            }

            return new FaceCut(newNormal, math.dot(normal3, pos - newBasis.origin), math.min(min, max), math.max(min, max));
        }
    }

}
