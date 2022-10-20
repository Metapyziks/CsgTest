using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace CsgTest.Geometry
{
    partial class CsgConvexSolid
    {
        public partial struct Face : IEquatable<Face>
        {
            public CsgPlane Plane;
            public List<FaceCut> FaceCuts;
            public List<SubFace> SubFaces;

            public override string ToString()
            {
                return $"{{ Plane: {Plane}, FaceCuts: {FaceCuts?.Count} }}";
            }

            public Face Clone()
            {
                var copy = new Face
                {
                    Plane = Plane,
                    FaceCuts = new List<FaceCut>( FaceCuts ),
                    SubFaces = new List<SubFace>( SubFaces.Count )
                };

                foreach ( var subFace in SubFaces )
                {
                    copy.SubFaces.Add( subFace.Clone() );
                }

                return copy;
            }

            public Face CloneFlipped( CsgConvexSolid neighbor )
            {
                var copy = new Face
                {
                    Plane = -Plane,
                    FaceCuts = new List<FaceCut>( FaceCuts ),
                    SubFaces = new List<SubFace>( SubFaces.Count )
                };

                var thisHelper = Plane.GetHelper();
                var flipHelper = copy.Plane.GetHelper();

                copy.FaceCuts.Flip( thisHelper, flipHelper );

                foreach ( var subFace in SubFaces )
                {
                    var subFaceCopy = subFace.Clone();

                    subFaceCopy.FaceCuts.Flip( thisHelper, flipHelper );
                    subFaceCopy.Neighbor = neighbor;

                    copy.SubFaces.Add( subFaceCopy );
                }
                
                return copy;
            }

            public bool Equals( Face other )
            {
                return Plane.Equals(other.Plane);
            }

            public override bool Equals( object obj )
            {
                return obj is Face other && Equals(other);
            }

            public override int GetHashCode()
            {
                return Plane.GetHashCode();
            }
        }

        public partial struct SubFace
        {
            public List<FaceCut> FaceCuts;
            public CsgConvexSolid Neighbor;
            public int? MaterialIndex;

            public SubFace Clone()
            {
                return new SubFace
                {
                    FaceCuts = new List<FaceCut>( FaceCuts ),
                    Neighbor = Neighbor,
                    MaterialIndex = MaterialIndex
                };
            }
        }

        public partial struct FaceCut : IComparable<FaceCut>, IEquatable<FaceCut>
        {
            public static Comparison<FaceCut> Comparer { get; } = ( x, y ) => Math.Sign(x.Angle - y.Angle);

            public static FaceCut ExcludeAll => new FaceCut(new float2(-1f, 0f),
                float.PositiveInfinity, float.NegativeInfinity, float.PositiveInfinity);

            public static FaceCut ExcludeNone => new FaceCut(new float2(1f, 0f),
                float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity);

            public static FaceCut operator -( FaceCut cut )
            {
                return new FaceCut(-cut.Normal, -cut.Distance, -cut.Max, -cut.Min);
            }

            public static FaceCut operator +( FaceCut cut, float offset )
            {
                return new FaceCut(cut.Normal, cut.Distance + offset, float.NegativeInfinity, float.PositiveInfinity);
            }

            public static FaceCut operator -( FaceCut cut, float offset )
            {
                return new FaceCut(cut.Normal, cut.Distance - offset, float.NegativeInfinity, float.PositiveInfinity);
            }

            public readonly float2 Normal;
            public readonly float Angle;
            public readonly float Distance;

            public float Min;
            public float Max;

            public bool ExcludesAll => float.IsPositiveInfinity(Distance);
            public bool ExcludesNone => float.IsNegativeInfinity(Distance);

            public FaceCut( float2 normal, float distance, float min, float max ) => (Normal, Angle, Distance, Min, Max) = (normal, math.atan2(normal.y, normal.x), distance, min, max);
            
            public int CompareTo( FaceCut other )
            {
                return Angle.CompareTo(other.Angle);
            }

            public override string ToString()
            {
                return $"{{ Normal: {Normal}, Distance: {Distance} }}";
            }

            public bool Equals( FaceCut other )
            {
                return Normal.Equals(other.Normal) && Distance.Equals(other.Distance);
            }

            public override bool Equals( object obj )
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
}
