using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace CsgTest
{
    public static class Helpers
    {
        public static float3 GetTangent(this float3 normal)
        {
            var absX = math.abs(normal.x);
            var absY = math.abs(normal.y);
            var absZ = math.abs(normal.z);

            return math.cross(normal, absX <= absY && absX <= absZ
                ? new float3(1f, 0f, 0f) : absY <= absZ
                    ? new float3(0f, 1f, 0f)
                    : new float3(0f, 0f, 1f));
        }

        public static float Cross(float2 a, float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        public static void EnsureCapacity<T>(ref NativeArray<T> array, int minSize)
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
    }
}
