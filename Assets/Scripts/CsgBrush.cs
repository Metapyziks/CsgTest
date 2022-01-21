using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace CsgTest
{
    public enum BrushType
    {
        Add,
        Subtract
    }

    public class CsgBrush : MonoBehaviour
    {
        public BrushType Type;

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Type == BrushType.Add ? Color.blue : Color.yellow;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }
    }
}
