using System;
using System.Collections;
using System.Collections.Generic;
using Codice.CM.Client.Differences;
using Unity.Collections;
using UnityEngine;

using Unity.Mathematics;

namespace CsgTest
{
    public readonly struct BspPlane : IEquatable<BspPlane>
    {
        public static BspPlane operator -(BspPlane plane)
        {
            return new BspPlane(-plane.Normal, -plane.Offset);
        }

        public readonly float3 Normal;
        public readonly float Offset;

        public BspPlane(float3 normalDir, float3 position)
        {
            Normal = math.normalizesafe(normalDir);
            Offset = math.dot(Normal, position);
        }

        public BspPlane(float3 normal, float offset)
        {
            Normal = normal;
            Offset = offset;
        }

        public BspPlane Transform(in float4x4 matrix, in float4x4 transInvMatrix)
        {
            var position = math.transform(matrix, Normal * Offset);
            var normal = math.mul(transInvMatrix, new float4(Normal, 0f)).xyz;
            return new BspPlane(normal, position);
        }

        public bool Equals(BspPlane other)
        {
            return Normal.Equals(other.Normal) && Offset.Equals(other.Offset);
        }

        public override bool Equals(object obj)
        {
            return obj is BspPlane other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Normal.GetHashCode() * 397) ^ Offset.GetHashCode();
            }
        }

        public override string ToString()
        {
            return $"{{ Normal: {Normal}, Offset: {Offset} }}";
        }
    }

    public readonly struct BspNode
    {
        public const ushort InIndex = 0;
        public const ushort OutIndex = ushort.MaxValue;

        public const ushort NullParentIndex = ushort.MaxValue;

        public static bool IsLeafIndex(ushort index)
        {
            return index == InIndex || index == OutIndex;
        }

        public readonly ushort PlaneIndex;
        public readonly ushort ParentIndex;
        public readonly ushort NegativeIndex;
        public readonly ushort PositiveIndex;

        public BspNode(ushort planeIndex, ushort parentIndex, ushort negativeIndex, ushort positiveIndex) =>
            (PlaneIndex, ParentIndex, NegativeIndex, PositiveIndex) = (planeIndex, parentIndex, negativeIndex, positiveIndex);

        public BspNode WithNegativeIndex(ushort value)
        {
            return new BspNode(PlaneIndex, ParentIndex, value, PositiveIndex);
        }

        public BspNode WithPositiveIndex(ushort value)
        {
            return new BspNode(PlaneIndex, ParentIndex, NegativeIndex, value);
        }

        public BspNode WithOffset(ushort rootParentIndex, int nodeIndexOffset, int planeIndexOffset)
        {
            return new BspNode((ushort) (PlaneIndex + planeIndexOffset),
                ParentIndex == NullParentIndex ? rootParentIndex : (ushort) (ParentIndex + nodeIndexOffset),
                IsLeafIndex(NegativeIndex) ? NegativeIndex : (ushort)(NegativeIndex + nodeIndexOffset),
                IsLeafIndex(PositiveIndex) ? PositiveIndex : (ushort)(PositiveIndex + nodeIndexOffset));
        }

        private static string ParentToString(ushort parentIndex)
        {
            return parentIndex == NullParentIndex ? "NULL" : $"N{parentIndex:000}";
        }

        private static string ChildToString(ushort childIndex)
        {
            switch (childIndex)
            {
                case InIndex: return "IN  ";
                case OutIndex: return "OUT ";
                default: return $"N{childIndex:000}";
            }
        }

        public override string ToString()
        {
            return $"{{ Plane: P{PlaneIndex:000}, Parent: {ParentToString(ParentIndex)}, Negative: {ChildToString(NegativeIndex)}, Positive: {ChildToString(PositiveIndex)} }}";
        }
    }

    public class BspSolid : IDisposable
    {
        private NativeArray<BspPlane> _planes;
        private NativeArray<BspNode> _nodes;

        private int _planeCount;
        private int _nodeCount;

        public static BspSolid CreateCube(float3 center, float3 size)
        {
            var mesh = new BspSolid
            {
                _planes = new NativeArray<BspPlane>(6, Allocator.Persistent),
                _nodes = new NativeArray<BspNode>(6, Allocator.Persistent)
            };

            var min = center - size * 0.5f;
            var max = center + size * 0.5f;

            mesh.Cut(new BspPlane(new float3(1f, 0f, 0f), min));
            mesh.Cut(new BspPlane(new float3(0f, 1f, 0f), min));
            mesh.Cut(new BspPlane(new float3(0f, 0f, 1f), min));
            mesh.Cut(new BspPlane(new float3(-1f, 0f, 0f), max));
            mesh.Cut(new BspPlane(new float3(0f, -1f, 0f), max));
            mesh.Cut(new BspPlane(new float3(0f, 0f, -1f), max));

            return mesh;
        }

        public void Clear()
        {
            _nodeCount = 0;
            _planeCount = 0;
        }

        private static void Resize<T>(ref NativeArray<T> array, int minSize)
            where T : struct
        {
            if (array.IsCreated && array.Length >= minSize) return;

            var oldArray = array;

            array = new NativeArray<T>(Mathf.NextPowerOfTwo(minSize), Allocator.Persistent);

            if (oldArray.IsCreated)
            {
                NativeArray<T>.Copy(oldArray, 0, array, 0, oldArray.Length);
                oldArray.Dispose();
            }
        }

        public ushort AddPlane(BspPlane plane)
        {
            // TODO: merge planes

            Resize(ref _planes, _planeCount + 1);

            _planes[_planeCount] = plane;

            return (ushort)_planeCount++;
        }

        public ushort AddNode(ushort planeIndex, ushort parentIndex, ushort negativeIndex, ushort positiveIndex)
        {
            Resize(ref _nodes, _nodeCount + 1);

            _nodes[_nodeCount] = new BspNode(planeIndex, parentIndex, negativeIndex, positiveIndex);

            return (ushort)_nodeCount++;
        }

        public void Cut(BspPlane plane)
        {
            var planeIndex = AddPlane(plane);

            if (_nodeCount == 0)
            {
                AddNode(planeIndex, BspNode.NullParentIndex, BspNode.OutIndex, BspNode.InIndex);
                return;
            }

            var oldNodeCount = _nodeCount;

            for (ushort i = 0; i < oldNodeCount; ++i)
            {
                var node = _nodes[i];

                if (node.NegativeIndex == BspNode.InIndex)
                {
                    node = node.WithNegativeIndex(AddNode(planeIndex, i, BspNode.OutIndex, BspNode.InIndex));
                }

                if (node.PositiveIndex == BspNode.InIndex)
                {
                    node = node.WithPositiveIndex(AddNode(planeIndex, i, BspNode.OutIndex, BspNode.InIndex));
                }

                _nodes[i] = node;
            }
        }

        private ushort InsertSubtree(NativeArray<BspNode> nodes, int count, ushort parentIndex, int planeIndexOffset)
        {
            var nodeIndexOffset = _nodeCount;

            Resize(ref _nodes, _nodeCount + count);

            for (var i = 0; i < count; ++i)
            {
                _nodes[nodeIndexOffset + i] = nodes[i].WithOffset(parentIndex, nodeIndexOffset, planeIndexOffset);
            }

            _nodeCount += count;

            return (ushort)nodeIndexOffset;
        }

        public void Union(BspSolid solid, float4x4 transform)
        {
            var planeIndexOffset = _planeCount;

            Resize(ref _planes, _planeCount + solid._planeCount);

            NativeArray<BspPlane>.Copy(solid._planes, 0, _planes, planeIndexOffset, solid._planeCount);

            _planeCount += solid._planeCount;

            var normalTransform = math.transpose(math.inverse(transform));

            for (var i = planeIndexOffset; i < _planeCount; ++i)
            {
                _planes[i] = _planes[i].Transform(transform, normalTransform);
            }

            if (_nodeCount == 0)
            {
                InsertSubtree(solid._nodes, solid._nodeCount, BspNode.NullParentIndex, planeIndexOffset);
                return;
            }

            var oldNodeCount = _nodeCount;

            for (ushort i = 0; i < oldNodeCount; ++i)
            {
                var node = _nodes[i];

                if (node.NegativeIndex == BspNode.OutIndex)
                {
                    node = node.WithNegativeIndex(InsertSubtree(solid._nodes, solid._nodeCount, i, planeIndexOffset));
                }

                if (node.PositiveIndex == BspNode.OutIndex)
                {
                    node = node.WithPositiveIndex(InsertSubtree(solid._nodes, solid._nodeCount, i, planeIndexOffset));
                }

                _nodes[i] = node;
            }
        }

        public void Transform(float4x4 matrix)
        {
            var transInvMatrix = math.transpose(math.inverse(matrix));

            for (var i = 0; i < _planeCount; ++i)
            {
                _planes[i] = _planes[i].Transform(matrix, transInvMatrix);
            }
        }

        public void Reduce()
        {
            // TODO
        }

        [ThreadStatic]
        private static List<Vector3> _sMeshVertices;

        [ThreadStatic]
        private static List<Vector3> _sMeshNormals;

        [ThreadStatic]
        private static List<int> _sMeshIndices;

        public void WriteToMesh(Mesh mesh)
        {
            mesh?.Clear();

            var vertices = _sMeshVertices ?? (_sMeshVertices = new List<Vector3>());
            var normals = _sMeshNormals ?? (_sMeshNormals = new List<Vector3>());
            var indices = _sMeshIndices ?? (_sMeshIndices = new List<int>());

            var paths = new Queue<uint>();

            vertices.Clear();
            normals.Clear();
            indices.Clear();

            for (ushort i = 0; i < _nodeCount; ++i)
            {
                paths.Clear();
                paths.Enqueue(0u);

                while (paths.Count > 0)
                {
                    TriangulateFace(paths, vertices, normals, indices, i, paths.Dequeue());
                }
            }

            mesh?.SetVertices(vertices);
            mesh?.SetNormals(normals);
            mesh?.SetTriangles(indices, 0);

            mesh?.UploadMeshData(false);

            mesh?.MarkModified();
        }

        private static float3 GetTangent(float3 normal)
        {
            var absX = math.abs(normal.x);
            var absY = math.abs(normal.y);
            var absZ = math.abs(normal.z);

            return math.cross(normal, absX <= absY && absX <= absZ
                ? new float3(1f, 0f, 0f) : absY <= absZ
                    ? new float3(0f, 1f, 0f)
                    : new float3(0f, 0f, 1f));
        }

        private readonly struct FaceCut : IComparable<FaceCut>
        {
            public readonly float2 Normal;
            public readonly float Angle;
            public readonly float Distance;

            public FaceCut(float2 normal, float distance) => (Normal, Angle, Distance) = (normal, math.atan2(normal.y, normal.x), distance);

            public int CompareTo(FaceCut other)
            {
                return Angle.CompareTo(other.Angle);
            }
        }

        private void AddCut(List<FaceCut> cuts, BspPlane plane, BspNode cutNode, bool positive, float3 origin, float3 tu, float3 tv)
        {
            var cutPlane = _planes[cutNode.PlaneIndex];

            if (!positive)
            {
                // cutPlane = -cutPlane;
            }

            var cutTangent = math.cross(plane.Normal, cutPlane.Normal);
            if (math.lengthsq(cutTangent) <= 0.0000001f) return;

            var cutNormal = math.cross(cutTangent, plane.Normal);

            cutNormal = math.normalizesafe(cutNormal);

            var cutNormal2 = new float2(
                math.dot(cutNormal, tu),
                math.dot(cutNormal, tv));

            cutNormal2 = math.normalizesafe(cutNormal2);

            var denom = math.dot(cutPlane.Normal, cutNormal);
            if (math.abs(denom) <= 0.0001f) return;

            var t = math.dot(cutPlane.Normal * cutPlane.Offset - origin, cutPlane.Normal) / denom;
            cuts.Add(new FaceCut(cutNormal2, t));
        }

        private static float Cross(float2 a, float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        [ThreadStatic]
        private static List<FaceCut> _sFaceCuts;

        private void HandleChildCuts(Queue<uint> paths, List<FaceCut> cuts, BspPlane plane, float3 origin, float3 tu, float3 tv, BspNode child, uint path, ref int pathIndex)
        {
            while (true)
            {
                bool positive;

                if (!BspNode.IsLeafIndex(child.NegativeIndex) && !BspNode.IsLeafIndex(child.PositiveIndex))
                {
                    positive = ((path >> pathIndex) & 1) == 1;

                    if (!positive)
                    {
                        paths.Enqueue(path | (uint)(1 << pathIndex));
                    }

                    ++pathIndex;
                }
                else
                {
                    positive = !BspNode.IsLeafIndex(child.PositiveIndex);
                }

                AddCut(cuts, plane, child, positive, origin, tu, tv);

                var nextIndex = positive ? child.PositiveIndex : child.NegativeIndex;

                if (BspNode.IsLeafIndex(nextIndex)) break;

                child = _nodes[nextIndex];
            }
        }

        private void TriangulateFace(Queue<uint> paths, List<Vector3> vertices, List<Vector3> normals, List<int> indices, ushort index, uint path)
        {
            var node = _nodes[index];
            var plane = _planes[node.PlaneIndex];

            var tu = math.normalizesafe(GetTangent(plane.Normal));
            var tv = math.normalizesafe(math.cross(tu, plane.Normal));

            var cuts = _sFaceCuts ?? (_sFaceCuts = new List<FaceCut>());
            var origin = plane.Normal * plane.Offset;

            var normal = plane.Normal * (node.PositiveIndex == BspNode.InIndex ? 1f : -1f);

            cuts.Clear();
            
            var parent = node;
            var prevIndex = index;

            while (parent.ParentIndex != BspNode.NullParentIndex)
            {
                var curIndex = parent.ParentIndex;
                parent = _nodes[parent.ParentIndex];

                AddCut(cuts, plane, parent, parent.PositiveIndex == prevIndex, origin, tu, tv);

                prevIndex = curIndex;
            }

            var pathIndex = 0;

            if (!BspNode.IsLeafIndex(node.NegativeIndex))
            {
                HandleChildCuts(paths, cuts, plane, origin, tu, tv, _nodes[node.NegativeIndex], path, ref pathIndex);
            }

            if (!BspNode.IsLeafIndex(node.PositiveIndex))
            {
                HandleChildCuts(paths, cuts, plane, origin, tu, tv, _nodes[node.PositiveIndex], path, ref pathIndex);
            }

            if (cuts.Count < 3) return;

            cuts.Sort();

            var firstIndex = vertices.Count;

            for (var i = 0; i < cuts.Count; ++i)
            {
                var cutA = cuts[i];
                var p0 = cutA.Normal * cutA.Distance;

                var min = float.NegativeInfinity;
                var max = float.PositiveInfinity;

                for (var j = 1; j < cuts.Count; ++j)
                {
                    var cutB = cuts[(i + j) % cuts.Count];
                    var cross = Cross(cutA.Normal, cutB.Normal);

                    if (math.abs(cross) <= 0.0001f)
                    {
                        if (cutA.Distance * math.dot(cutA.Normal, cutB.Normal) < cutB.Distance)
                        {
                            min = float.PositiveInfinity;
                            max = float.NegativeInfinity;
                        }

                        continue;
                    }

                    var p1 = cutB.Normal * cutB.Distance;

                    var along = math.dot(p1 - p0, cutB.Normal) / cross;

                    if (cross > 0f)
                    {
                        min = math.max(min, along);
                    }
                    else
                    {
                        max = math.min(max, along);
                    }
                }

                if (max < min) continue;

                var vert = p0 + new float2(-cutA.Normal.y, cutA.Normal.x) * max;

                vertices.Add(origin + vert.x * tu + vert.y * tv);
                normals.Add(normal);
            }

            for (var i = firstIndex + 2; i < vertices.Count; ++i)
            {
                indices.Add(firstIndex);
                indices.Add(i - 1);
                indices.Add(i);
            }

            return;
        }

        public void Dispose()
        {
            if (_planes.IsCreated)
            {
                _planes.Dispose();
            }

            _planeCount = 0;

            if (_nodes.IsCreated)
            {
                _nodes.Dispose();
            }

            _nodeCount = 0;
        }
    }
}
