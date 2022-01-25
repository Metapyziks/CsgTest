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
        
        public float3 GetPoint(float3 origin, float3 tu, float3 tv)
        {
            var minFinite = !float.IsNegativeInfinity(Min);
            var maxFinite = !float.IsPositiveInfinity(Max);

            return GetPoint(origin, tu, tv, minFinite && maxFinite
                ? (Min + Max) * 0.5f
                : minFinite
                    ? Min + 1f
                    : maxFinite
                        ? Max - 1f
                        : 0f);
        }

        public float3 GetPoint(float3 origin, float3 tu, float3 tv, float along)
        {
            var pos = Normal * Distance + new float2(-Normal.y, Normal.x) * along;

            return origin + tu * pos.x + tv * pos.y;
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
    }

}
