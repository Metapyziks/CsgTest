﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;

namespace CsgTest.Geometry
{
    partial class CsgSolid
    {
        public bool Combine( CsgConvexSolid solid, BrushOperator op )
        {
            var changed = false;
            
            if ( solid.IsEmpty ) return false;

            var faces = solid.Faces;

            var min = solid.VertexMin - CsgHelpers.DistanceEpsilon;
            var max = solid.VertexMax + CsgHelpers.DistanceEpsilon;
            
            for ( var polyIndex = _polyhedra.Count - 1; polyIndex >= 0; --polyIndex )
            {
                var next = _polyhedra[polyIndex];

                if ( next.IsEmpty )
                {
                    _polyhedra.RemoveAt( polyIndex );
                    continue;
                }

                var nextMin = next.VertexMin;
                var nextMax = next.VertexMax;

                if ( nextMin.x > max.x || nextMin.y > max.y || nextMin.z > max.z ) continue;
                if ( nextMax.x < min.x || nextMax.y < min.y || nextMax.z < min.z ) continue;

                var skip = false;

                switch ( op )
                {
                    case BrushOperator.Replace:
                        next.Paint( solid, null );
                        skip = next.MaterialIndex == solid.MaterialIndex;
                        break;

                    case BrushOperator.Paint:
                        next.Paint( solid, solid.MaterialIndex );
                        skip = true;
                        break;

                    case BrushOperator.Add:
                        for ( var faceIndex = 0; faceIndex < faces.Count; ++faceIndex )
                        {
                            var solidFace = faces[faceIndex];

                            if ( !next.TryGetFace( -solidFace.Plane, out var nextFace ) )
                            {
                                continue;
                            }

                            skip = true;

                            if ( ConnectFaces( solidFace, solid, nextFace, next ) )
                            {
                                changed = true;
                            }

                            break;
                        }
                        break;
                }

                if ( skip ) continue;

                for ( var faceIndex = 0; faceIndex < faces.Count && !next.IsEmpty; ++faceIndex )
                {
                    var face = faces[faceIndex];
                    var child = next.Split( face.Plane, face.FaceCuts );

                    if ( child == null )
                    {
                        continue;
                    }

                    changed = true;

                    if ( child.Faces.Count < 4 )
                    {
                        child.Remove( null );
                    }
                    else
                    {
                        _polyhedra.Add( child );
                    }

                    if ( next.Faces.Count < 4 )
                    {
                        next.Remove( null );
                    }
                }

                if ( !next.IsEmpty && solid.GetSign( next.VertexAverage ) < 0 ) continue;

                // next will now contain only the intersection with solid.
                // We'll copy its faces and remove it

                switch ( op )
                {
                    case BrushOperator.Replace:
                        next.MaterialIndex = solid.MaterialIndex;
                        break;

                    case BrushOperator.Add:
                        _polyhedra.RemoveAt( polyIndex );

                        solid.MergeSubFacesFrom( next );
                        next.Remove( null );
                        break;

                    case BrushOperator.Subtract:
                        _polyhedra.RemoveAt( polyIndex );

                        next.Remove( null );
                        break;
                }
            }

            switch ( op )
            {
                case BrushOperator.Add:
                    _polyhedra.Add( solid );
                    break;
            }

            _meshInvalid |= changed;

            return changed;
        }

        private static bool ConnectFaces( CsgConvexSolid.Face faceA, CsgConvexSolid solidA, CsgConvexSolid.Face faceB, CsgConvexSolid solidB )
        {
            var intersectionCuts = CsgHelpers.RentFaceCutList();

            var faceAHelper = faceA.Plane.GetHelper();
            var faceBHelper = faceB.Plane.GetHelper();
            
            try
            {
                intersectionCuts.AddRange( faceA.FaceCuts );

                foreach ( var faceCut in faceB.FaceCuts )
                {
                    intersectionCuts.Split( -faceBHelper.Transform( faceCut, faceAHelper ) );
                }

                if ( intersectionCuts.IsDegenerate() || solidB.GetSign( faceAHelper.GetAveragePos( intersectionCuts ) ) < 0 )
                {
                    return false;
                }

                faceA.RemoveSubFacesInside( intersectionCuts );
                faceA.SubFaces.Add( new CsgConvexSolid.SubFace
                {
                    FaceCuts = new List<CsgConvexSolid.FaceCut>( intersectionCuts ),
                    Neighbor = solidB
                } );

                for ( var i = 0; i < intersectionCuts.Count; i++ )
                {
                    intersectionCuts[i] = -faceAHelper.Transform( intersectionCuts[i], faceBHelper );
                }

                faceB.RemoveSubFacesInside( intersectionCuts );
                faceB.SubFaces.Add( new CsgConvexSolid.SubFace
                {
                    FaceCuts = new List<CsgConvexSolid.FaceCut>( intersectionCuts ),
                    Neighbor = solidA
                } );

                return true;
            }
            finally
            {
                CsgHelpers.ReturnFaceCutList( intersectionCuts );
            }
        }
    }
}
