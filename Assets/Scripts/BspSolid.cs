using System;
using System.Collections;
using System.Collections.Generic;
using Codice.CM.Client.Differences;
using Unity.Collections;
using UnityEngine;

using Unity.Mathematics;

namespace CsgTest
{
    public readonly struct BspPlane
    {
        public readonly float3 Normal;
        public readonly float3 Offset;

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
    }

    public readonly struct BspNode
    {
        public const ushort InIndex = 0;
        public const ushort OutIndex = ushort.MaxValue;

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

        public BspSolid()
        {

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
                AddNode(planeIndex, 0, BspNode.OutIndex, BspNode.InIndex);
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

        public void WriteToMesh(Mesh mesh)
        {
            mesh?.Clear();

            for (ushort i = 0; i < _nodeCount; ++i)
            {
                TriangulateFace(i);
            }
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

        private void AddCut(List<FaceCut> cuts, BspPlane plane, BspNode cutNode, float3 origin, float3 tu, float3 tv)
        {
            var cutPlane = _planes[cutNode.PlaneIndex];

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

            Gizmos.DrawLine(origin, origin + cutNormal * t);
        }

        private static float Cross(float2 a, float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private void TriangulateFace(ushort index)
        {
            var node = _nodes[index];
            var plane = _planes[node.PlaneIndex];

            var tu = math.normalizesafe(GetTangent(plane.Normal));
            var tv = math.normalizesafe(math.cross(tu, plane.Normal));

            var cuts = new List<FaceCut>();
            var origin = plane.Normal * plane.Offset;

            Gizmos.color = new Color(0.25f, 0.75f, 1f, 0.5f);

            if (node.ParentIndex != index)
            {
                var parentIndex = node.ParentIndex;

                while (true)
                {
                    var parent = _nodes[parentIndex];

                    AddCut(cuts, plane, parent, origin, tu, tv);

                    if (parent.ParentIndex == parentIndex) break;

                    parentIndex = parent.ParentIndex;
                }
            }

            var child = node;

            while (true)
            {
                if (!BspNode.IsLeafIndex(child.NegativeIndex) && !BspNode.IsLeafIndex(child.PositiveIndex))
                {
                    throw new NotImplementedException();
                }

                var nextIndex = !BspNode.IsLeafIndex(child.NegativeIndex) ? child.NegativeIndex : child.PositiveIndex;

                if (BspNode.IsLeafIndex(nextIndex)) break;

                child = _nodes[nextIndex];

                AddCut(cuts, plane, child, origin, tu, tv);
            }

            if (cuts.Count < 3) return;

            cuts.Sort();

            Gizmos.color = index == 6 ? Color.green : Color.red;

            var verts = new List<float2>();

            for (var i = 0; i < cuts.Count; ++i)
            {
                var cutA = cuts[i];
                var cutB = cuts[(i + 1) % cuts.Count];

                var cross = Cross(cutA.Normal, cutB.Normal);

                if (math.abs(cross) <= 0.0001f)
                {
                    return;
                }

                var p0 = cutA.Normal * cutA.Distance;
                var p1 = cutB.Normal * cutB.Distance;

                var along = math.dot(p1 - p0, cutB.Normal) / cross;
                var vert = p0 + new float2(-cutA.Normal.y, cutA.Normal.x) * along;

                var inside = true;

                for (var j = 2; j < cuts.Count; ++j)
                {
                    var otherCut = cuts[(i + j) % cuts.Count];

                    if (math.dot(otherCut.Normal, vert) < otherCut.Distance - 0.0001f)
                    {
                        Gizmos.DrawSphere(origin + vert.x * tu + vert.y * tv, 0.01f);

                        inside = false;
                        break;
                    }
                }

                if (inside)
                {
                    verts.Add(vert);
                }
            }

            if (verts.Count < 3) return;

            Gizmos.color = Color.white;

            for (var i = 0; i < verts.Count; ++i)
            {
                var a = verts[i];
                var b = verts[(i + 1) % verts.Count];

                Gizmos.DrawLine(origin + a.x * tu * 0.99f + a.y * tv * 0.99f, origin + b.x * tu * 0.99f + b.y * tv * 0.99f);
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
