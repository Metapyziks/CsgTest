using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace CsgTest
{
    partial class BspSolid
    {
        private readonly struct Face
        {
            public readonly ushort FirstVertex;
            public readonly ushort VertexCount;
            public readonly bool Flipped;
            public readonly float3 Normal;

            public Face(ushort firstVertex, ushort vertexCount, bool flipped, float3 normal)
            {
                FirstVertex = firstVertex;
                VertexCount = vertexCount;
                Flipped = flipped;
                Normal = normal;
            }
        }

        private class MeshGenerator
        {
            private readonly HashSet<uint> _pathSet = new HashSet<uint>();
            private readonly Queue<uint> _pathQueue = new Queue<uint>();

            private readonly Stack<BspPlane> _planeStack = new Stack<BspPlane>();

            private readonly List<Face> _faces = new List<Face>();

            private readonly List<FaceCut> _nodeCuts = new List<FaceCut>();
            private readonly List<FaceCut> _faceCuts = new List<FaceCut>();

            private Vector3[] _vertices;
            private BspNode[] _nodes;
            private BspPlane[] _planes;

            private int _vertexCount;
            private int _triangleCount;

            public bool StatsOnly { get; set; }

            private bool _enableIndexFilter;
            private readonly HashSet<int> _indexFilter = new HashSet<int>();

            public void Reset()
            {
                Clear();

                StatsOnly = false;

                _enableIndexFilter = false;
                _indexFilter.Clear();
            }

            public void Clear()
            {
                _faces.Clear();

                _vertexCount = 0;
                _triangleCount = 0;
            }

            public void FilterNodes(IEnumerable<int> indices)
            {
                _enableIndexFilter = true;

                foreach (var index in indices)
                {
                    _indexFilter.Add((ushort) index);
                }
            }

            public void Init(BspSolid solid)
            {
                _nodes = solid._nodes;
                _planes = solid._planes;

                _planeStack.Clear();
            }

            private void CleanUp()
            {
                _nodes = default;
                _planes = default;

                _enableIndexFilter = false;
                _indexFilter.Clear();
            }

            public void Write(BspSolid solid)
            {
                if (solid._nodeCount == 0) return;

                Init(solid);
                TriangulateFaces(_planeStack, solid._rootIndex);
                CleanUp();
            }

            public Bounds CalculateBounds()
            {
                var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

                foreach (var vertex in _vertices)
                {
                    min = Vector3.Min(min, vertex);
                    max = Vector3.Max(max, vertex);
                }

                return new Bounds((min + max) * 0.5f, max - min);
            }

            public void CopyToMesh(Mesh mesh)
            {
                mesh.Clear();

                var normals = new NativeArray<float3>(_vertexCount, Allocator.Temp);
                var indices = new NativeArray<ushort>(_triangleCount * 3, Allocator.Temp);

                var writeIndex = 0;
                
                foreach (var face in _faces)
                {
                    for (var i = 0; i < face.VertexCount; ++i)
                    {
                        normals[face.FirstVertex + i] = face.Normal;
                    }

                    if (face.Flipped)
                    {
                        for (var i = 2; i < face.VertexCount; ++i)
                        {
                            indices[writeIndex++] = face.FirstVertex;
                            indices[writeIndex++] = (ushort)(face.FirstVertex + i - 1);
                            indices[writeIndex++] = (ushort)(face.FirstVertex + i);
                        }
                    }
                    else
                    {
                        for (var i = 2; i < face.VertexCount; ++i)
                        {
                            indices[writeIndex++] = face.FirstVertex;
                            indices[writeIndex++] = (ushort)(face.FirstVertex + i);
                            indices[writeIndex++] = (ushort)(face.FirstVertex + i - 1);
                        }
                    }
                }

                mesh.SetVertices(_vertices, 0, _vertexCount);
                mesh.SetNormals(normals, 0, _vertexCount);
                mesh.SetIndices(indices, MeshTopology.Triangles, 0);

                normals.Dispose();
                indices.Dispose();

                mesh.UploadMeshData(false);

                mesh.MarkModified();
            }

            public void DebugDraw()
            {
                const float margin = 0.05f;

                var random = new Unity.Mathematics.Random(0x54ad3e92);

                var index = 0;
                foreach (var face in _faces)
                {
                    Gizmos.color = new Color(random.NextFloat(), random.NextFloat(), random.NextFloat());

                    var prev = (float3) _vertices[face.FirstVertex + face.VertexCount - 1];
                    var avg = float3.zero;

                    for (var i = 0; i < face.VertexCount; ++i)
                    {
                        var next = (float3) _vertices[face.FirstVertex + i];
                        var tangent = math.normalizesafe(next - prev);
                        var binormal = math.normalizesafe(math.cross(tangent, face.Normal));
                        var offset = face.Normal * 0.001f
                            + (face.Flipped ? -binormal * margin : binormal * margin);

                        Gizmos.DrawLine(
                            prev + offset + tangent * margin,
                            next + offset - tangent * margin);

                        avg += next;
                        prev = next;
                    }

#if UNITY_EDITOR
                    //avg /= face.VertexCount;

                    //UnityEditor.Handles.color = Gizmos.color;
                    //UnityEditor.Handles.Label(avg, $"face {index}");
#endif

                    ++index;
                }
            }

            private TriangulationStats TriangulateFaces(Stack<BspPlane> planes, NodeIndex index)
            {
                if (index.IsLeaf) return default;

                var node = _nodes[index];
                var plane = _planes[node.PlaneIndex];

                TriangulationStats stats = default;

                if (!_enableIndexFilter || _indexFilter.Count == 0 || _indexFilter.Contains(index))
                {
                    stats += TriangulateFace(planes, node);

                    if (stats.VertexCount == 0)
                    {
                        Debug.Log($"No verts: {index}");
                    }
                }
                
                planes.Push(-plane);
                stats += TriangulateFaces(planes, node.NegativeIndex);
                planes.Pop();

                planes.Push(plane);
                stats += TriangulateFaces(planes, node.PositiveIndex);
                planes.Pop();

                return stats;
            }

            public TriangulationStats TriangulateFace(Stack<BspPlane> planes, BspNode node)
            {
                var plane = _planes[node.PlaneIndex];
                var basis = plane.GetBasis();

                _nodeCuts.Clear();

                foreach (var cutPlane in planes)
                {
                    var faceCut = Helpers.GetFaceCut(plane, cutPlane, basis);
                    var (excludesNone, excludesAll) = _nodeCuts.GetNewFaceCutExclusions(faceCut);

                    if (excludesAll) return default;
                    if (!excludesNone) _nodeCuts.AddFaceCut(faceCut);
                }

                _pathQueue.Clear();
                _pathSet.Clear();

                _pathQueue.Enqueue(0u);

                var stats = default(TriangulationStats);

                while (_pathQueue.Count > 0)
                {
                    var path = _pathQueue.Dequeue();

                    if (!_pathSet.Add(path))
                    {
                        continue;
                    }

                    _faceCuts.Clear();
                    _faceCuts.AddRange(_nodeCuts);

                    var pathIndex = 0;
                    var negativeLeaf = HandleChildCuts(plane, basis, node.NegativeIndex, path,
                        ref pathIndex);
                    var positiveLeaf = HandleChildCuts(plane, basis, node.PositiveIndex, path,
                        ref pathIndex);

                    if (!negativeLeaf.IsLeaf || !positiveLeaf.IsLeaf)
                    {
                        continue;
                    }

                    stats.NegativeOut |= negativeLeaf.IsOut;
                    stats.NegativeIn |= negativeLeaf.IsIn;

                    stats.PositiveOut |= positiveLeaf.IsOut;
                    stats.PositiveIn |= positiveLeaf.IsIn;

                    if (!_enableIndexFilter && negativeLeaf == positiveLeaf) continue;

                    _faceCuts.RemoveAll(x => x.Max <= x.Min + DistanceEpsilon);

                    if (_faceCuts.Count < 3) continue;

                    var vertexCount = (ushort)(_faceCuts.Count + _faceCuts.Count(x => float.IsNegativeInfinity(x.Min)));

                    stats.FaceCount += 1;
                    stats.VertexCount += vertexCount;

                    _triangleCount += vertexCount - 2;

                    if (StatsOnly) continue;

                    _faceCuts.Sort(FaceCut.Comparer);

                    var firstVertex = (ushort) _vertexCount;
                    var flipped = positiveLeaf.IsIn;
                    var normal = plane.Normal * (positiveLeaf.IsIn ? -1f : 1f);

                    _faces.Add(new Face(firstVertex, vertexCount, flipped, normal));

                    Helpers.EnsureCapacity(ref _vertices, _vertexCount + _faceCuts.Count);

                    foreach (var cut in _faceCuts)
                    {
                        _vertices[_vertexCount++] = cut.GetPoint(basis, cut.Max);

                        if (float.IsNegativeInfinity(cut.Min))
                        {
                            _vertices[_vertexCount++] = cut.GetPoint(basis, cut.Min);
                        }
                    }
                }

                return stats;
            }

            private NodeIndex HandleChildCuts(BspPlane plane, in (float3 origin, float3 tu, float3 tv) basis, NodeIndex index, uint path, ref int pathIndex)
            {
                while (true)
                {
                    if (index.IsLeaf)
                    {
                        return index;
                    }

                    bool positive;

                    var child = _nodes[index];
                    var childPlane = _planes[child.PlaneIndex];
                    var positiveCut = Helpers.GetFaceCut(plane, childPlane, basis);
                    var negativeCut = -positiveCut;

                    var (negExcludesAll, posExcludesAll) = _faceCuts.GetNewFaceCutExclusions(positiveCut);

                    if (negExcludesAll && posExcludesAll)
                    {
                        return default;
                    }

                    if (!negExcludesAll && !posExcludesAll)
                    {
                        positive = ((path >> pathIndex) & 1) == 1;

                        if (path >> pathIndex == 0)
                        {
                            _pathQueue.Enqueue(path | (uint)(1 << pathIndex));
                        }

                        ++pathIndex;
                    }
                    else
                    {
                        positive = negExcludesAll;
                    }

                    if (positive && !negExcludesAll || !positive && !posExcludesAll)
                    {
                        _faceCuts.AddFaceCut(positive ? positiveCut : negativeCut);
                    }

                    index = positive ? child.PositiveIndex : child.NegativeIndex;
                }
            }
        }

        public struct TriangulationStats
        {
            public static TriangulationStats operator +(TriangulationStats a, TriangulationStats b)
            {
                return new TriangulationStats
                {
                    FaceCount = a.FaceCount + b.FaceCount,
                    VertexCount = a.VertexCount + b.VertexCount,

                    NegativeOut = a.NegativeOut | b.NegativeOut,
                    NegativeIn = a.NegativeIn | b.NegativeIn,
                    PositiveOut = a.PositiveOut | b.PositiveOut,
                    PositiveIn = a.PositiveIn | b.PositiveIn
                };
            }

            public int FaceCount;
            public int VertexCount;

            public bool NegativeOut;
            public bool NegativeIn;
            public bool PositiveOut;
            public bool PositiveIn;

            public override string ToString()
            {
                return
                    $"{{ FaceCount: {FaceCount}, VertexCount: {VertexCount}, NegativeOut: {NegativeOut}, NegativeIn: {NegativeIn}, PositiveOut: {PositiveOut}, PositiveIn: {PositiveIn} }}";
            }
        }

        [ThreadStatic]
        private static MeshGenerator _sMeshGenerator;

        private static MeshGenerator GetMeshGenerator()
        {
            if (_sMeshGenerator == null) _sMeshGenerator = new MeshGenerator();

            _sMeshGenerator.Reset();

            return _sMeshGenerator;
        }

        public void WriteToMesh(Mesh mesh)
        {
            var meshGen = GetMeshGenerator();

            meshGen.Write(this);
            meshGen.CopyToMesh(mesh);
        }

        public void DrawDebugNodes(IEnumerable<int> nodeIndices)
        {
            var meshGen = GetMeshGenerator();

            meshGen.FilterNodes(nodeIndices);
            meshGen.Write(this);
            meshGen.DebugDraw();
        }
    }
}
