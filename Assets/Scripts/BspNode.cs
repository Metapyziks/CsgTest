using Unity.Collections;

namespace CsgTest
{
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
            return new BspNode((ushort)(PlaneIndex + planeIndexOffset),
                ParentIndex == NullParentIndex ? rootParentIndex : (ushort)(ParentIndex + nodeIndexOffset),
                IsLeafIndex(NegativeIndex) ? NegativeIndex == OutIndex ? outValue : inValue : (ushort)(NegativeIndex + nodeIndexOffset),
                IsLeafIndex(PositiveIndex) ? PositiveIndex == OutIndex ? outValue : inValue : (ushort)(PositiveIndex + nodeIndexOffset));
        }

        public BspNode Remapped(ushort planeIndex, NativeArray<ushort> nodeRemapDict)
        {
            return new BspNode(planeIndex,
                ParentIndex == NullParentIndex ? NullParentIndex : nodeRemapDict[ParentIndex],
                IsLeafIndex(NegativeIndex) ? NegativeIndex : nodeRemapDict[NegativeIndex],
                IsLeafIndex(PositiveIndex) ? PositiveIndex : nodeRemapDict[PositiveIndex]);
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
}
