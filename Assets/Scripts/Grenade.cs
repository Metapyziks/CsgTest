using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

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

            var mesh = ConvexPolyhedron.CreateDodecahedron(float3.zero, 0.5f);
            mesh.MaterialIndex = 1;
            mesh.Transform(math.mul(worldToLocal, float4x4.TRS(position, rotation, localScale * 1.1f)));
            polyDemo.Combine(mesh, BrushOperator.Replace);

            mesh = ConvexPolyhedron.CreateDodecahedron(float3.zero, 0.5f);
            mesh.Transform(math.mul(worldToLocal, float4x4.TRS(position, rotation, localScale)));
            polyDemo.Combine(mesh, BrushOperator.Subtract);

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
