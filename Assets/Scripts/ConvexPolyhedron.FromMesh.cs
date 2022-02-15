using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

namespace CsgTest
{
    partial class ConvexPolyhedron
    {
        private class MeshDecomposer
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Vector3> _uniqueVertices = new List<Vector3>();
            private readonly List<int> _indices = new List<int>();
            private readonly List<int> _mergedIndices = new List<int>();
            private readonly Dictionary<int3, int> _mergedVertices = new Dictionary<int3, int>();

            private float3 _vertexMergeGridScale;
            private const int VertexMergeGridResolution = 1000;

            private readonly List<(Face Face, int Excluded)> _faces = new List<(Face, int)>();
            private readonly Dictionary<Edge, (Face A, Face B)> _edges =
                new Dictionary<Edge, (Face A, Face B)>();

            private readonly HashSet<ConvexFace> _excludedFaces = new HashSet<ConvexFace>();
            private readonly List<FaceCut> _faceCuts = new List<FaceCut>();

            private readonly List<ConvexPolyhedron> _finalPolys = new List<ConvexPolyhedron>();

            private class Face
            {
                public BspPlane Plane { get; }

                public List<Edge> Edges { get; } = new List<Edge>(4);
                public List<Edge> InnerEdges { get; } = new List<Edge>();

                public Face(BspPlane plane)
                {
                    Plane = plane;
                }

                public override string ToString()
                {
                    return $"{{ {Plane}, {Edges.Count} Edges }}";
                }
            }

            private struct Edge : IEquatable<Edge>
            {
                public static Edge operator -(Edge edge)
                {
                    return new Edge(edge.VertexBIndex, edge.VertexAIndex);
                }

                public readonly int VertexAIndex;
                public readonly int VertexBIndex;

                public Edge(int vertexAIndex, int vertexBIndex)
                {
                    VertexAIndex = vertexAIndex;
                    VertexBIndex = vertexBIndex;
                }
                
                public bool Equals(Edge other)
                {
                    return VertexAIndex == other.VertexAIndex && VertexBIndex == other.VertexBIndex
                        || VertexAIndex == other.VertexBIndex && VertexBIndex == other.VertexAIndex;
                }

                public override bool Equals(object obj)
                {
                    return obj is Edge other && Equals(other);
                }

                public override int GetHashCode()
                {
                    return VertexAIndex + VertexBIndex;
                }

                public override string ToString()
                {
                    return $"{{ {VertexAIndex}, {VertexBIndex} }}";
                }
            }

            private bool ApproxEquals(List<FaceCut> cutsA, List<FaceCut> cutsB)
            {
                if (cutsA.Count != cutsB.Count) return false;

                for (var i = 0; i < cutsA.Count; ++i)
                {
                    var a = cutsA[i];
                    var b = cutsB[i];

                    if (!a.ApproxEquals(b)) return false;
                }

                return true;
            }

