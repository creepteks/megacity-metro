using System;
using Unity.Mathematics;
using UnityEngine;
using Utils.Misc;

namespace Unity.MegacityMetro.CameraManagement
{
    /// <summary>
    /// Create camera target authoring component in order to
    /// allow the game object camera to follow the player camera target entity
    /// </summary>
    public class HybridCameraManager : MonoBehaviour
    {
        public GameObject CameraObject;

        public float FollowDistance = 25.0f;
        public float FollowSharpness = 5.0f;
        public float LookAtSharpness = 5.0f;
        
        private float3 _playerPosition;
        private float3 _aimPosition;
        private float3 _prevTargetPosition;
        private Camera _camera;
        
        [NonSerialized]
        public bool WasInitialized;

        // Prefab values, restored when the active flight profile does not override the camera
        private float _defaultFollowDistance;
        private float _defaultFollowSharpness;
        private float _defaultLookAtSharpness;
        private float _defaultFieldOfView;
        private bool _flightCameraOverridden;

        public static HybridCameraManager Instance;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
            }
            else
            {
                Instance = this;
                _camera = CameraObject.GetComponent<Camera>();
                _defaultFollowDistance = FollowDistance;
                _defaultFollowSharpness = FollowSharpness;
                _defaultLookAtSharpness = LookAtSharpness;
                _defaultFieldOfView = _camera != null ? _camera.fieldOfView : 60f;
            }
        }

        /// <summary>Applies a flight profile's camera settings (see FlightProfile.OverrideCamera).</summary>
        public void ApplyFlightCamera(float followDistance, float followSharpness, float lookAtSharpness)
        {
            FollowDistance = followDistance;
            FollowSharpness = followSharpness;
            LookAtSharpness = lookAtSharpness;
            _flightCameraOverridden = true;
        }

        public void SetFieldOfView(float fieldOfView)
        {
            if (_camera != null)
                _camera.fieldOfView = fieldOfView;
        }

        /// <summary>Restores the prefab camera values after a profile override.</summary>
        public void ResetFlightCamera()
        {
            if (!_flightCameraOverridden)
                return;
            FollowDistance = _defaultFollowDistance;
            FollowSharpness = _defaultFollowSharpness;
            LookAtSharpness = _defaultLookAtSharpness;
            SetFieldOfView(_defaultFieldOfView);
            _flightCameraOverridden = false;
        }

        public void Reset()
        {
            WasInitialized = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStaticData()
        {
            Instance = null;
        }
        
        public void UpdateAimCameraTargetPosition(float3 position)
        {
            _aimPosition = position;
        }

        public void SetPlayerTargetPosition(float3 position)
        {
            _playerPosition = position;
        }

        public void StartShaking()
        {
            // TODO
        }

        public void PlaceCamera(float3 position, quaternion rotation)
        {
            _playerPosition = position;
            _aimPosition = _playerPosition + math.mul(rotation, math.forward());
            CameraUpdate(1f, true);
        }

        public void CameraUpdate(float deltaTime, bool skipSmoothing = false)
        {
            Transform cameraTransform = CameraObject.transform;

            float3 lookDirection = math.normalizesafe(_aimPosition - _playerPosition);
            float3 targetPosition = _playerPosition - (lookDirection * FollowDistance);
            float3 camToAimPointDir = math.normalizesafe(_aimPosition - (float3)cameraTransform.position);
            quaternion targetRotation = quaternion.LookRotationSafe(camToAimPointDir, math.up());
            if (skipSmoothing)
            {
                cameraTransform.position = targetPosition;
                cameraTransform.rotation = targetRotation;
            }
            else
            {
                cameraTransform.position = MathUtilities.SmoothFollow(cameraTransform.position, _prevTargetPosition, targetPosition, deltaTime, FollowSharpness);
                cameraTransform.rotation = math.slerp(cameraTransform.rotation, targetRotation,  MathUtilities.GetSharpnessInterpolant(LookAtSharpness, deltaTime));
            }

            _prevTargetPosition = targetPosition;
        }

        public void SetAllowDynamicResolution(bool value)
        {
            _camera.allowDynamicResolution = value;
        }
    }
}
