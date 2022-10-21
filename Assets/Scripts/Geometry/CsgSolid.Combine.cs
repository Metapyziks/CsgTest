using System;
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
            if (solid.IsEmpty) return false;

            if ( op == BrushOperator.Paint )
            {
                // TODO
                return false;
            }

            var min = solid.VertexMin - CsgHelpers.DistanceEpsilon;
            var max = solid.VertexMax + CsgHelpers.DistanceEpsilon;

            var changed = false;
            
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
                        skip = next.MaterialIndex == solid.MaterialIndex;
                        break;
                }

                if ( skip ) continue;

                var faces = solid.Faces;

                for ( var faceIndex = 0; faceIndex < faces.Count && !next.IsEmpty; ++faceIndex )
                {
                    var face = faces[faceIndex];
                    var child = next.Split( face.Plane, face.FaceCuts );

                    if ( child == null )
                    {
                        continue;
                    }

                    changed = true;

                    _polyhedra.Add( child );
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
    }
}
