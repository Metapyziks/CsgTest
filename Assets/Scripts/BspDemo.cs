using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace CsgTest
{
    [RequireComponent(typeof(MeshFilter)), ExecuteInEditMode]
    public class BspDemo : MonoBehaviour
    {
        private BspSolid _solid;
        private Mesh _mesh;

        public Transform cutTransform;
        
        void OnEnable()
        {
            _solid?.Dispose();
            _solid = new BspSolid();

            if (_mesh == null)
            {
                _mesh = new Mesh
                {
                    hideFlags = HideFlags.DontSave
                };

                GetComponent<MeshFilter>().sharedMesh = _mesh;
            }
        }

        void OnDisable()
        {
            _solid?.Dispose();
            _solid = null;
        }

        void OnDrawGizmos()
        {
            if (_solid == null) return;

            var min = new float3(-0.5f, -0.5f, -0.5f);
            var max = new float3(0.5f, 0.5f, 0.5f);

            _solid.Clear();

            _solid.Cut(new BspPlane(new float3(1f, 0f, 0f), min));
            _solid.Cut(new BspPlane(new float3(0f, 1f, 0f), min));
            _solid.Cut(new BspPlane(new float3(0f, 0f, 1f), min));
            _solid.Cut(new BspPlane(new float3(-1f, 0f, 0f), max));
            _solid.Cut(new BspPlane(new float3(0f, -1f, 0f), max));
            _solid.Cut(new BspPlane(new float3(0f, 0f, -1f), max));

            if (cutTransform != null)
            {
                _solid.Cut(new BspPlane(cutTransform.forward, cutTransform.position));
            }

            _solid.WriteToMesh(null);
        }
    }
}
