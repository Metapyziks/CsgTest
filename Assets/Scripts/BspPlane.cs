using System;
using Unity.Mathematics;

namespace CsgTest
{
    public readonly struct BspPlane : IEquatable<BspPlane>
    {
        public static BspPlane operator -(BspPlane plane)
        {
            return new BspPlane(-plane.Normal, -plane.Offset);
        }

        public readonly float3 Normal;
        public readonly float Offset;

        public BspPlane(float3 normalDir, float3 position)
        {
            Normal = math.normalizesafe(normalDir);
            Offset = math.dot(Normal, position);
        }

        public BspPlane(float3 normal, float offset)
        {
            Normal = normal;
            Offset = offset;
        }

        public BspPlane Transform(in float4x4 matrix, in float4x4 transInvMatrix)
        {
            var position = math.transform(matrix, Normal * Offset);
            var normal = math.mul(transInvMatrix, new float4(Normal, 0f)).xyz;
            return new BspPlane(normal, position);
        }

        public bool Equals(BspPlane other)
        {
            return Normal.Equals(other.Normal) && Offset.Equals(other.Offset);
        }

        public override bool Equals(object obj)
        {
            return obj is BspPlane other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Normal.GetHashCode() * 397) ^ Offset.GetHashCode();
            }
        }

        public override string ToString()
        {
            return $"{{ Normal: {Normal}, Offset: {Offset} }}";
        }
    }
}
