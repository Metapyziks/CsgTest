using System;
using Unity.Collections;
using Unity.Mathematics;

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

        private ushort InsertSubtree(NativeArray<BspNode> nodes, int count, ushort parentIndex, int planeIndexOffset, ushort outValue, ushort inValue)
        {
            if (outValue == inValue) return outValue;

            var nodeIndexOffset = _nodeCount;

            Helpers.EnsureCapacity(ref _nodes, _nodeCount + count);

            for (var i = 0; i < count; ++i)
            {
                _nodes[nodeIndexOffset + i] = nodes[i].Inserted(parentIndex, nodeIndexOffset, planeIndexOffset, outValue, inValue);
            }

            _nodeCount += count;

            return (ushort)nodeIndexOffset;
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
    }
}
