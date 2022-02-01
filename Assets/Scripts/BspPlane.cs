using System;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace CsgTest
{
    public readonly struct BspPlane : IEquatable<BspPlane>
    {
        public static BspPlane operator -(BspPlane plane)
        {
            return new BspPlane(-plane.Normal, -plane.Offset);
        }

        [JsonProperty("normal")]
        public readonly float3 Normal;

        [JsonProperty("offset")]
        public readonly float Offset;

        public BspPlane(float3 normalDir, float3 position)
        {
            Normal = math.normalizesafe(normalDir);
            Offset = math.dot(Normal, position);
        }

        [JsonConstructor]
        public BspPlane(float3 normal, float offset)
        {
            Normal = normal;
            Offset = offset;
        }

        public BspPlane Transform(in float4x4 matrix)
        {
            var basis = this.GetBasis();
            var position = math.transform(matrix, basis.origin);
            var p1 = math.transform(matrix, basis.origin + basis.tu);
            var p2 = math.transform(matrix, basis.origin + basis.tv);

            return new BspPlane(math.cross(p2 - position, p1 - position), position);
        }

        public bool Equals(BspPlane other)
        {
            return Normal.Equals(other.Normal) && math.abs(Offset - other.Offset) <= BspSolid.Epsilon;
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
