using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace CsgTest
{
    public static class Helpers
    {
        public static float3 GetTangent(this float3 normal)
        {
            var absX = math.abs(normal.x);
            var absY = math.abs(normal.y);
            var absZ = math.abs(normal.z);

            return math.cross(normal, absX <= absY && absX <= absZ
                ? new float3(1f, 0f, 0f) : absY <= absZ
                    ? new float3(0f, 1f, 0f)
                    : new float3(0f, 0f, 1f));
        }

        public static float Cross(float2 a, float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        public static void EnsureCapacity<T>(ref T[] array, int minSize)
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
        
        public static (float3 origin, float3 tu, float3 tv) GetBasis(this BspPlane plane)
        {
            var tu = math.normalizesafe(plane.Normal.GetTangent());
            var tv = math.normalizesafe(math.cross(tu, plane.Normal));

            var origin = plane.Normal * plane.Offset;

            return (origin, tu, tv);
        }


        public static FaceCut GetFaceCut(BspPlane plane, BspPlane cutPlane, float3 origin, float3 tu, float3 tv)
        {
            var cutTangent = math.cross(plane.Normal, cutPlane.Normal);

            if (math.lengthsq(cutTangent) <= BspSolid.Epsilon * BspSolid.Epsilon)
            {
                // If this cut completely excludes the original plane, return a FaceCut that also excludes everything

                return plane.Offset * math.dot(plane.Normal, cutPlane.Normal) > cutPlane.Offset + BspSolid.Epsilon
                    ? FaceCut.ExcludeNone
                    : FaceCut.ExcludeAll;
            }

            var cutNormal = math.cross(cutTangent, plane.Normal);

            cutNormal = math.normalizesafe(cutNormal);

            var cutNormal2 = new float2(
                math.dot(cutNormal, tu),
                math.dot(cutNormal, tv));

            cutNormal2 = math.normalizesafe(cutNormal2);

            var t = math.dot(cutPlane.Normal * cutPlane.Offset - origin, cutPlane.Normal)
                    / math.dot(cutPlane.Normal, cutNormal);

            return new FaceCut(cutNormal2, t, float.NegativeInfinity, float.PositiveInfinity);
        }

        public static (bool ExcludesNone, bool ExcludesAll) GetNewFaceCutExclusions(this List<FaceCut> faceCuts, FaceCut cut)
        {
            if (cut.ExcludesAll)
            {
                return (false, true);
            }

            if (cut.ExcludesNone)
            {
                return (true, false);
            }

            var anyIntersections = false;
            var excludesAny = false;
            var excludedCutCount = 0;

            foreach (var other in faceCuts)
            {
                var cross = Helpers.Cross(cut.Normal, other.Normal);
                var dot = math.dot(cut.Normal, other.Normal);

                if (math.abs(cross) <= BspSolid.Epsilon)
                {
                    if (cut.Equals(other))
                    {
                        return (true, false);
                    }

                    if (cut.Equals(-other))
                    {
                        return (false, true);
                    }

                    if (other.Distance * dot < cut.Distance)
                    {
                        if (cut.Distance * dot < other.Distance)
                        {
                            return (false, true);
                        }

                        excludesAny = true;
                        ++excludedCutCount;
                    }

                    if (cut.Distance * dot < other.Distance)
                    {
                        return (true, false);
                    }

                    continue;
                }

                anyIntersections = true;

                var proj1 = (cut.Distance - other.Distance * dot) / -cross;

                if (cross > 0f && proj1 < other.Max)
                {
                    excludesAny = true;

                    if (proj1 < other.Min + BspSolid.Epsilon)
                    {
                        ++excludedCutCount;
                    }
                }
                else if (cross < 0f && proj1 > other.Min)
                {
                    excludesAny = true;

                    if (proj1 > other.Max - BspSolid.Epsilon)
                    {
                        ++excludedCutCount;
                    }
                }
            }

            return (anyIntersections && !excludesAny, anyIntersections && excludedCutCount == faceCuts.Count);
        }

        public static bool AddFaceCut(this List<FaceCut> faceCuts, FaceCut cut)
        {
            for (var i = faceCuts.Count - 1; i >= 0; --i)
            {
                var other = faceCuts[i];
                var cross = Helpers.Cross(cut.Normal, other.Normal);
                var dot = math.dot(cut.Normal, other.Normal);

                if (math.abs(cross) <= BspSolid.Epsilon)
                {
                    if (other.Distance * dot < cut.Distance)
                    {
                        if (cut.Distance * dot < other.Distance)
                        {
                            throw new Exception();
                        }

                        faceCuts.RemoveAt(i);
                        continue;
                    }

                    if (cut.Distance * dot < other.Distance)
                    {
                        cut.Min = float.PositiveInfinity;
                        cut.Max = float.NegativeInfinity;
                    }

                    continue;
                }

                var proj0 = (other.Distance - cut.Distance * dot) / cross;
                var proj1 = (cut.Distance - other.Distance * dot) / -cross;

                if (cross > 0f)
                {
                    cut.Min = math.max(cut.Min, proj0);
                    other.Max = math.min(other.Max, proj1);
                }
                else
                {
                    cut.Max = math.min(cut.Max, proj0);
                    other.Min = math.max(other.Min, proj1);
                }

                if (other.Min >= other.Max - BspSolid.Epsilon)
                {
                    faceCuts.RemoveAt(i);
                }
                else
                {
                    faceCuts[i] = other;
                }
            }

            if (cut.Min >= cut.Max - BspSolid.Epsilon)
            {
                return false;
            }

            faceCuts.Add(cut);
            return true;
        }
    }
}
