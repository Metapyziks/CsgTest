using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CsgTest
{
    public class Rotator : MonoBehaviour
    {
        public float Speed = 90f;

        void Update()
        {
            transform.Rotate(Vector3.up, Speed * Time.deltaTime);
        }
    }
}
