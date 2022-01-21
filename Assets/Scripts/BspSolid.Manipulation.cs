using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

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

                    return;
                }
            }

            if (_nodeCount == 0)
            {
                // Should be already handled
                throw new Exception();
            }

            var planeIndexOffset = _planeCount;

            Helpers.EnsureCapacity(ref _planes, _planeCount + solid._planeCount);

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

            var planes = _sMergePlanes ?? (_sMergePlanes = new Stack<BspPlane>());

            planes.Clear();

            Merge(planes, 0, solid, op, planeIndexOffset);
            
            Reduce();
        }

        [ThreadStatic] private static Stack<BspPlane> _sMergePlanes;

        private void Merge(Stack<BspPlane> planes, ushort nodeIndex, BspSolid solid, CsgOperator op, int planeIndexOffset)
        {
            var node = _nodes[nodeIndex];
            var plane = _planes[node.PlaneIndex];

            planes.Push(-plane);

            if (BspNode.IsLeafIndex(node.NegativeIndex))
            {
                var (outValue, inValue) = GetLeafValues(op, node.NegativeIndex == BspNode.InIndex);
                node = node.WithNegativeIndex(InsertSubtree(planes, nodeIndex, solid._nodes, 0, planeIndexOffset, outValue, inValue));
            }
            else
            {
                Merge(planes, node.NegativeIndex, solid, op, planeIndexOffset);
            }

            planes.Pop();
            planes.Push(plane);

            if (BspNode.IsLeafIndex(node.PositiveIndex))
            {
                var (outValue, inValue) = GetLeafValues(op, node.PositiveIndex == BspNode.InIndex);
                node = node.WithPositiveIndex(InsertSubtree(planes, nodeIndex, solid._nodes, 0, planeIndexOffset, outValue, inValue));
            }
            else
            {
                Merge(planes, node.PositiveIndex, solid, op, planeIndexOffset);
            }

            planes.Pop();

            _nodes[nodeIndex] = node;
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
                    // TODO
                    return (false, false);
                }

                if (excludesNegative) continue;

                faceCuts.AddFaceCut(cut);
            }

            var insidePoint = GetAnyPoint(faceCuts, origin, tu, tv);

            return math.dot(insidePoint, plane.Normal) > plane.Offset ? (true, false) : (false, true);
        }

        private ushort InsertSubtree(Stack<BspPlane> planes, ushort parentIndex, NativeArray<BspNode> nodes, int rootIndex, int planeIndexOffset, ushort outValue, ushort inValue)
        {
            if (outValue == inValue) return outValue;

            var node = nodes[rootIndex];
            var planeIndex = (ushort)(node.PlaneIndex + planeIndexOffset);
            var plane = _planes[planeIndex];

            var (excludesNegative, excludesPositive) = GetPlaneExclusions(planes, plane);

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

                return InsertSubtree(planes, parentIndex, nodes, node.NegativeIndex, planeIndexOffset, outValue, inValue);
            }

            if (excludesNegative)
            {
                if (BspNode.IsLeafIndex(node.PositiveIndex))
                {
                    return node.PositiveIndex == BspNode.OutIndex ? outValue : inValue;
                }

                return InsertSubtree(planes, parentIndex, nodes, node.PositiveIndex, planeIndexOffset, outValue, inValue);
            }

            var newIndex = AddNode(planeIndex, parentIndex, outValue, outValue);
            var newNode = _nodes[newIndex];

            if (BspNode.IsLeafIndex(node.NegativeIndex))
            {
                newNode = newNode.WithNegativeIndex(node.NegativeIndex == BspNode.OutIndex ? outValue : inValue);
            }
            else
            {
                planes.Push(-plane);
                newNode = newNode.WithNegativeIndex(InsertSubtree(planes, newIndex, nodes, node.NegativeIndex, planeIndexOffset, outValue, inValue));
                planes.Pop();
            }

            if (BspNode.IsLeafIndex(node.PositiveIndex))
            {
                newNode = newNode.WithPositiveIndex(node.PositiveIndex == BspNode.OutIndex ? outValue : inValue);
            }
            else
            {
                planes.Push(plane);
                newNode = newNode.WithPositiveIndex(InsertSubtree(planes, newIndex, nodes, node.PositiveIndex, planeIndexOffset, outValue, inValue));
                planes.Pop();
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
