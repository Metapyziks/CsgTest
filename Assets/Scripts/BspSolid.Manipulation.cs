using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CsgTest
{
    public enum CsgOperator
    {
        First = 0b1010,
        Second = 0b1100,

        Or = First | Second,
        And = First & Second,
        Xor = First ^ Second,
        Subtract = First & ~Second
    }

    partial class BspSolid
    {
        public const float Epsilon = 0.000001f;

        public void Cut(BspPlane plane)
        {
            var (planeIndex, flipped) = AddPlane(plane);
            var negative = flipped ? BspNode.InIndex : BspNode.OutIndex;
            var positive = flipped ? BspNode.OutIndex : BspNode.InIndex;

            if (_nodeCount == 0)
            {
                AddNode(planeIndex, BspNode.NullParentIndex, negative, positive);
                return;
            }

            var oldNodeCount = _nodeCount;

            for (ushort i = 0; i < oldNodeCount; ++i)
            {
                var node = _nodes[i];

                if (node.NegativeIndex == BspNode.InIndex)
                {
                    node = node.WithNegativeIndex(AddNode(planeIndex, i, negative, positive));
                }

                if (node.PositiveIndex == BspNode.InIndex)
                {
                    node = node.WithPositiveIndex(AddNode(planeIndex, i, negative, positive));
                }

                _nodes[i] = node;
            }

            Reduce();
        }

        private static (ushort rhsOutValue, ushort rhsInValue) GetLeafValues(CsgOperator op, bool lhsIn)
        {
            var outKey = lhsIn ? 1 : 0;
            var inKey = outKey | 2;

            return ((((int)op >> outKey) & 1) == 0 ? BspNode.OutIndex : BspNode.InIndex,
                (((int)op >> inKey) & 1) == 0 ? BspNode.OutIndex : BspNode.InIndex);
        }

        public void Merge(BspSolid solid, CsgOperator op, float4x4? transform = null)
        {
            if (solid == this)
            {
                throw new NotImplementedException();
            }

            if (solid._nodeCount == 0)
            {
                op &= CsgOperator.First;
            }

            if (_nodeCount == 0)
            {
                op &= CsgOperator.Second;
            }

            switch (op)
            {
                case 0:
                    Clear();
                    return;

                case CsgOperator.First:
                    return;

                case CsgOperator.Second:
                {
                    Clear();

                    Helpers.EnsureCapacity(ref _planes, solid._planeCount);
                    Helpers.EnsureCapacity(ref _nodes, solid._nodeCount);

                    NativeArray<BspPlane>.Copy(solid._planes, 0, _planes, 0, solid._planeCount);
                    NativeArray<BspNode>.Copy(solid._nodes, 0, _nodes, 0, solid._nodeCount);

                    _planeCount = solid._planeCount;
                    _nodeCount = solid._nodeCount;

                    if (transform != null)
                    {
                        Transform(transform.Value);
                    }

                    _verticesValid = false;

                    return;
                }
            }

            if (_nodeCount == 0 || _planeCount == 0)
            {
                // Should be already handled
                throw new Exception();
            }

            solid.UpdateVertices();

            var leafPlanes = _sMergePlanes ?? (_sMergePlanes = new Stack<BspPlane>());

            leafPlanes.Clear();

            var planes = transform == null ? solid._planes : new NativeArray<BspPlane>(solid._planes, Allocator.Temp);
            var vertices = transform == null ? solid._vertices : new NativeArray<float3>(solid._vertices, Allocator.Temp);

            if (transform != null)
            {
                var normalTransform = math.transpose(math.inverse(transform.Value));

                for (var i = 0; i < planes.Length; ++i)
                {
                    planes[i] = planes[i].Transform(transform.Value, normalTransform);
                }

                for (var i = 0; i < vertices.Length; ++i)
                {
                    vertices[i] = math.transform(transform.Value, vertices[i]);
                }
            }

            Merge(leafPlanes, vertices, solid._vertexCount, 0, solid._nodes, 0, planes, op);

            if (transform != null)
            {
                planes.Dispose();
                vertices.Dispose();
            }

            Reduce();

            _verticesValid = false;
        }

        [ThreadStatic] private static Stack<BspPlane> _sMergePlanes;

        private ushort Merge(Stack<BspPlane> leafPlanes, NativeArray<float3> vertices, int vertexCount, ushort nodeIndex, NativeArray<BspNode> nodes, int rootIndex, NativeArray<BspPlane> planes, CsgOperator op)
        {
            var node = _nodes[nodeIndex];
            var plane = _planes[node.PlaneIndex];

            var anyNegative = false;
            var anyPositive = false;

            for (var i = 0; i < vertexCount; ++i)
            {
                var vertex = vertices[i];
                var dist = math.dot(plane.Normal, vertex) - plane.Offset;

                if (dist > Epsilon)
                {
                    anyPositive = true;
                    if (anyNegative) break;
                }
                else if (dist < -Epsilon)
                {
                    anyNegative = true;
                    if (anyPositive) break;
                }
            }

            if (anyNegative)
            {
                leafPlanes.Push(-plane);

                if (BspNode.IsLeafIndex(node.NegativeIndex))
                {
                    var (outValue, inValue) = GetLeafValues(op, node.NegativeIndex == BspNode.InIndex);
                    node = node.WithNegativeIndex(InsertSubtree(leafPlanes, nodeIndex, nodes, rootIndex, planes, outValue, inValue));
                }
                else
                {
                    node = node.WithNegativeIndex(Merge(leafPlanes, vertices, vertexCount, node.NegativeIndex, nodes, rootIndex, planes, op));
                }

                leafPlanes.Pop();
            }

            if (anyPositive)
            {
                leafPlanes.Push(plane);

                if (BspNode.IsLeafIndex(node.PositiveIndex))
                {
                    var (outValue, inValue) = GetLeafValues(op, node.PositiveIndex == BspNode.InIndex);
                    node = node.WithPositiveIndex(InsertSubtree(leafPlanes, nodeIndex, nodes, rootIndex, planes, outValue, inValue));
                }
                else
                {
                    node = node.WithPositiveIndex(Merge(leafPlanes, vertices, vertexCount, node.PositiveIndex, nodes, rootIndex, planes, op));
                }

                leafPlanes.Pop();
            }

            if (BspNode.IsLeafIndex(node.NegativeIndex) && node.NegativeIndex == node.PositiveIndex)
            {
                return node.NegativeIndex;
            }

            _nodes[nodeIndex] = node;
            return nodeIndex;
        }
        
        [ThreadStatic]
        private static List<FaceCut> _sFaceCuts;

        private float3 GetAnyPoint(List<FaceCut> cuts, float3 origin, float3 tu, float3 tv)
        {
            if (cuts.Count == 0)
            {
                return origin;
            }

            if (cuts.Count == 1)
            {
                return cuts[0].GetPoint(origin, tu, tv);
            }

            var point = float3.zero;

            foreach (var cut in cuts)
            {
                point += cut.GetPoint(origin, tu, tv);
            }

            return point / cuts.Count;
        }

        private (bool ExcludesNegative, bool ExcludesPositive) GetPlaneExclusions(Stack<BspPlane> planes, BspPlane plane)
        {
            var faceCuts = _sFaceCuts ?? (_sFaceCuts = new List<FaceCut>());

            faceCuts.Clear();

            var (origin, tu, tv) = plane.GetBasis();

            BspPlane excludingPlane = default;
            var excluded = false;

            foreach (var otherPlane in planes)
            {
                var cut = Helpers.GetFaceCut(plane, otherPlane, origin, tu, tv);

                if (cut.ExcludesAll)
                {
                    var aligned = math.dot(otherPlane.Normal, plane.Normal) > 0f;
                    return (aligned, !aligned);
                }

                var (excludesNegative, excludesPositive) = faceCuts.GetNewFaceCutExclusions(cut);

                if (excludesPositive)
                {
                    excluded = true;
                    excludingPlane = otherPlane;
                    break;
                }

                if (excludesNegative) continue;

                faceCuts.AddFaceCut(cut);
            }

            //faceCuts.Sort(FaceCut.Comparer);

            //foreach (var c in faceCuts)
            //{
            //    UnityEngine.Debug.DrawLine(c.GetPoint(origin, tu, tv, c.Min),
            //        c.GetPoint(origin, tu, tv, c.Max), excluded ? Color.red : Color.blue);
            //}

            if (!excluded)
            {
                return (false, false);
            }

            faceCuts.Clear();

            (origin, tu, tv) = excludingPlane.GetBasis();

            foreach (var otherPlane in planes)
            {
                if (otherPlane.Equals(excludingPlane))
                {
                    continue;
                }

                var cut = Helpers.GetFaceCut(excludingPlane, otherPlane, origin, tu, tv);
                var (excludesNegative, excludesPositive) = faceCuts.GetNewFaceCutExclusions(cut);

                if (excludesPositive)
                {
                    continue;
                    // TODO
                    Debug.Log("Hmm");

                    faceCuts.Sort(FaceCut.Comparer);

                    foreach (var c in faceCuts)
                    {
                        Debug.DrawLine(c.GetPoint(origin, tu, tv, c.Min),
                            c.GetPoint(origin, tu, tv, c.Max), Color.red);
                    }

                    Debug.DrawLine(cut.GetPoint(origin, tu, tv, -16f),
                        cut.GetPoint(origin, tu, tv, 16f), Color.blue);

                    Debug.DrawLine(plane.Normal * plane.Offset, plane.Normal * (plane.Offset + 0.25f), Color.green);

                    return (false, false);
                }

                if (excludesNegative) continue;

                faceCuts.AddFaceCut(cut);
            }

            var insidePoint = GetAnyPoint(faceCuts, origin, tu, tv);

            return math.dot(insidePoint, plane.Normal) > plane.Offset ? (true, false) : (false, true);
        }

        private ushort InsertSubtree(Stack<BspPlane> leafPlanes, ushort parentIndex, NativeArray<BspNode> nodes, int rootIndex, NativeArray<BspPlane> planes, ushort outValue, ushort inValue)
        {
            if (outValue == inValue) return outValue;

            var node = nodes[rootIndex];
            var plane = planes[node.PlaneIndex];

            var (excludesNegative, excludesPositive) = GetPlaneExclusions(leafPlanes, plane);

            if (excludesNegative && excludesPositive)
            {
                throw new Exception();
            }

            if (excludesPositive)
            {
                if (BspNode.IsLeafIndex(node.NegativeIndex))
                {
                    return node.NegativeIndex == BspNode.OutIndex ? outValue : inValue;
                }

                return InsertSubtree(leafPlanes, parentIndex, nodes, node.NegativeIndex, planes, outValue, inValue);
            }

            if (excludesNegative)
            {
                if (BspNode.IsLeafIndex(node.PositiveIndex))
                {
                    return node.PositiveIndex == BspNode.OutIndex ? outValue : inValue;
                }

                return InsertSubtree(leafPlanes, parentIndex, nodes, node.PositiveIndex, planes, outValue, inValue);
            }

            var (planeIndex, flipped) = AddPlane(plane);

            if (flipped)
            {
                plane = -plane;
                node = -node;
            }

            var newIndex = AddNode(planeIndex, parentIndex, outValue, outValue);
            var newNode = _nodes[newIndex];

            if (BspNode.IsLeafIndex(node.NegativeIndex))
            {
                newNode = newNode.WithNegativeIndex(node.NegativeIndex == BspNode.OutIndex ? outValue : inValue);
            }
            else
            {
                leafPlanes.Push(-plane);
                newNode = newNode.WithNegativeIndex(InsertSubtree(leafPlanes, newIndex, nodes, node.NegativeIndex, planes, outValue, inValue));
                leafPlanes.Pop();
            }

            if (BspNode.IsLeafIndex(node.PositiveIndex))
            {
                newNode = newNode.WithPositiveIndex(node.PositiveIndex == BspNode.OutIndex ? outValue : inValue);
            }
            else
            {
                leafPlanes.Push(plane);
                newNode = newNode.WithPositiveIndex(InsertSubtree(leafPlanes, newIndex, nodes, node.PositiveIndex, planes, outValue, inValue));
                leafPlanes.Pop();
            }

            if (BspNode.IsLeafIndex(newNode.NegativeIndex) && newNode.NegativeIndex == newNode.PositiveIndex)
            {
                return newNode.NegativeIndex;
            }

            _nodes[newIndex] = newNode;

            return newIndex;
        }
    }
}
