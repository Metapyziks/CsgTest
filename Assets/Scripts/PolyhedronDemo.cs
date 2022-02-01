using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

namespace CsgTest
{
    [RequireComponent(typeof(MeshFilter)), ExecuteInEditMode]
    public class PolyhedronDemo : MonoBehaviour
    {
        private readonly List<ConvexPolyhedron> _polyhedra = new List<ConvexPolyhedron>();

        private bool _geometryInvalid;
        private bool _meshInvalid;

        private Vector3[] _vertices;
        private Vector3[] _normals;
        private ushort[] _indices;
        private Mesh _mesh;

        void Start()
        {
            _geometryInvalid = true;
        }

        void Update()
        {
#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying)
            {
                _geometryInvalid = true;
            }
#endif

            if (_geometryInvalid)
            {
                _geometryInvalid = false;

                ConvexPolyhedron.NextIndex = 0;

                _polyhedra.Clear();

                foreach (var brush in transform.GetComponentsInChildren<CsgBrush>())
                {
                    var shape = brush.Primitive == Primitive.Cube
                        ? ConvexPolyhedron.CreateCube(new Bounds(Vector3.zero, Vector3.one))
                        : ConvexPolyhedron.CreateDodecahedron(Vector3.zero, 0.5f);
                    var matrix = brush.transform.localToWorldMatrix;
                    var normalMatrix = math.transpose(math.inverse(matrix));

                    shape.Transform(matrix, normalMatrix);

                    switch (brush.Operator)
                    {
                        case BrushOperator.Add:
                            Add(shape);
                            break;

                        case BrushOperator.Subtract:
                            Subtract(shape);
                            break;
                    }
                }
            }

            if (_meshInvalid)
            {
                _meshInvalid = false;

                if (_mesh == null)
                {
                    var meshFilter = GetComponent<MeshFilter>();

                    meshFilter.sharedMesh = _mesh = new Mesh
                    {
                        hideFlags = HideFlags.DontSave
                    };

                    _mesh.MarkDynamic();
                }

                var indexOffset = 0;
                var vertexOffset = 0;

                foreach (var poly in _polyhedra)
                {
                    var (faceCount, vertexCount) = poly.GetMeshInfo();

                    Helpers.EnsureCapacity(ref _vertices, vertexOffset + vertexCount);
                    Helpers.EnsureCapacity(ref _normals, vertexOffset + vertexCount);
                    Helpers.EnsureCapacity(ref _indices, indexOffset + faceCount * 3);

                    poly.WriteMesh(ref vertexOffset, ref indexOffset, _vertices, _normals, _indices);
                }

                _mesh.Clear();
                _mesh.SetVertices(_vertices, 0, vertexOffset);
                _mesh.SetNormals(_normals, 0, vertexOffset);
                _mesh.SetIndices(_indices, 0, indexOffset, MeshTopology.Triangles, 0);

                _mesh.MarkModified();

                var meshCollider = GetComponent<MeshCollider>();

                if (meshCollider != null)
                {
                    meshCollider.sharedMesh = _mesh;
                }
            }
        }

        public bool Add(ConvexPolyhedron polyhedron)
        {
            if (polyhedron.IsEmpty) return false;

            Subtract(polyhedron);
            _polyhedra.Add(polyhedron);

            _meshInvalid = true;
            return true;
        }

        private readonly HashSet<ConvexFace> _excludedFaces = new HashSet<ConvexFace>();

        public bool Subtract(ConvexPolyhedron polyhedron)
        {
            if (polyhedron.IsEmpty) return false;

            var changed = false;

            for (var polyIndex = _polyhedra.Count - 1; polyIndex >= 0; --polyIndex)
            {
                var next = _polyhedra[polyIndex];
                var allInside = true;
                var skippedCount = 0;

                for (var faceIndex = 0; faceIndex < polyhedron.FaceCount; ++faceIndex)
                {
                    var face = polyhedron.GetFace(faceIndex);

                    _excludedFaces.Clear();
                    var (excludedNone, excludedAll) = next.Clip(face.Plane, face.FaceCuts, null, _excludedFaces, dryRun: true);

                    if (excludedAll && excludedNone)
                    {
                        ++skippedCount;
                    }

                    if (excludedNone) continue;

                    if (excludedAll)
                    {
                        allInside = false;
                        break;
                    }

                    var child = new ConvexPolyhedron();

                    child.CopyFaces(_excludedFaces);

                    next.Clip(face.Plane, null, child);
                    child.Clip(-face.Plane, null, next);

                    if (!child.IsEmpty)
                    {
                        _polyhedra.Add(child);
                    }
                    else
                    {
                        next.ReplaceNeighbor(face.Plane, child, null);
                    }

                    changed = true;
                }

                if (allInside && skippedCount != polyhedron.FaceCount)
                {
                    next.Removed();
                    _polyhedra.Remove(next);
                    changed = true;
                }
            }

            _meshInvalid |= changed;
            return changed;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;

            foreach (var poly in _polyhedra)
            {
                poly.DrawGizmos();
            }
        }
    }
}
