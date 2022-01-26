using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CsgTest
{
    [RequireComponent(typeof(MeshFilter)), ExecuteInEditMode]
    public class BspDemo : MonoBehaviour
    {
        private BspSolid _solid;
        private BspSolid _cube;
        private BspSolid _dodecahedron;

        private Mesh _mesh;
        private MeshCollider _collider;

        [Multiline(32)]
        public string value;

        private string _oldValue;

        public List<int> debugNodes;
        public bool debugDraw;

        private bool _geometryInvalid;
        private bool _meshInvalid;

        void OnEnable()
        {
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
            var timer = new Stopwatch();
            timer.Start();

            _solid.Merge(_dodecahedron, CsgOperator.Subtract, matrix);

            Debug.Log($"Merge: {timer.Elapsed.TotalMilliseconds:F3}ms");

            _meshInvalid = true;
        }

        void OnValidate()
        {
            if (_oldValue != value)
            {
                _oldValue = value;

                if (_solid != null && value != null)
                {
                    _solid.FromJson(value);
                    _meshInvalid = true;
                }
            }
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
                    _solid.Merge(
                        brush.Primitive == Primitive.Cube ? _cube : _dodecahedron,
                        brush.Operator == BrushOperator.Add ? CsgOperator.Or : CsgOperator.Subtract,
                        brush.transform.localToWorldMatrix);
                }

#if UNITY_EDITOR
                if (!UnityEditor.EditorApplication.isPlaying)
                {
                    UpdateValue();
                }
#endif

                _meshInvalid = true;
            }

            if (_meshInvalid)
            {
                _meshInvalid = false;

                var timer = new Stopwatch();

                timer.Start();

                _solid.WriteToMesh(_mesh);

                Debug.Log($"Mesh: {timer.Elapsed.TotalMilliseconds:F3}ms");

                if (_collider != null)
                {
                    _collider.sharedMesh = _mesh;
                }
            }
        }

        private void UpdateValue()
        {
            value = _solid.ToString();
            _oldValue = value;
        }

        void OnDrawGizmos()
        {
            if (debugDraw)
            {
                _solid.DrawDebugNodes(debugNodes ?? Enumerable.Empty<int>());
                UpdateValue();
            }
        }

        public void LogInfo()
        {
            _solid.LogInfo();
        }
    }
}
