using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace CsgTest
{
    public enum BrushOperator
    {
        Add,
        Subtract
    }

    public enum Primitive
    {
        Cube,
        Dodecahedron
    }

    public class CsgBrush : MonoBehaviour
    {
        public BrushOperator Operator;
        public Primitive Primitive;

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Operator == BrushOperator.Add ? Color.blue : Color.yellow;

            if (Primitive == Primitive.Cube)
            {
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            }
            else
            {
                Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
            }
        }
    }
}
