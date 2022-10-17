using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CsgTest
{
    public class Grenade : MonoBehaviour
    {
        public float SubtractSize = 2.5f;

        void OnCollisionEnter(Collision collision)
        {
            var polyDemo = collision.gameObject.GetComponent<PolyhedronDemo>();
            if (polyDemo == null) return;
            
            var worldToLocal = polyDemo.transform.worldToLocalMatrix;
            var position = collision.GetContact(0).point;
            var rotation = UnityEngine.Random.rotationUniform;
            var localScale = new Vector3(UnityEngine.Random.value * 0.125f + 1f,
                UnityEngine.Random.value * 0.125f + 1f,
                UnityEngine.Random.value * 0.125f + 1f) * SubtractSize;

            var randomA = new Unity.Mathematics.Random( (uint) new System.Random().Next() );
            var randomB = randomA;

            var mesh = ConvexPolyhedron.CreateDodecahedron(float3.zero, 0.5f, ref randomA, 1f);
            mesh.Transform(math.mul(worldToLocal, float4x4.TRS(position, rotation, localScale)));
            polyDemo.Combine(mesh, BrushOperator.Subtract);

            mesh = ConvexPolyhedron.CreateDodecahedron(float3.zero, 0.55f, ref randomB, 1f);
            mesh.MaterialIndex = 1;
            mesh.Transform(math.mul(worldToLocal, float4x4.TRS(position, rotation, localScale)));
            polyDemo.Combine(mesh, BrushOperator.Paint);

            var ps = GetComponent<ParticleSystem>();

            ps.Play();

            Destroy(GetComponent<Rigidbody>());
            Destroy(GetComponent<Collider>());
            Destroy(transform.GetChild(0).gameObject);

            StartCoroutine(DestroyAfterTime());
        }

        IEnumerator DestroyAfterTime()
        {
            yield return new WaitForSeconds(2f);
            Destroy(gameObject);
        }
    }
}
