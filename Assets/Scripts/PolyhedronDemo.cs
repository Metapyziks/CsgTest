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

        private bool _hasRigidBody;

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
                    ConvexPolyhedron[] polys = Array.Empty<ConvexPolyhedron>();

                    switch (brush.Primitive)
                    {
                        case Primitive.Cube:
                            polys = new[] { ConvexPolyhedron.CreateCube(new Bounds(Vector3.zero, Vector3.one)) };
                            break;

                        case Primitive.Dodecahedron:
                            polys = new[] { ConvexPolyhedron.CreateDodecahedron(Vector3.zero, 0.5f) };
                            break;

                        case Primitive.Mesh:
                            polys = ConvexPolyhedron.CreateFromMesh(brush.GetComponent<MeshFilter>().sharedMesh);
                            break;
                    }

                    var matrix = brush.transform.localToWorldMatrix;

                    foreach (var poly in polys)
                    {
                        poly.MaterialIndex = brush.MaterialIndex;
                        poly.Transform(matrix);

                        Combine(poly, brush.Operator);
                    }
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

        [ThreadStatic] private static List<(ConvexPolyhedron, float)> _sChunks;
        [ThreadStatic] private static HashSet<ConvexPolyhedron> _sVisited;
        [ThreadStatic] private static Queue<ConvexPolyhedron> _sVisitQueue;

        private static List<(ConvexPolyhedron Root, float Volume)> GetChunkList()
        {
            var list = _sChunks ?? (_sChunks = new List<(ConvexPolyhedron, float)>());
            list.Clear();

            return list;
        }

        private static HashSet<ConvexPolyhedron> GetVisitedSet()
        {
            var set = _sVisited ?? (_sVisited = new HashSet<ConvexPolyhedron>());
            set.Clear();

            return set;
        }

        private static Queue<ConvexPolyhedron> GetVisitedQueue()
        {
            var queue = _sVisitQueue ?? (_sVisitQueue = new Queue<ConvexPolyhedron>());
            queue.Clear();

            return queue;
        }

        private void RemoveDisconnectedPolyhedra()
        {
            if (_polyhedra.Count == 0) return;

            var chunks = GetChunkList();
            var visited = GetVisitedSet();
            var queue = GetVisitedQueue();

            while (visited.Count < _polyhedra.Count)
            {
                queue.Clear();

                ConvexPolyhedron root = null;

                foreach (var poly in _polyhedra)
                {
                    if (visited.Contains(poly)) continue;

                    root = poly;
                    break;
                }

                Debug.Assert(root != null);

                visited.Add(root);
                queue.Enqueue(root);

                var volume = 0f;

                while (queue.Count > 0)
                {
                    var next = queue.Dequeue();

                    volume += next.Volume;

                    next.AddNeighbors(visited, queue);
                }

                chunks.Add((root, volume));
            }

            if (chunks.Count == 1) return;

            chunks.Sort((a, b) => Math.Sign(b.Volume - a.Volume));

            foreach (var chunk in chunks.Skip(1))
            {
                visited.Clear();
                queue.Clear();

                queue.Enqueue(chunk.Root);
                visited.Add(chunk.Root);

                while (queue.Count > 0)
                {
                    var next = queue.Dequeue();

                    next.InvalidateMesh();
                    next.AddNeighbors(visited, queue);
                }

                _polyhedra.RemoveAll(x => visited.Contains(x));

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

                child._polyhedra.AddRange(visited);
                child._meshInvalid = true;

                child.Start();
                child.Update();
            }
        }

        [ThreadStatic]
        private static HashSet<ConvexFace> _sExcludedFaces;

        [ThreadStatic]
        private static List<ConvexPolyhedron> _sIntersections;

        public bool Combine(ConvexPolyhedron polyhedron, BrushOperator op)
        {
            if (polyhedron.IsEmpty) return false;

            var changed = false;
            var maybeOutside = false;

            var min = polyhedron.VertexMin - BspSolid.Epsilon;
            var max = polyhedron.VertexMax + BspSolid.Epsilon;

            var intersections = _sIntersections ?? (_sIntersections = new List<ConvexPolyhedron>());
            var excludedFaces = _sExcludedFaces ?? (_sExcludedFaces = new HashSet<ConvexFace>());

            intersections.Clear();

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

                    excludedFaces.Clear();
                    var (excludedNone, excludedAll, _) = next.Clip(face.Plane,
                        face.FaceCuts, null, excludedFaces, dryRun: true);

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

                    child.CopyFaces(excludedFaces);

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
                        intersections.Add(next);
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

                foreach (var intersection in intersections)
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
