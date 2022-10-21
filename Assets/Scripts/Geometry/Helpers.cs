using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

namespace CsgTest.Geometry
{
    internal static class CsgHelpers
    {
        public const float UnitEpsilon = 9.5367431640625E-7f; // 0x35800000
        public const float DistanceEpsilon = 9.765625E-4f; // 0x3a800000

        public static float3 GetTangent( this float3 normal )
        {
            var absX = math.abs(normal.x);
            var absY = math.abs(normal.y);
            var absZ = math.abs(normal.z);

            return math.cross(normal, absX <= absY && absX <= absZ
                ? new float3(1f, 0f, 0f) : absY <= absZ
                    ? new float3(0f, 1f, 0f)
                    : new float3(0f, 0f, 1f));
        }

        public static void EnsureCapacity<T>( ref T[] array, int minSize )
            where T : struct
        {
            if (array != null && array.Length >= minSize) return;

            var oldArray = array;

            array = new T[Mathf.NextPowerOfTwo(minSize)];

            if (oldArray != null)
            {
                Array.Copy(oldArray, 0, array, 0, oldArray.Length);
            }
        }

        public static float Cross( float2 a, float2 b )
        {
            return a.x * b.y - a.y * b.x;
        }
        public static void Flip( this List<CsgConvexSolid.FaceCut> faceCuts, CsgPlane plane )
        {
            Flip( faceCuts, plane.GetHelper(), (-plane).GetHelper() );
        }

        public static void Flip( this List<CsgConvexSolid.FaceCut> faceCuts,
            in CsgPlane.Helper oldHelper, in CsgPlane.Helper newHelper )
        {
            for ( var i = 0; i < faceCuts.Count; i++ )
            {
                faceCuts[i] = -oldHelper.Transform( faceCuts[i], newHelper );
            }
        }

        public static void Split( this List<CsgConvexSolid.FaceCut> faceCuts, CsgConvexSolid.FaceCut splitCut,
            List<CsgConvexSolid.FaceCut> outPositive, List<CsgConvexSolid.FaceCut> outNegative = null )
        {
            outPositive.Clear();
            outNegative?.Clear();

            if ( splitCut.ExcludesNone )
            {
                outPositive.AddRange( faceCuts );
                return;
            }

            if ( splitCut.ExcludesAll )
            {
                outNegative?.AddRange( faceCuts );
                return;
            }

            foreach ( var faceCut in faceCuts )
            {
                var cross = Cross(splitCut.Normal, faceCut.Normal);
                var dot = math.dot(splitCut.Normal, faceCut.Normal);

                if (math.abs(cross) <= UnitEpsilon)
                {
                    // Edge case: parallel cuts

                    if (faceCut.Distance * dot - splitCut.Distance < DistanceEpsilon)
                    {
                        // splitCut is pointing away from faceCut,
                        // so faceCut is negative

                        if (dot < 0f && splitCut.Distance * dot - faceCut.Distance < DistanceEpsilon)
                        {
                            // faceCut is also pointing away from splitCut,
                            // so the whole face must be negative

                            outPositive.Clear();
                            outNegative?.Clear();
                            outNegative?.AddRange( faceCuts );
                            return;
                        }

                        outNegative?.Add( faceCut );
                        continue;
                    }

                    if (splitCut.Distance * dot - faceCut.Distance < DistanceEpsilon)
                    {
                        // faceCut is pointing away from splitCut,
                        // so splitCut is redundant

                        outPositive.Clear();
                        outNegative?.Clear();
                        outPositive.AddRange( faceCuts );
                        return;
                    }

                    // Otherwise the two cuts are pointing towards each other

                    outPositive.Add( faceCut );
                    continue;
                }

                // Not parallel, so check for intersection

                var proj0 = (faceCut.Distance - splitCut.Distance * dot) / cross;
                var proj1 = (splitCut.Distance - faceCut.Distance * dot) / -cross;

                var posFaceCut = faceCut;
                var negFaceCut = faceCut;

                if (cross > 0f)
                {
                    splitCut.Min = math.max(splitCut.Min, proj0);
                    posFaceCut.Max = math.min(faceCut.Max, proj1);
                    negFaceCut.Min = math.max(faceCut.Min, proj1);
                }
                else
                {
                    splitCut.Max = math.min(splitCut.Max, proj0);
                    posFaceCut.Min = math.max(faceCut.Min, proj1);
                    negFaceCut.Max = math.min(faceCut.Max, proj1);
                }

                if (splitCut.Max - splitCut.Min < DistanceEpsilon)
                {
                    // splitCut must be fully outside the face

                    if ( posFaceCut.Max - posFaceCut.Min >= DistanceEpsilon )
                    {
                        outPositive.Clear();
                        outNegative?.Clear();
                        outPositive.AddRange( faceCuts );
                        return;
                    }

                    if ( negFaceCut.Max - negFaceCut.Min >= DistanceEpsilon )
                    {
                        outPositive.Clear();
                        outNegative?.Clear();
                        outNegative?.AddRange( faceCuts );
                        return;
                    }

                    Assert.IsTrue( false );
                }

                if (posFaceCut.Max - posFaceCut.Min >= DistanceEpsilon)
                {
                    outPositive.Add( posFaceCut );
                }

                if (negFaceCut.Max - negFaceCut.Min >= DistanceEpsilon)
                {
                    outNegative?.Add(negFaceCut);
                }
            }

            outPositive.Add( splitCut );
            outNegative?.Add( -splitCut );
        }
    }
}
