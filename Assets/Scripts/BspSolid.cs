using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Codice.CM.Client.Differences;
using Unity.Collections;
using UnityEngine;

using Unity.Mathematics;

namespace CsgTest
{
    public enum CsgOperator
    {
        Source = 0b1100,
        Target = 0b1010,

        Or = Source | Target,
        And = Source & Target,
        Xor = Source ^ Target,
        Subtract = Target & ~Source
    }

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

        public BspNode Inserted(ushort rootParentIndex, int nodeIndexOffset, int planeIndexOffset, ushort outValue, ushort inValue)
        {
            return new BspNode((ushort) (PlaneIndex + planeIndexOffset),
                ParentIndex == NullParentIndex ? rootParentIndex : (ushort) (ParentIndex + nodeIndexOffset),
                IsLeafIndex(NegativeIndex) ? NegativeIndex == OutIndex ? outValue : inValue : (ushort)(NegativeIndex + nodeIndexOffset),
                IsLeafIndex(PositiveIndex) ? PositiveIndex == OutIndex ? outValue : inValue : (ushort)(PositiveIndex + nodeIndexOffset));
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

            Reduce();
        }

        private ushort InsertSubtree(NativeArray<BspNode> nodes, int count, ushort parentIndex, int planeIndexOffset, ushort outValue, ushort inValue)
        {
            if (outValue == inValue) return outValue;

            var nodeIndexOffset = _nodeCount;

            Resize(ref _nodes, _nodeCount + count);

            for (var i = 0; i < count; ++i)
            {
                _nodes[nodeIndexOffset + i] = nodes[i].Inserted(parentIndex, nodeIndexOffset, planeIndexOffset, outValue, inValue);
            }

            _nodeCount += count;

            return (ushort)nodeIndexOffset;
        }

        private static (ushort outValue, ushort inValue) GetLeafValues(CsgOperator op, bool parentLeafIn)
        {
            var outKey = parentLeafIn ? 1 : 0;
            var inKey = outKey | 2;

            return ((((int) op >> outKey) & 1) == 0 ? BspNode.OutIndex : BspNode.InIndex,
                (((int)op >> inKey) & 1) == 0 ? BspNode.OutIndex : BspNode.InIndex);
        }

