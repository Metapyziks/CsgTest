using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

namespace CsgTest.Geometry
{
    [RequireComponent(typeof(MeshFilter)), ExecuteInEditMode]
    public partial class CsgSolid : MonoBehaviour
    {
        private readonly List<CsgConvexSolid> _polyhedra = new List<CsgConvexSolid>();

        private bool _geometryInvalid;
        private int _geometryHash;

        public int DebugIndex;

        partial void RenderingStart();
        partial void RenderingUpdate();

        void Start()
        {
            _geometryInvalid = true;
            RenderingStart();
        }

        void OnValidate()
        {
#if UNITY_EDITOR
            if ( !UnityEditor.EditorApplication.isPlaying )
            {
                _geometryHash = 0;
                _geometryInvalid = true;
            }
#endif
        }

        void Update()
        {
#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying)
            {
                var hash = 0;

                foreach (var brush in transform.GetComponentsInChildren<CsgBrush>())
                {
                    unchecked
                    {
                        hash = (hash * 397) ^ brush.MaterialIndex;
                        hash = (hash * 397) ^ brush.Operator.GetHashCode();
                        hash = (hash * 397) ^ brush.Primitive.GetHashCode();
                        hash = (hash * 397) ^ brush.transform.position.GetHashCode();
                        hash = (hash * 397) ^ brush.transform.rotation.GetHashCode();
                        hash = (hash * 397) ^ brush.transform.lossyScale.GetHashCode();
                    }
                }

                _geometryInvalid |= hash != _geometryHash;
                _geometryHash = hash;
            }
#endif

            if (_geometryInvalid)
            {
                _geometryInvalid = false;
                _meshInvalid = true;

                CsgConvexSolid.NextIndex = 0;

                _polyhedra.Clear();

                var polys = new List<CsgConvexSolid>();

                foreach (var brush in transform.GetComponentsInChildren<CsgBrush>())
                {
                    polys.Clear();

                    switch (brush.Primitive)
                    {
                        case Primitive.Cube:
                            polys.Add(CsgConvexSolid.CreateCube(new Bounds(Vector3.zero, Vector3.one)));
                            break;

                        case Primitive.Dodecahedron:
                            polys.Add(CsgConvexSolid.CreateDodecahedron(Vector3.zero, 0.5f));
                            break;

                        case Primitive.Mesh:
                            // TODO
                            break;
                    }

                    var matrix = brush.transform.localToWorldMatrix;

                    foreach (var poly in polys)
                    {
                        poly.MaterialIndex = brush.MaterialIndex;
                        poly.Transform(matrix);
                    }

                    //SubdivideGridAxis(new float3(1f, 0f, 0f), polys);
                    //SubdivideGridAxis(new float3(0f, 0f, 1f), polys);
                    //SubdivideGridAxis(new float3(0f, 1f, 0f), polys);

                    foreach (var poly in polys)
                    {
                        Combine(poly, brush.Operator);
                    }
                }

                //GetConnectivityContainers(out var chunks, out var visited, out var queue);
                //FindChunks(chunks, visited, queue);
            }

            RenderingUpdate();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;

#if UNITY_EDITOR
            UnityEditor.Handles.matrix = transform.localToWorldMatrix;
#endif

            foreach ( var poly in _polyhedra )
            {
                poly.DrawGizmos( poly.Index == DebugIndex );
            }
        }
    }
}
