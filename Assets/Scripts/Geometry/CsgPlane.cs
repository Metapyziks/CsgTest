using System;
using System.Collections.Generic;
using Codice.CM.WorkspaceServer.DataStore.IncomingChanges;
using Unity.Mathematics;
using UnityEditor;

namespace CsgTest.Geometry
{
    public readonly struct CsgPlane : IEquatable<CsgPlane>
    {
        public static CsgPlane operator -( CsgPlane plane )
        {
            return new CsgPlane(-plane.Normal, -plane.Offset);
        }

        public static CsgPlane operator +( CsgPlane plane, float offset )
        {
            return new CsgPlane(plane.Normal, plane.Offset + offset);
        }

        public static CsgPlane operator -( CsgPlane plane, float offset )
        {
            return new CsgPlane(plane.Normal, plane.Offset - offset);
        }
        
        public readonly float3 Normal;
        public readonly float Offset;

        public CsgPlane( float3 normalDir, float3 position )
        {
            Normal = math.normalizesafe(normalDir);
            Offset = math.dot(Normal, position);
        }
        
        public CsgPlane( float3 normal, float offset )
        {
            Normal = normal;
            Offset = offset;
        }

        public Helper GetHelper()
        {
            return new Helper( this );
        }

        public CsgPlane Transform( in float4x4 matrix )
        {
            var basis = GetHelper();
            var position = math.transform(matrix, basis.Origin);
            var p1 = math.transform(matrix, basis.Origin + basis.Tu);
            var p2 = math.transform(matrix, basis.Origin + basis.Tv);

            return new CsgPlane(math.cross(p2 - position, p1 - position), position);
        }

        public int GetSign( float3 pos )
        {
            var dot = math.dot( pos, Normal ) - Offset;

            return dot > CsgHelpers.DistanceEpsilon ? 1 : dot < -CsgHelpers.DistanceEpsilon ? -1 : 0;
        }

        public bool Equals( CsgPlane other )
        {
            return Normal.Equals(other.Normal) && Offset.Equals(other.Offset);
        }

        public bool ApproxEquals( CsgPlane other )
        {
            return
                math.abs(1f - math.dot(Normal, other.Normal)) < CsgHelpers.UnitEpsilon &&
                math.abs(Offset - other.Offset) <= CsgHelpers.DistanceEpsilon;
        }
        
        public override bool Equals( object obj )
        {
            return obj is CsgPlane other && Equals(other);
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

        public readonly struct Helper
        {
            public readonly float3 Origin;
            public readonly float Offset;
            public readonly float3 Normal;
            public readonly float3 Tu;
            public readonly float3 Tv;

            public Helper( in CsgPlane plane )
            {
                Normal = plane.Normal;
                Offset = plane.Offset;

                Tu = math.normalizesafe(Normal.GetTangent());
                Tv = math.normalizesafe(math.cross(Tu, Normal));

                Origin = Normal * plane.Offset;
            }

            public CsgConvexSolid.FaceCut GetCut( CsgPlane cutPlane )
            {
                if (1f - math.abs(math.dot(Normal, cutPlane.Normal)) <= CsgHelpers.UnitEpsilon)
                {
                    // If this cut completely excludes the original plane, return a FaceCut that also excludes everything

                    var dot = math.dot(Normal, cutPlane.Normal);

                    return dot * Offset - cutPlane.Offset > CsgHelpers.DistanceEpsilon ? CsgConvexSolid.FaceCut.ExcludeNone : CsgConvexSolid.FaceCut.ExcludeAll;
                }

                var cutTangent = math.cross(Normal, cutPlane.Normal);
                var cutNormal = math.cross(cutTangent, Normal);

                cutNormal = math.normalizesafe(cutNormal);

                var cutNormal2 = new float2(
                    math.dot(cutNormal, Tu),
                    math.dot(cutNormal, Tv));

                cutNormal2 = math.normalizesafe(cutNormal2);

                var t = math.dot(cutPlane.Normal * cutPlane.Offset - Origin, cutPlane.Normal)
                        / math.dot(cutPlane.Normal, cutNormal);

                return new CsgConvexSolid.FaceCut(cutNormal2, t, float.NegativeInfinity, float.PositiveInfinity);
            }

            public float3 GetPoint( CsgConvexSolid.FaceCut cut )
            {
                return GetPoint( cut, cut.Mid );
            }

            public float3 GetPoint( CsgConvexSolid.FaceCut cut, float along )
            {
                var pos = cut.Normal * cut.Distance + new float2(-cut.Normal.y, cut.Normal.x) * math.clamp(along,
                    float.IsNegativeInfinity(cut.Min) ? -1024f : cut.Min,
                    float.IsPositiveInfinity(cut.Max) ? 1024f : cut.Max);

                return Origin + Tu * pos.x + Tv * pos.y;
            }

            public float3 GetAveragePos( List<CsgConvexSolid.FaceCut> faceCuts )
            {
                if ( faceCuts.Count == 0 )
                {
                    return Normal * Offset;
                }

                var avgPos = faceCuts.GetAveragePos();

                return Normal * Offset + Tu * avgPos.x + Tv * avgPos.y;
            }

            public CsgConvexSolid.FaceCut Transform( CsgConvexSolid.FaceCut cut, in Helper newHelper, float4x4? matrix = null )
            {
                if ( float.IsNegativeInfinity( cut.Min ) || float.IsPositiveInfinity( cut.Max ) )
                {
                    throw new NotImplementedException();
                }

                var oldTangent = Tu * -cut.Normal.y + Tv * cut.Normal.x;
                var newTangent = oldTangent;

                var minPos3 = GetPoint( cut, cut.Min );
                var maxPos3 = GetPoint( cut, cut.Max );

                if ( matrix is { } mat )
                {
                    newTangent = math.normalizesafe( math.mul( mat, new float4( oldTangent, 0f ) ).xyz );

                    minPos3 = math.transform( mat, minPos3 );
                    maxPos3 = math.transform( mat, maxPos3 );
                }

                var midPos3 = (minPos3 + maxPos3) * 0.5f;

                var normal = new float2(
                    math.dot( newHelper.Tv, newTangent ),
                    -math.dot( newHelper.Tu, newTangent ) );

                var midPos2 = new float2(
                    math.dot( newHelper.Tu, midPos3 ),
                    math.dot( newHelper.Tv, midPos3 ) );

                var min = math.dot( minPos3, newTangent );
                var max = math.dot( maxPos3, newTangent );

                return new CsgConvexSolid.FaceCut( normal, math.dot( normal, midPos2 ),
                    math.min( min, max ), math.max( min, max ) );
            }
        }
    }
}