        public void Combine(BspSolid solid, CsgOperator op, float4x4? transform = null)
        {
            var planeIndexOffset = _planeCount;

            Resize(ref _planes, _planeCount + solid._planeCount);

            NativeArray<BspPlane>.Copy(solid._planes, 0, _planes, planeIndexOffset, solid._planeCount);

            _planeCount += solid._planeCount;

            if (transform != null)
            {
                var normalTransform = math.transpose(math.inverse(transform.Value));

                for (var i = planeIndexOffset; i < _planeCount; ++i)
                {
                    _planes[i] = _planes[i].Transform(transform.Value, normalTransform);
                }
            }

            if (_nodeCount == 0)
            {
                var (outValue, inValue) = GetLeafValues(op, false);
                InsertSubtree(solid._nodes, solid._nodeCount, BspNode.NullParentIndex, planeIndexOffset, outValue, inValue);
                return;
            }

            var oldNodeCount = _nodeCount;

            for (ushort i = 0; i < oldNodeCount; ++i)
            {
                var node = _nodes[i];

                if (BspNode.IsLeafIndex(node.NegativeIndex))
                {
                    var (outValue, inValue) = GetLeafValues(op, node.NegativeIndex == BspNode.InIndex);
                    node = node.WithNegativeIndex(InsertSubtree(solid._nodes, solid._nodeCount, i, planeIndexOffset, outValue, inValue));
                }

                if (BspNode.IsLeafIndex(node.PositiveIndex))
                {
                    var (outValue, inValue) = GetLeafValues(op, node.PositiveIndex == BspNode.InIndex);
                    node = node.WithPositiveIndex(InsertSubtree(solid._nodes, solid._nodeCount, i, planeIndexOffset, outValue, inValue));
                }

                _nodes[i] = node;
            }

            Reduce();
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

            var pathSet = new HashSet<uint>();
            var paths = new Queue<uint>();

            vertices.Clear();
            normals.Clear();
            indices.Clear();

            var writer = new StringWriter();

            for (ushort i = 0; i < _nodeCount; ++i)
            {
                paths.Clear();
                pathSet.Clear();

                paths.Enqueue(0u);

                writer.WriteLine(i);

                while (paths.Count > 0)
                {
                    var path = paths.Dequeue();

                    if (!pathSet.Add(path)) continue;

                    writer.WriteLine($"  {Convert.ToString(path, 2)}");

                    TriangulateFace(paths, vertices, normals, indices, i, path);
                }
            }

            Debug.Log(writer);

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

        private struct FaceCut : IComparable<FaceCut>
        {
            public static FaceCut ExcludeAll => new FaceCut(new float2(-1f, 0f),
                float.PositiveInfinity, float.NegativeInfinity, float.PositiveInfinity);

            public static FaceCut ExcludeNone => new FaceCut(new float2(1f, 0f),
                float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity);

            public static FaceCut operator -(FaceCut cut)
            {
                return new FaceCut(-cut.Normal, -cut.Distance, -cut.Max, -cut.Min);
            }

            public readonly float2 Normal;
            public readonly float Angle;
            public readonly float Distance;

            public float Min;
            public float Max;

            public bool ExcludesAll => float.IsPositiveInfinity(Distance);
            public bool ExcludesNone => float.IsNegativeInfinity(Distance);

            public FaceCut(float2 normal, float distance, float min, float max) => (Normal, Angle, Distance, Min, Max) = (normal, math.atan2(normal.y, normal.x), distance, min, max);

            public int CompareTo(FaceCut other)
            {
                return Angle.CompareTo(other.Angle);
            }
        }

        private FaceCut GetFaceCut(BspPlane plane, BspPlane cutPlane, float3 origin, float3 tu, float3 tv)
        {
            var cutTangent = math.cross(plane.Normal, cutPlane.Normal);

            if (math.lengthsq(cutTangent) <= 0.0000001f)
            {
                // If this cut completely excludes the original plane, return a FaceCut that also excludes everything

                return plane.Offset * math.dot(plane.Normal, cutPlane.Normal) > cutPlane.Offset + 0.0001f
                    ? FaceCut.ExcludeNone
                    : FaceCut.ExcludeAll;
            }

            var cutNormal = math.cross(cutTangent, plane.Normal);

            cutNormal = math.normalizesafe(cutNormal);

            var cutNormal2 = new float2(
                math.dot(cutNormal, tu),
                math.dot(cutNormal, tv));

            cutNormal2 = math.normalizesafe(cutNormal2);

            var t = math.dot(cutPlane.Normal * cutPlane.Offset - origin, cutPlane.Normal)
                    / math.dot(cutPlane.Normal, cutNormal);

            return new FaceCut(cutNormal2, t, float.NegativeInfinity, float.PositiveInfinity);
        }

        private (bool ExcludesNone, bool ExcludesAll) GetNewFaceCutExclusions(List<FaceCut> cuts, FaceCut cut)
        {
            if (cut.ExcludesAll)
            {
                return (false, true);
            }

            if (cut.ExcludesNone)
            {
                return (true, false);
            }

            var p0 = cut.Normal * cut.Distance;

            var min = float.NegativeInfinity;
            var max = float.PositiveInfinity;

            var anyIntersections = false;
            var excludesNone = true;
            var excludedCutCount = 0;

            foreach (var other in cuts)
            {
                var cross = Cross(cut.Normal, other.Normal);

                if (math.abs(cross) <= 0.0001f)
                {
                    var dot = math.dot(cut.Normal, other.Normal);

                    if (other.Distance * dot < cut.Distance)
                    {
                        if (cut.Distance * dot < other.Distance)
                        {
                            return (false, true);
                        }

                        excludesNone = false;
                    }

                    if (cut.Distance * dot < other.Distance)
                    {
                        return (true, false);
                    }

                    continue;
                }

                anyIntersections = true;

                var p1 = other.Normal * other.Distance;
                var proj0 = math.dot(p1 - p0, other.Normal) / cross;
                var proj1 = math.dot(p0 - p1, cut.Normal) / -cross;

                if (cross > 0f)
                {
                    min = math.max(min, proj0);

                    if (proj1 < other.Min)
                    {
                        excludesNone = false;
                        ++excludedCutCount;
                    }
                    else if (proj1 < other.Max)
                    {
                        excludesNone = false;
                    }
                }
                else
                {
                    max = math.min(max, proj0);

                    if (proj1 > other.Max)
                    {
                        excludesNone = false;
                        ++excludedCutCount;
                    }
                    else if (proj1 > other.Min)
                    {
                        excludesNone = false;
                    }
                }
            }

            return (anyIntersections && excludesNone, anyIntersections && excludedCutCount == cuts.Count);
        }

        private void AddFaceCut(List<FaceCut> cuts, FaceCut cut)
        {
            var p0 = cut.Normal * cut.Distance;

            for (var i = cuts.Count - 1; i >= 0; --i)
            {
                var other = cuts[i];
                var cross = Cross(cut.Normal, other.Normal);

                if (math.abs(cross) <= 0.0001f)
                {
                    var dot = math.dot(cut.Normal, other.Normal);

                    if (other.Distance * dot < cut.Distance)
                    {
                        if (cut.Distance * dot < other.Distance)
                        {
                            throw new Exception();
                        }

                        cuts.RemoveAt(i);
                        continue;
                    }

                    if (cut.Distance * dot < other.Distance)
                    {
                        cut.Min = float.PositiveInfinity;
                        cut.Max = float.NegativeInfinity;
                    }

                    continue;
                }

                var p1 = other.Normal * other.Distance;
                var proj0 = math.dot(p1 - p0, other.Normal) / cross;
                var proj1 = math.dot(p0 - p1, cut.Normal) / -cross;

                if (cross > 0f)
                {
                    cut.Min = math.max(cut.Min, proj0);
                    other.Max = math.min(other.Max, proj1);
                }
                else
                {
                    cut.Max = math.min(cut.Max, proj0);
                    other.Min = math.max(other.Min, proj1);
                }

                if (other.Min >= other.Max)
                {
                    cuts.RemoveAt(i);
                }
                else
                {
                    cuts[i] = other;
                }
            }

            if (cut.Min < cut.Max)
            {
                cuts.Add(cut);
            }
        }

        private static float Cross(float2 a, float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        [ThreadStatic]
        private static List<FaceCut> _sFaceCuts;
        
        private (bool, ushort) HandleChildCuts(Queue<uint> paths, List<FaceCut> cuts, BspPlane plane, float3 origin, float3 tu, float3 tv, BspNode child, uint path, ref int pathIndex)
        {
            while (true)
            {
                bool positive;

                var childPlane = _planes[child.PlaneIndex];
                var positiveCut = GetFaceCut(plane, childPlane, origin, tu, tv);
                var negativeCut = -positiveCut;

                var (negExcludesNone, negExcludesAll) = GetNewFaceCutExclusions(cuts, negativeCut);
                var (posExcludesNone, posExcludesAll) = GetNewFaceCutExclusions(cuts, positiveCut);

                if (negExcludesAll && posExcludesAll)
                {
                    return (false, default);
                }

                if (!negExcludesNone && !posExcludesNone && !negExcludesAll && !posExcludesAll)
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
                    positive = negExcludesAll;
                }

                if (positive && !posExcludesNone || !positive && !negExcludesNone)
                {
                    AddFaceCut(cuts, positive ? positiveCut : negativeCut);
                }

                var nextIndex = positive ? child.PositiveIndex : child.NegativeIndex;

                if (BspNode.IsLeafIndex(nextIndex))
                {
                    return (true, nextIndex);
                }

                child = _nodes[nextIndex];
            }
        }

        private void TriangulateFace(Queue<uint> paths, List<Vector3> vertices, List<Vector3> normals, List<int> indices, ushort index, uint path)
        {
            var node = _nodes[index];

            if (node.NegativeIndex == node.PositiveIndex && BspNode.IsLeafIndex(node.NegativeIndex)) return;

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
                parent = _nodes[curIndex];

                var cutPlane = _planes[parent.PlaneIndex];
                var faceCut = GetFaceCut(plane, parent.PositiveIndex == prevIndex ? cutPlane : -cutPlane, origin, tu, tv);
                var (excludesNone, excludesAll) = GetNewFaceCutExclusions(cuts, faceCut);

                if (excludesAll) return;
                if (!excludesNone) AddFaceCut(cuts, faceCut);

                prevIndex = curIndex;
            }

            var pathIndex = 0;
            var negativeLeaf = node.NegativeIndex;
            var positiveLeaf = node.PositiveIndex;

            bool valid;

            if (!BspNode.IsLeafIndex(negativeLeaf))
            {
                (valid, negativeLeaf) = HandleChildCuts(paths, cuts, plane, origin, tu, tv, _nodes[negativeLeaf], path, ref pathIndex);
                if (!valid) return;
            }

            if (!BspNode.IsLeafIndex(positiveLeaf))
            {
                (valid, positiveLeaf) = HandleChildCuts(paths, cuts, plane, origin, tu, tv, _nodes[positiveLeaf], path, ref pathIndex);
                if (!valid) return;
            }

            if (negativeLeaf == positiveLeaf) return;

            for (var i = cuts.Count - 1; i >= 0; --i)
            {
                var cut = cuts[i];

                if (float.IsNegativeInfinity(cut.Min) || float.IsPositiveInfinity(cut.Max))
                {
                    return;
                }

                if (cut.Max <= cut.Min)
                {
                    cuts.RemoveAt(i);
                }
            }

            if (cuts.Count == 0) return;

            cuts.Sort();

            var firstIndex = vertices.Count;

            if (negativeLeaf == BspNode.InIndex)
            {
                normal = -normal;
            }

            for (var i = 0; i < cuts.Count; ++i)
            {
                var cut = cuts[i];
                var p0 = cut.Normal * cut.Distance;

                var vert = p0 + new float2(-cut.Normal.y, cut.Normal.x) * cut.Max;

                vertices.Add(origin + vert.x * tu + vert.y * tv);
                normals.Add(normal);
            }

            if (negativeLeaf == BspNode.InIndex)
            {
                for (var i = firstIndex + 2; i < vertices.Count; ++i)
                {
                    indices.Add(firstIndex);
                    indices.Add(i);
                    indices.Add(i - 1);
                }
            }
            else
            {
                for (var i = firstIndex + 2; i < vertices.Count; ++i)
                {
                    indices.Add(firstIndex);
                    indices.Add(i - 1);
                    indices.Add(i);
                }
            }
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