            public ConvexPolyhedron[] Decompose(Mesh mesh)
            {
                Reset();
                ReadFromMesh(mesh);
                PopulateEdgesFaces();
                // MergeCoplanarFaces();
                SortFaces();

                var polyQueue = new Queue<(ConvexPolyhedron Poly, int NextFace)>();

                polyQueue.Enqueue((new ConvexPolyhedron(), 0));

                while (polyQueue.Count > 0 && polyQueue.Count < 1000)
                {
                    var (poly, nextFace) = polyQueue.Dequeue();
                    var anyInside = false;

                    for (var i = nextFace; i < _faces.Count; ++i)
                    {
                        var (face, excludedCount) = _faces[i];

                        _faceCuts.Clear();

                        var basis = face.Plane.GetBasis();

                        foreach (var edge in face.Edges)
                        {
                            var a = (float3)_vertices[edge.VertexAIndex];
                            var b = (float3)_vertices[edge.VertexBIndex];

                            var abPlane = new BspPlane(math.cross(face.Plane.Normal, a - b), a);
                            var abCut = Helpers.GetFaceCut(face.Plane, abPlane, basis);

                            _faceCuts.AddFaceCut(abCut);
                        }

                        _faceCuts.Sort(FaceCut.Comparer);

                        _excludedFaces.Clear();
                        var (excludedNone, excludedAll, newFace) = poly.Clip(face.Plane, _faceCuts, null, _excludedFaces, true);

                        if (excludedNone)
                        {
                            continue;
                        }

                        if (excludedAll)
                        {
                            poly.SetEmpty();
                            break;
                        }

                        anyInside = true;

                        if (excludedCount == 0 || ApproxEquals(_faceCuts, newFace.FaceCuts))
                        {
                            poly.Clip(face.Plane);
                            continue;
                        }

                        var child = new ConvexPolyhedron();

                        child.CopyFaces(_excludedFaces);

                        poly.Clip(face.Plane, null, child);
                        child.Clip(-face.Plane, null, poly);

                        if (!child.IsEmpty)
                        {
                            polyQueue.Enqueue((child, i + 1));
                        }
                        else
                        {
                            Debug.LogError("Empty child!");
                            child.Removed(null);
                        }
                    }

                    if (anyInside && !poly.IsEmpty)
                    {
                        poly.InvalidateMesh();
                        _finalPolys.Add(poly);
                    }
                    else
                    {
                        poly.Removed(null);
                    }
                }

                return _finalPolys.ToArray();
            }

            private void Reset()
            {
                _edges.Clear();
                _faces.Clear();

                _finalPolys.Clear();
            }

            private static readonly int[] _sMergeOffsets1D = new[] { 0, -1, 1 };

            private static readonly int3[] _sMergeOffsets =
                _sMergeOffsets1D.SelectMany(x =>
                    _sMergeOffsets1D.SelectMany(y =>
                        _sMergeOffsets1D.Select(z => new int3(x, y, z))))
                    .OrderBy(x => math.lengthsq(x))
                .ToArray();

            private void ReadFromMesh(Mesh mesh)
            {
                _vertices.Clear();
                _uniqueVertices.Clear();
                _indices.Clear();

                Debug.Assert(mesh.GetTopology(0) == MeshTopology.Triangles);

                mesh.GetVertices(_vertices);
                mesh.GetIndices(_indices, 0);

                var min = new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                var max = new float3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

                foreach (var vertex in _vertices)
                {
                    min = math.min(min, vertex);
                    max = math.max(max, vertex);
                }

                if (max.Equals(min))
                {
                    _vertexMergeGridScale = new float3(1f, 1f, 1f);
                }
                else
                {
                    _vertexMergeGridScale = VertexMergeGridResolution / (max - min);
                }

                _mergedIndices.Clear();
                _mergedVertices.Clear();

                for (var i = 0; i < _vertices.Count; ++i)
                {
                    var index3 = (int3) math.floor(_vertices[i] * _vertexMergeGridScale);
                    var found = false;

                    foreach (var offset in _sMergeOffsets)
                    {
                        if (_mergedVertices.TryGetValue(index3 + offset, out var index))
                        {
                            found = true;
                            _mergedIndices.Add(index);
                            break;
                        }
                    }

                    if (found) continue;

                    _uniqueVertices.Add(_vertices[i]);
                    _mergedVertices.Add(index3, i);
                    _mergedIndices.Add(i);
                }
            }

