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
        private BspSolid _dodecahedron;
        private Mesh _mesh;

        public List<Transform> CutTransforms;
        public Transform UnionCubeTransform;

        public CsgOperator Operator = CsgOperator.Or;

        void OnEnable()
        {
            _solid?.Dispose();
            _cube?.Dispose();

            _solid = new BspSolid();
            _cube = BspSolid.CreateBox(float3.zero, new float3(1f, 1f, 1f));
            _dodecahedron = BspSolid.CreateDodecahedron(float3.zero, 0.5f);

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

            _dodecahedron?.Dispose();
            _dodecahedron = null;
        }

        void Update()
        {
            if (_solid == null) return;

            _solid.Clear();
            _solid.Merge(_dodecahedron, CsgOperator.Or);

            if (UnionCubeTransform != null)
            {
                _solid.Merge(_cube, Operator, transform.worldToLocalMatrix * UnionCubeTransform.localToWorldMatrix);
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
