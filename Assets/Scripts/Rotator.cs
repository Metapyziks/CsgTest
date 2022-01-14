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
            transform.localRotation = Quaternion.Euler(0f, Speed * Time.deltaTime, 0f) * transform.localRotation;
        }
    }
}