            private void PopulateEdgesFaces()
            {
                for (var i = 0; i < _indices.Count; i += 3)
                {
                    var indexA = _mergedIndices[_indices[i + 0]];
                    var indexB = _mergedIndices[_indices[i + 1]];
                    var indexC = _mergedIndices[_indices[i + 2]];

                    if (indexA == indexB || indexB == indexC || indexA == indexC)
                    {
                        continue;
                    }

                    var a = (float3)_vertices[indexA];
                    var b = (float3)_vertices[indexB];
                    var c = (float3)_vertices[indexC];

                    var cross = math.cross(a - b, c - b);

                    if (math.abs(math.lengthsq(cross)) < BspSolid.Epsilon)
                    {
                        throw new Exception("Unsupported geometry");
                    }

                    var plane = new BspPlane(cross, a);
                    var face = new Face(plane);

                    face.Edges.Add(new Edge(indexA, indexB));
                    face.Edges.Add(new Edge(indexB, indexC));
                    face.Edges.Add(new Edge(indexC, indexA));

                    foreach (var edge in face.Edges)
                    {
                        if (_edges.TryGetValue(edge, out var facePair))
                        {
                            if (facePair.B != null)
                            {
                                throw new Exception("Unsupported geometry");
                            }

                            if (facePair.A == face)
                            {
                                throw new Exception("Unsupported geometry");
                            }

                            _edges[-edge] = (facePair.A, face);
                        }
                        else
                        {
                            _edges.Add(edge, (face, null));
                        }
                    }

                    _faces.Add((face, -1));
                }

                foreach (var pair in _edges)
                {
                    if (pair.Value.B == null)
                    {
                        throw new Exception("Mesh isn't a closed surface!");
                    }
                }
            }

            private void MergeCoplanarFaces()
            {
                for (var i = _faces.Count - 1; i >= 0; --i)
                {
                    var (face, _) = _faces[i];
                    Face coplanarFace = null;

                    var fromIndex = 0;
                    var insertIndex = 0;

                    for (var edgeIndex = 0; edgeIndex < face.Edges.Count; ++edgeIndex)
                    {
                        var edge = face.Edges[edgeIndex];
                        var pair = _edges[edge];
                        var otherFace = pair.A == face ? pair.B : pair.A;

                        if (otherFace == face)
                        {
                            throw new Exception("Invalid edge");
                        }

                        if (!otherFace.Plane.ApproxEquals(face.Plane, 0.01f, 0.01f))
                        {
                            continue;
                        }

                        _edges.Remove(edge);

                        coplanarFace = otherFace;
                        fromIndex = edgeIndex;
                        insertIndex = coplanarFace.Edges.IndexOf(edge);

                        coplanarFace.Edges.RemoveAt(insertIndex);

                        coplanarFace.InnerEdges.Add(edge);
                        break;
                    }

                    if (coplanarFace == null)
                    {
                        continue;
                    }

                    _faces.RemoveAt(i);

                    if (face.Edges.Count > 100)
                    {
                        throw new Exception("Maybe stuck in a loop");
                    }

                    for (var j = 1; j < face.Edges.Count; ++j)
                    {
                        var edge = face.Edges[(fromIndex + j) % face.Edges.Count];
                        var pair = _edges[edge];

                        coplanarFace.Edges.Insert(insertIndex++, edge);

                        if (pair.A == face && pair.B != coplanarFace)
                        {
                            _edges[edge] = (coplanarFace, pair.B);
                        }
                        else if (pair.B == face && pair.A != coplanarFace)
                        {
                            _edges[-edge] = (pair.A, coplanarFace);
                        }
                        else
                        {
                            throw new Exception("Unexpected case when merging faces");
                        }
                    }
                }
            }

            private static int GetFaceScore(int excluded, int vertexCount)
            {
                return excluded == 0 ? -1 : math.abs(excluded * 2 - vertexCount);
            }

            private void SortFaces()
            {
                for (var i = 0; i < _faces.Count; ++i)
                {
                    var (face, _) = _faces[i];
                    var excludedCount = 0;

                    foreach (var vertex in _uniqueVertices)
                    {
                        var offset = math.dot(vertex, face.Plane.Normal) - face.Plane.Offset;

                        if (offset < -0.001f)
                        {
                            ++excludedCount;
                        }
                    }

                    _faces[i] = (face, excludedCount);
                }

                var vertexCount = _uniqueVertices.Count;

                _faces.Sort((a, b) =>
                    GetFaceScore(a.Excluded, vertexCount) - GetFaceScore(b.Excluded, vertexCount));
            }
        }

        [ThreadStatic]
        private static MeshDecomposer _sDecomposer;

        public static ConvexPolyhedron[] CreateFromMesh(Mesh mesh)
        {
            return (_sDecomposer ?? (_sDecomposer = new MeshDecomposer())).Decompose(mesh);
        }
    }
}
