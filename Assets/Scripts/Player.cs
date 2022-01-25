using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace CsgTest
{
    [RequireComponent(typeof(CharacterController))]
    public class Player : MonoBehaviour
    {
        private CharacterController _characterController;
        private Transform _eyes;

        public float JumpSpeed = 4f;

        public float MaxWalkSpeed = 2f;
        public float MaxRunSpeed = 4f;

        public float GroundAccel = 8f;
        public float AirAccel = 8f;

        public float GroundFriction = 0.95f;
        public float AirFriction = 0.1f;

        public float MouseSensitivity = 4f;

        public float FlareThrowSpeed = 8f;

        public float3 Velocity;
        public bool IsGrounded;

        public bool JumpPressed;

        public MeshFilter SubtractMesh;

        public GameObject FlarePrefab;

        void Start()
        {
            _characterController = GetComponent<CharacterController>();
            _eyes = transform.GetComponentInChildren<Camera>(true).transform;
        }

        public bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance)
        {
            _characterController.enabled = false;
            var result = Physics.Raycast(ray, out hitInfo, maxDistance);
            _characterController.enabled = true;

            return result;
        }
        
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                JumpPressed = true;
            }
            else if (!Input.GetKey(KeyCode.Space))
            {
                JumpPressed = false;
            }

            if (Input.GetKeyDown(KeyCode.F))
            {
                var flare = Instantiate(FlarePrefab);
                flare.transform.position = _eyes.position - Vector3.up * 0.5f;

                var rigidBody = flare.GetComponent<Rigidbody>();

                rigidBody.velocity = (_eyes.forward + Vector3.up * 0.25f).normalized * FlareThrowSpeed;
                rigidBody.angularVelocity = new Vector3(UnityEngine.Random.value * 90f - 45f,
                    UnityEngine.Random.value * 90f - 45f, UnityEngine.Random.value * 90f - 45f);
            }

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                var look = new float2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * MouseSensitivity;
                var pitch = _eyes.localEulerAngles.x;

                if (pitch >= 180f) pitch -= 360f;

                transform.localRotation = Quaternion.Euler(0f, look.x, 0f) * transform.localRotation;
                _eyes.localEulerAngles = new Vector3(Mathf.Clamp(pitch - look.y, -90f, 90f), 0f, 0f);

                var ctrlHeld = Input.GetKey(KeyCode.LeftControl);

                if (Input.GetMouseButtonDown(0))
                {
                    Subtract();
                }
                else if (Input.GetMouseButtonDown(1))
                {
                    // EditTerrain(true, ctrlHeld);
                }
            }
            else if (Input.GetMouseButtonDown(0))
            {
                Cursor.lockState = CursorLockMode.Locked;
            }
        }

        private void Subtract()
        {
            var ray = new Ray(_eyes.position, _eyes.forward);
            if (!Raycast(ray, out var hitInfo, 16f)) return;

            var bspDemo = hitInfo.transform.GetComponent<BspDemo>();
            if (bspDemo == null) return;

            var position = hitInfo.point;
            var rotation = UnityEngine.Random.rotationUniform;
            var localScale = new Vector3(UnityEngine.Random.value * 0.25f + 2.5f,
                UnityEngine.Random.value * 0.25f + 2.5f,
                UnityEngine.Random.value * 0.25f + 2.5f);

            bspDemo.Subtract(float4x4.TRS(position, rotation, localScale));
            bspDemo.LogInfo();
        }

        void FixedUpdate()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;

            var move = new float2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));

            var dt = Time.fixedDeltaTime;

            var friction = IsGrounded ? GroundFriction : AirFriction;
            var accel = IsGrounded ? GroundAccel : AirAccel;

            Velocity.xz *= math.pow(1f - friction, dt);

            if (IsGrounded)
            {
                if (Velocity.y < 0f)
                {
                    Velocity.y = 0f;
                }

                if (JumpPressed)
                {
                    JumpPressed = false;

                    Velocity.y += JumpSpeed;
                }
            }

            if (transform.position.y < -16f)
            {
                Velocity = float3.zero;
                _characterController.Move(Vector3.up * 32f);
            }

            Velocity.y -= 9.81f * dt;

            if (math.lengthsq(move) > 1f)
            {
                move = math.normalizesafe(move);
            }

            var forward = ((float3)transform.forward).xz;
            var right = ((float3)transform.right).xz;

            var maxSpeed = Input.GetKey(KeyCode.LeftShift) ? MaxRunSpeed : MaxWalkSpeed;

            var targetVel = (forward * move.y + right * move.x) * maxSpeed;
            var wish = targetVel - Velocity.xz;

            var wishDir = math.normalizesafe(wish);
            var dot = math.dot(wishDir, Velocity.xz);

            if (dot < 0f)
            {
                Velocity.xz -= wishDir * dot;
            }
            else
            {
                if (math.lengthsq(wish) > accel * dt)
                {
                    wish = math.normalizesafe(wish) * accel * dt;
                }

                Velocity.xz += wish;
            }

            _characterController.Move(Velocity * dt);

            IsGrounded = _characterController.isGrounded;

            _characterController.enabled = false;

            if (Physics.SphereCast(transform.position + Vector3.up * (_characterController.radius),
                    _characterController.radius, Vector3.down, out var hitInfo, 0.1f))
            {
                IsGrounded = true;
            }

            _characterController.enabled = true;
        }
    }
}