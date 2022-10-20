using UnityEngine;

namespace CsgTest.Geometry
{
    public enum BrushOperator
    {
        Add,
        Subtract,
        Replace,
        Paint
    }

    public enum Primitive
    {
        Cube,
        Dodecahedron,
        Mesh
    }

    public class CsgBrush : MonoBehaviour
    {
        public BrushOperator Operator;
        public Primitive Primitive;
        public int MaterialIndex;

        private void OnValidate()
        {
            if (GetComponent<MeshFilter>() != null)
            {
                Primitive = Primitive.Mesh;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Operator == BrushOperator.Add ? Color.blue : Color.yellow;

            switch (Primitive)
            {
                case Primitive.Mesh:
                {
                    var meshFilter = GetComponent<MeshFilter>();
                    if (meshFilter == null) return;

                    Gizmos.DrawWireMesh(meshFilter.sharedMesh);
                    break;
                }
                case Primitive.Cube:
                    Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
                    break;
                default:
                    Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
                    break;
            }
        }
    }
}
