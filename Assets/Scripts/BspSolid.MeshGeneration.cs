using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace CsgTest
{
    partial class BspSolid
    {
        private class MeshGenerator
        {
            private readonly HashSet<uint> _pathSet = new HashSet<uint>();
            private readonly Queue<uint> _pathQueue = new Queue<uint>();

            private readonly List<Vector3> _meshVertices = new List<Vector3>();
            private readonly List<Vector3> _meshNormals = new List<Vector3>();
            private readonly List<int> _meshIndices = new List<int>();

            private readonly List<FaceCut> _faceCuts = new List<FaceCut>();
            
            private NativeArray<BspNode> _nodes;
            private int _nodeCount;

            private NativeArray<BspPlane> _planes;
            private int _planeCount;

            public void WriteToMesh(BspSolid solid, Mesh mesh)
            {
                mesh?.Clear();

                _meshVertices.Clear();
                _meshNormals.Clear();
                _meshIndices.Clear();

                _nodes = solid._nodes;
                _nodeCount = solid._nodeCount;

                _planes = solid._planes;
                _planeCount = solid._planeCount;

                for (ushort i = 0; i < _nodeCount; ++i)
                {
                    _pathQueue.Clear();
                    _pathSet.Clear();

                    _pathQueue.Enqueue(0u);

                    while (_pathQueue.Count > 0)
                    {
                        var path = _pathQueue.Dequeue();

                        if (!_pathSet.Add(path)) continue;

                        TriangulateFace(i, path);
                    }
                }

                _nodes = default;
                _nodeCount = 0;

                _planes = default;
                _planeCount = 0;

                mesh?.SetVertices(_meshVertices);
                mesh?.SetNormals(_meshNormals);
                mesh?.SetTriangles(_meshIndices, 0);

                mesh?.UploadMeshData(false);

                mesh?.MarkModified();
            }

            private TriangulationStats TriangulateFace(ushort index, uint path)
            {
                var node = _nodes[index];

                if (node.NegativeIndex == node.PositiveIndex && BspNode.IsLeafIndex(node.NegativeIndex)) return default;

                var plane = _planes[node.PlaneIndex];
                var (origin, tu, tv) = plane.GetBasis();

                _faceCuts.Clear();

                var parent = node;
                var prevIndex = index;

                while (parent.ParentIndex != BspNode.NullParentIndex)
                {
                    var curIndex = parent.ParentIndex;
                    parent = _nodes[curIndex];

                    var cutPlane = _planes[parent.PlaneIndex];
                    var faceCut = Helpers.GetFaceCut(plane, parent.PositiveIndex == prevIndex ? cutPlane : -cutPlane, origin, tu, tv);
                    var (excludesNone, excludesAll) = _faceCuts.GetNewFaceCutExclusions(faceCut);

                    if (excludesAll) return default;
                    if (!excludesNone) _faceCuts.AddFaceCut(faceCut);

                    prevIndex = curIndex;
                }

                var pathIndex = 0;
                var negativeLeaf = node.NegativeIndex;
                var positiveLeaf = node.PositiveIndex;

                bool valid;

                if (!BspNode.IsLeafIndex(negativeLeaf))
                {
                    (valid, negativeLeaf) = HandleChildCuts(plane, origin, tu, tv, _nodes[negativeLeaf], path, ref pathIndex);
                    if (!valid) return default;
                }

                if (!BspNode.IsLeafIndex(positiveLeaf))
                {
                    (valid, positiveLeaf) = HandleChildCuts(plane, origin, tu, tv, _nodes[positiveLeaf], path, ref pathIndex);
                    if (!valid) return default;
                }

                TriangulationStats stats = default;

                if (negativeLeaf == BspNode.OutIndex)
                {
                    stats.NegativeOut = true;
                }
                else
                {
                    stats.NegativeIn = true;
                }

                if (positiveLeaf == BspNode.OutIndex)
                {
                    stats.PositiveOut = true;
                }
                else
                {
                    stats.PositiveIn = true;
                }

                if (negativeLeaf == positiveLeaf) return stats;

                for (var i = _faceCuts.Count - 1; i >= 0; --i)
                {
                    var cut = _faceCuts[i];

                    if (float.IsNegativeInfinity(cut.Min) || float.IsPositiveInfinity(cut.Max))
                    {
                        return stats;
                    }

                    if (cut.Max <= cut.Min)
                    {
                        _faceCuts.RemoveAt(i);
                    }
                }

                stats.VertexCount = _faceCuts.Count;

                if (_faceCuts.Count == 0) return stats;

                _faceCuts.Sort(FaceCut.Comparer);

                var firstIndex = _meshVertices.Count;

                var normal = plane.Normal * (positiveLeaf == BspNode.InIndex ? -1f : 1f);

                foreach (var cut in _faceCuts)
                {
                    _meshVertices.Add(cut.GetPoint(origin, tu, tv, cut.Max));
                    _meshNormals.Add(normal);
                }

                if (negativeLeaf == BspNode.InIndex)
                {
                    for (var i = firstIndex + 2; i < _meshVertices.Count; ++i)
                    {
                        _meshIndices.Add(firstIndex);
                        _meshIndices.Add(i);
                        _meshIndices.Add(i - 1);
                    }
                }
                else
                {
                    for (var i = firstIndex + 2; i < _meshVertices.Count; ++i)
                    {
                        _meshIndices.Add(firstIndex);
                        _meshIndices.Add(i - 1);
                        _meshIndices.Add(i);
                    }
                }

                return stats;
            }

            private (bool, ushort) HandleChildCuts(BspPlane plane, float3 origin, float3 tu, float3 tv, BspNode child, uint path, ref int pathIndex)
            {
                while (true)
                {
                    bool positive;

                    var childPlane = _planes[child.PlaneIndex];
                    var positiveCut = Helpers.GetFaceCut(plane, childPlane, origin, tu, tv);
                    var negativeCut = -positiveCut;

                    var (negExcludesAll, posExcludesAll) = _faceCuts.GetNewFaceCutExclusions(positiveCut);

                    if (negExcludesAll && posExcludesAll)
                    {
                        return (false, default);
                    }

                    if (!negExcludesAll && !posExcludesAll)
                    {
                        positive = ((path >> pathIndex) & 1) == 1;

                        if (!positive)
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

                    var nextIndex = positive ? child.PositiveIndex : child.NegativeIndex;

                    if (BspNode.IsLeafIndex(nextIndex))
                    {
                        return (true, nextIndex);
                    }

                    child = _nodes[nextIndex];
                }
            }

            private struct TriangulationStats
            {
                public static TriangulationStats operator +(TriangulationStats a, TriangulationStats b)
                {
                    return new TriangulationStats
                    {
                        VertexCount = a.VertexCount + b.VertexCount,

                        NegativeOut = a.NegativeOut | b.NegativeOut,
                        NegativeIn = a.NegativeIn | b.NegativeIn,
                        PositiveOut = a.PositiveOut | b.PositiveOut,
                        PositiveIn = a.PositiveIn | b.PositiveIn
                    };
                }

                public int VertexCount;

                public bool NegativeOut;
                public bool NegativeIn;
                public bool PositiveOut;
                public bool PositiveIn;

                public override string ToString()
                {
                    return
                        $"{{ VertexCount: {VertexCount}, NegativeOut: {NegativeOut}, NegativeIn: {NegativeIn}, PositiveOut: {PositiveOut}, PositiveIn: {PositiveIn} }}";
                }
            }
        }

        [ThreadStatic]
        private static MeshGenerator _sMeshGenerator;

        public void WriteToMesh(Mesh mesh)
        {
            (_sMeshGenerator ?? (_sMeshGenerator = new MeshGenerator())).WriteToMesh(this, mesh);
        }
    }
}
