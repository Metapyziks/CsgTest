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
        public void DrawGizmos( bool drawFaces, bool isZombie = false )
        {
            foreach (var face in _faces)
            {
                face.DrawGizmos(VertexAverage, drawFaces, isZombie);
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
            public void DrawGizmos( float3 vertexAverage, bool drawFaces, bool isZombie )
            {
                const int debugZombie = 154;

                var basis = Plane.GetHelper();

                foreach (var subFace in SubFaces)
                {
                    if (drawFaces)
                    {
                        Gizmos.color = isZombie || subFace.Neighbor?.Index == debugZombie ? Color.red : Color.white;

                        foreach (var cut in subFace.FaceCuts)
                        {
                            var min = basis.GetPoint(cut, cut.Min);
                            var max = basis.GetPoint(cut, cut.Max);

                            Gizmos.DrawLine(min, max);

                            var mid = (min + max) * 0.5f;

                            Gizmos.DrawLine( mid, mid - Plane.Normal );
                        }
                    }

                    if (!isZombie && subFace.Neighbor != null)
                    {
                        Gizmos.color = isZombie ? Color.yellow : Color.green;
                        Gizmos.DrawLine(vertexAverage, subFace.Neighbor.VertexAverage);

                        if (subFace.Neighbor.Index == debugZombie)
                        {
                            subFace.Neighbor.DrawGizmos(true, true);
                        }
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
