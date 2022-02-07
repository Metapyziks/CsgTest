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

        private readonly HashSet<ConvexPolyhedron> _visited = new HashSet<ConvexPolyhedron>();
        private readonly Queue<ConvexPolyhedron> _visitQueue = new Queue<ConvexPolyhedron>();

        private bool _geometryInvalid;
        private bool _meshInvalid;

        private Vector3[] _vertices;
        private Vector3[] _normals;
        private Vector4[] _texCoords;
        private ushort[] _indices;
        private Mesh _mesh;

        private bool _hasRigidBody;

        public Transform Anchor;

        void Start()
        {
            _hasRigidBody = GetComponent<Rigidbody>() != null;
            _geometryInvalid = _polyhedra.Count == 0;
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

#if UNITY_EDITOR
                if (UnityEditor.EditorApplication.isPlaying)
#endif
                {
                    if (_polyhedra.Count == 0)
                    {
                        Destroy(gameObject);
                        return;
                    }

                    RemoveDisconnectedPolyhedra();
                }

                if (_mesh == null)
                {
                    var meshFilter = GetComponent<MeshFilter>();

                    meshFilter.sharedMesh = _mesh = new Mesh
                    {
                        hideFlags = HideFlags.DontSave
                    };

                    _mesh.MarkDynamic();
                }

                UpdateMesh(_mesh, _polyhedra);

                var meshCollider = GetComponent<MeshCollider>();

                if (meshCollider != null)
                {
                    meshCollider.sharedMesh = _mesh;
                }
            }
        }

        private void WritePolyhedron(ref int vertexOffset, ref int indexOffset, ConvexPolyhedron poly)
        {
            var (faceCount, vertexCount) = poly.GetMeshInfo();

            Helpers.EnsureCapacity(ref _vertices, vertexOffset + vertexCount);
            Helpers.EnsureCapacity(ref _normals, vertexOffset + vertexCount);
            Helpers.EnsureCapacity(ref _texCoords, vertexOffset + vertexCount);
            Helpers.EnsureCapacity(ref _indices, indexOffset + faceCount * 3);

            poly.WriteMesh(ref vertexOffset, ref indexOffset,
                _vertices, _normals, _texCoords, _indices);

            if (_hasRigidBody && poly.ColliderInvalid)
            {
                poly.UpdateCollider(this);
            }
        }

        private void UpdateMesh<T>(Mesh mesh, T polyhedra)
            where T : IEnumerable<ConvexPolyhedron>
        {
            var indexOffset = 0;
            var vertexOffset = 0;

            var mass = 0f;

            // TODO: can have different densities for different materials
            const float density = 1f;

            foreach (var poly in polyhedra)
            {
                WritePolyhedron(ref vertexOffset, ref indexOffset, poly);

                mass += poly.Volume * density;
            }

            mesh.Clear();
            mesh.SetVertices(_vertices, 0, vertexOffset);
            mesh.SetNormals(_normals, 0, vertexOffset);
            mesh.SetUVs(0, _texCoords, 0, vertexOffset);
            mesh.SetIndices(_indices, 0, indexOffset, MeshTopology.Triangles, 0);

            mesh.MarkModified();

            if (_hasRigidBody)
            {
                GetComponent<Rigidbody>().mass = mass;
            }
        }

        private void RemoveDisconnectedPolyhedra()
        {
            if (_polyhedra.Count == 0) return;

            _visitQueue.Clear();
            _visited.Clear();

            if (Anchor != null)
            {
                var anchorPos = (float3) Anchor.position;
                var match = _polyhedra.FirstOrDefault(x => x.Contains(anchorPos));

                if (match == null)
                {
                    return;
                }

                _visited.Add(match);
                _visitQueue.Enqueue(match);
            }
            else
            {
                _visited.Add(_polyhedra[0]);
                _visitQueue.Enqueue(_polyhedra[0]);
            }

            while (_visitQueue.Count > 0)
            {
                _visitQueue.Dequeue().AddNeighbors(_visited, _visitQueue);
            }

            if (_visited.Count == _polyhedra.Count) return;

            var disconnected = new List<ConvexPolyhedron>();

            for (var i = _polyhedra.Count - 1; i >= 0; --i)
            {
                var poly = _polyhedra[i];

                if (_visited.Contains(poly)) continue;

                disconnected.Add(poly);
                _polyhedra.RemoveAt(i);
            }

            while (disconnected.Count > 0)
            {
                _visited.Clear();
                _visitQueue.Clear();

                var last = disconnected[disconnected.Count - 1];

                _visitQueue.Enqueue(last);
                _visited.Add(last);

                while (_visitQueue.Count > 0)
                {
                    var next = _visitQueue.Dequeue();
                    disconnected.Remove(next);

                    next.InvalidateMesh();
                    next.AddNeighbors(_visited, _visitQueue);
                }

                var child = new GameObject("Debris", typeof(Rigidbody), typeof(MeshFilter), typeof(MeshRenderer),
                    typeof(PolyhedronDemo))
                {
                    transform =
                    {
                        localPosition = transform.localPosition,
                        localRotation = transform.localRotation,
                        localScale = transform.localScale
                    }
                }.GetComponent<PolyhedronDemo>();

                child.GetComponent<MeshRenderer>().sharedMaterial = GetComponent<MeshRenderer>().sharedMaterial;

                child._polyhedra.AddRange(_visited);
                child._meshInvalid = true;

                child.Start();
                child.Update();
            }
        }

        private readonly HashSet<ConvexFace> _excludedFaces = new HashSet<ConvexFace>();

        private readonly List<ConvexPolyhedron> _intersections =
            new List<ConvexPolyhedron>();

        public bool Combine(ConvexPolyhedron polyhedron, BrushOperator op)
        {
            if (polyhedron.IsEmpty) return false;

            var changed = false;
            var maybeOutside = false;

            var min = polyhedron.VertexMin - BspSolid.Epsilon;
            var max = polyhedron.VertexMax + BspSolid.Epsilon;

            _intersections.Clear();

            for (var polyIndex = _polyhedra.Count - 1; polyIndex >= 0; --polyIndex)
            {
                var next = _polyhedra[polyIndex];

                var nextMin = next.VertexMin;
                var nextMax = next.VertexMax;

                if (nextMin.x > max.x || nextMin.y > max.y || nextMin.z > max.z) continue;
                if (nextMax.x < min.x || nextMax.y < min.y || nextMax.z < min.z) continue;

                if (op == BrushOperator.Replace && next.MaterialIndex == polyhedron.MaterialIndex)
                {
                    continue;
                }

                if (op == BrushOperator.Paint)
                {
                    next.PaintMaterial(polyhedron);
                    continue;
                }

                var allInside = true;

                for (var faceIndex = 0; faceIndex < polyhedron.FaceCount; ++faceIndex)
                {
                    var face = polyhedron.GetFace(faceIndex);

                    _excludedFaces.Clear();
                    var (excludedNone, excludedAll) = next.Clip(face.Plane,
                        face.FaceCuts, null, _excludedFaces, dryRun: true);

                    if (excludedNone)
                    {
                        maybeOutside |= excludedAll;
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
                        Debug.LogError("Empty child!");
                        child.Removed(null);
                    }

                    changed = true;
                }

                if (allInside && maybeOutside)
                {
                    for (var faceIndex = 0; faceIndex < polyhedron.FaceCount; ++faceIndex)
                    {
                        var face = polyhedron.GetFace(faceIndex);

                        if (math.dot(face.Plane.Normal, next.VertexAverage) <= face.Plane.Offset)
                        {
                            allInside = false;
                            break;
                        }
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

#if UNITY_EDITOR
            UnityEditor.Handles.matrix = transform.localToWorldMatrix;
#endif

            foreach (var poly in _polyhedra)
            {
                poly.DrawGizmos();
            }
        }
    }
}
