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
        private Vector4[] _texCoords;
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
                _meshInvalid = true;

                ConvexPolyhedron.NextIndex = 0;

                _polyhedra.Clear();

                foreach (var brush in transform.GetComponentsInChildren<CsgBrush>())
                {
                    var shape = brush.Primitive == Primitive.Cube
                        ? ConvexPolyhedron.CreateCube(new Bounds(Vector3.zero, Vector3.one))
                        : ConvexPolyhedron.CreateDodecahedron(Vector3.zero, 0.5f);
                    var matrix = brush.transform.localToWorldMatrix;

                    shape.MaterialIndex = brush.MaterialIndex;
                    shape.Transform(matrix);

                    Combine(shape, brush.Operator);
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
                    Helpers.EnsureCapacity(ref _texCoords, vertexOffset + vertexCount);
                    Helpers.EnsureCapacity(ref _indices, indexOffset + faceCount * 3);

                    poly.WriteMesh(ref vertexOffset, ref indexOffset,
                        _vertices, _normals, _texCoords, _indices);
                }

                _mesh.Clear();
                _mesh.SetVertices(_vertices, 0, vertexOffset);
                _mesh.SetNormals(_normals, 0, vertexOffset);
                _mesh.SetUVs(0, _texCoords, 0, vertexOffset);
                _mesh.SetIndices(_indices, 0, indexOffset, MeshTopology.Triangles, 0);

                _mesh.MarkModified();

                var meshCollider = GetComponent<MeshCollider>();

                if (meshCollider != null)
                {
                    meshCollider.sharedMesh = _mesh;
                }
            }
        }

        private readonly HashSet<ConvexFace> _excludedFaces = new HashSet<ConvexFace>();

        private readonly List<ConvexPolyhedron> _intersections =
            new List<ConvexPolyhedron>();

        public bool Combine(ConvexPolyhedron polyhedron, BrushOperator op)
        {
            if (polyhedron.IsEmpty) return false;

            var changed = false;

            _intersections.Clear();

            for (var polyIndex = _polyhedra.Count - 1; polyIndex >= 0; --polyIndex)
            {
                var next = _polyhedra[polyIndex];

                var allInside = true;

                for (var faceIndex = 0; faceIndex < polyhedron.FaceCount; ++faceIndex)
                {
                    var face = polyhedron.GetFace(faceIndex);

                    _excludedFaces.Clear();
                    var (excludedNone, excludedAll) = next.Clip(face.Plane,
                        face.FaceCuts, null, _excludedFaces, dryRun: true);

                    if (excludedNone)
                    {
                        continue;
                    }

                    if (excludedAll)
                    {
                        allInside = false;
                        break;
                    }

                    var child = new ConvexPolyhedron
                    {
                        MaterialIndex = next.MaterialIndex
                    };

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

                if (!allInside) continue;

                for (var faceIndex = 0; faceIndex < polyhedron.FaceCount; ++faceIndex)
                {
                    var face = polyhedron.GetFace(faceIndex);

                    if (math.dot(face.Plane.Normal, next.VertexAverage) <= face.Plane.Offset)
                    {
                        allInside = false;
                        break;
                    }
                }

                if (!allInside) continue;

                switch (op)
                {
                    case BrushOperator.Replace:
                        next.MaterialIndex = polyhedron.MaterialIndex;
                        break;

                    case BrushOperator.Add:
                        _intersections.Add(next);
                        _polyhedra.RemoveAt(polyIndex);
                        break;

                    case BrushOperator.Subtract:
                        next.Removed(null);
                        _polyhedra.RemoveAt(polyIndex);
                        break;
                }

                changed = true;
            }

            if (op == BrushOperator.Add)
            {
                polyhedron = polyhedron.Clone();
                _polyhedra.Add(polyhedron);

                foreach (var intersection in _intersections)
                {
                    polyhedron.CopySubFaces(intersection);
                    intersection.Removed(polyhedron);
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
