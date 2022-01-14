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
        private BspSolid _cube;
        private Mesh _mesh;

        public List<Transform> CutTransforms;
        public Transform UnionCubeTransform;

        void OnEnable()
        {
            _solid?.Dispose();
            _cube?.Dispose();

            _solid = new BspSolid();
            _cube = BspSolid.CreateCube(float3.zero, new float3(1f, 1f, 1f));

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

            _cube?.Dispose();
            _cube = null;
        }

        void Update()
        {
            if (_solid == null) return;

            _solid.Clear();
            _solid.Union(_cube, float4x4.identity);

            if (UnionCubeTransform != null)
            {
                _solid.Union(_cube, transform.worldToLocalMatrix * UnionCubeTransform.localToWorldMatrix);
            }

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
