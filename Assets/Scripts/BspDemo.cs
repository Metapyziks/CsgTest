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

        public List<Transform> CutTransforms;

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

                _mesh.MarkDynamic();

                GetComponent<MeshFilter>().sharedMesh = _mesh;
            }
        }

        void OnDisable()
        {
            _solid?.Dispose();
            _solid = null;
        }

        void Update()
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

            if (CutTransforms != null)
            {
                var normTransform = transform.worldToLocalMatrix.inverse.transpose;

                foreach (var cutTransform in CutTransforms)
                {
                    if (cutTransform == null) continue;

                    var forward = cutTransform.forward;

                    var normal = (Vector3) (normTransform * new Vector4(forward.x, forward.y, forward.z, 0f));
                    var position = transform.InverseTransformPoint(cutTransform.position);

                    _solid.Cut(new BspPlane(normal, position));
                }
            }

            _solid.WriteToMesh(_mesh);
        }
    }
}
