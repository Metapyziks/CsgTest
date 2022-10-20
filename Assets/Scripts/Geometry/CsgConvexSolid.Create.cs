using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace CsgTest.Geometry
{
    partial class CsgConvexSolid
    {
        public static CsgConvexSolid CreateCube( Bounds bounds )
        {
            var mesh = new CsgConvexSolid();

            mesh.Clip(new CsgPlane(new float3(1f, 0f, 0f), bounds.min));
            mesh.Clip(new CsgPlane(new float3(-1f, 0f, 0f), bounds.max));
            mesh.Clip(new CsgPlane(new float3(0f, 1f, 0f), bounds.min));
            mesh.Clip(new CsgPlane(new float3(0f, -1f, 0f), bounds.max));
            mesh.Clip(new CsgPlane(new float3(0f, 0f, 1f), bounds.min));
            mesh.Clip(new CsgPlane(new float3(0f, 0f, -1f), bounds.max));

            return mesh;
        }

        private static float3 DistortNormal( float3 normal, ref Random random, float distortion )
        {
            if (distortion <= 0f) return normal;

            normal += random.NextFloat3Direction() * distortion;

            return math.normalizesafe(normal);
        }

        public static CsgConvexSolid CreateDodecahedron( float3 center, float radius )
        {
            Random random = default;
            return CreateDodecahedron(center, radius, ref random, 0f);
        }

        public static CsgConvexSolid CreateDodecahedron( float3 center, float radius, ref Random random, float distortion )
        {
            distortion = math.clamp(distortion, 0f, 1f) * 0.25f;

            var mesh = new CsgConvexSolid();

            mesh.Clip(new CsgPlane(DistortNormal(new float3(0f, 1f, 0f), ref random, distortion), -radius));
            mesh.Clip(new CsgPlane(DistortNormal(new float3(0f, -1f, 0f), ref random, distortion), -radius));

            var rot = Quaternion.AngleAxis(60f, Vector3.right);

            for (var i = 0; i < 5; ++i)
            {
                mesh.Clip(new CsgPlane(DistortNormal(rot * Vector3.down, ref random, distortion), -radius));
                mesh.Clip(new CsgPlane(DistortNormal(rot * Vector3.up, ref random, distortion), -radius));

                rot = Quaternion.AngleAxis(72f, Vector3.up) * rot;
            }

            mesh.Transform(float4x4.Translate(center));

            return mesh;
        }
    }
}
