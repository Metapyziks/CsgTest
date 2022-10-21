using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

namespace CsgTest.Geometry
{
    partial class CsgConvexSolid
    {
        public void DrawGizmos( bool drawFaces )
        {
            for ( var i = 0 ; i < _faces.Count; ++i)
            {
                // if ( i != 0 ) continue;

                var face = _faces[i];

                face.DrawGizmos(VertexAverage, drawFaces);
            }

#if UNITY_EDITOR
            UnityEditor.Handles.Label(VertexAverage, ToString());
#endif
        }

        public void DrawDebug( Color color )
        {
            foreach (var face in _faces)
            {
                face.DrawDebug(color);
            }
        }

        public override string ToString()
        {
            return $"[{Index}]";
        }

        partial struct Face
        {
            private static void DrawFaceGizmos( List<FaceCut> faceCuts, in CsgPlane.Helper helper, float scale )
            {
                var totalLength = 0f;

                faceCuts.Sort( FaceCut.Comparer );

                foreach ( var cut in faceCuts )
                {
                    var min = helper.GetPoint( cut, cut.Min );
                    var max = helper.GetPoint( cut, cut.Max );

                    Gizmos.DrawLine( min, max );

                    totalLength += math.length( max - min );
                }

                scale *= Mathf.Sqrt( totalLength / 16f );
                
                var arrowCount = Mathf.Floor( totalLength / (16f * scale) );
                var arrowGap = totalLength / arrowCount;

                var t = (-1f + DateTime.UtcNow.Millisecond / 1000f) * arrowGap;

                foreach ( var cut in faceCuts )
                {
                    var min = helper.GetPoint( cut, cut.Min );
                    var max = helper.GetPoint( cut, cut.Max );
                    
                    var tangent = math.normalizesafe( max - min );
                    var normal = math.cross( tangent, helper.Normal );

                    var l = math.length( max - min );

                    t += l;

                    while ( t > 0f )
                    {
                        var mid = math.lerp( min, max, t / l );
                        var size = math.clamp( math.min( t, l - t ), 0f, scale );

                        Gizmos.DrawLine( mid + tangent * size, mid - normal * size );
                        Gizmos.DrawLine( mid, mid - normal * size );

                        t -= arrowGap;
                    }
                }
            }

            public void DrawGizmos( float3 vertexAverage, bool drawFaces )
            {
                const int debugZombie = 154;

                var basis = Plane.GetHelper();
                
                if ( drawFaces )
                {
                    Gizmos.color = Color.white;
                    DrawFaceGizmos( FaceCuts, basis, 1f );
                }

                foreach (var subFace in SubFaces)
                {
                    if (drawFaces)
                    {
                        Gizmos.color = new Color( 0f, 1f, 0f, 0.5f );
                        DrawFaceGizmos( subFace.FaceCuts, basis, 0.5f );
                    }
                }
            }

            public void DrawDebug( Color color )
            {
                var basis = Plane.GetHelper();

                foreach (var cut in FaceCuts)
                {
                    cut.DrawDebug(basis, color);
                }
            }
        }

        partial struct SubFace
        {
            public void DrawDebug( CsgPlane plane, Color color )
            {
                var basis = plane.GetHelper();

                foreach (var cut in FaceCuts)
                {
                    cut.DrawDebug(basis, color);
                }
            }
        }

        partial struct FaceCut
        {
            public void DrawDebug( CsgPlane plane, Color color )
            {
                DrawDebug(plane.GetHelper(), color);
            }

            public void DrawDebug( in CsgPlane.Helper basis, Color color )
            {
                var min = basis.GetPoint(this, Min);
                var max = basis.GetPoint(this, Max);

                var mid = (min + max) * 0.5f;
                var norm = Normal.x * basis.Tu + Normal.y * basis.Tv;

                Debug.DrawLine(min, max, color);
                Debug.DrawLine(mid, mid + norm, color);
            }
        }
    }
}
