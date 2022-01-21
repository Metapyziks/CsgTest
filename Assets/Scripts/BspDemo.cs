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
        private MeshCollider _collider;

        // [Multiline(32)]
        // public string value;

        private bool _geometryInvalid;
        private bool _meshInvalid;

        void OnEnable()
        {
            _solid?.Dispose();
            _cube?.Dispose();

            _solid = new BspSolid();
            _cube = BspSolid.CreateBox(float3.zero, new float3(1f, 1f, 1f));

            if (_mesh == null)
            {
                _mesh = new Mesh
                {
                    hideFlags = HideFlags.DontSave
                };

                _mesh.MarkDynamic();

                GetComponent<MeshFilter>().sharedMesh = _mesh;

                _collider = GetComponent<MeshCollider>();

                if (_collider != null)
                {
                    _collider.sharedMesh = _mesh;
                }
            }

            _geometryInvalid = true;
        }

        public void Subtract(float4x4 matrix)
        {
            _solid.Merge(_cube, CsgOperator.Subtract, matrix);

            _meshInvalid = true;
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

#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying)
            {
                _geometryInvalid = true;
            }
#endif

            if (_geometryInvalid)
            {
                _geometryInvalid = false;

                _solid.Clear();

                foreach (var brush in transform.GetComponentsInChildren<CsgBrush>())
                {
                    _solid.Merge(_cube, brush.Type == BrushType.Add ? CsgOperator.Or : CsgOperator.Subtract,
                        brush.transform.localToWorldMatrix);
                }

                // value = _solid.ToString();

                _meshInvalid = true;
            }

            if (_meshInvalid)
            {
                _meshInvalid = false;
                _solid.WriteToMesh(_mesh);

                if (_collider != null)
                {
                    _collider.sharedMesh = _mesh;
                }
            }
        }
    }
}
