using System.Collections;
using UnityEngine;
using Oculus.Interaction;

namespace MixedRealityProject
{
    /// <summary>
    /// Keeps the ball suspended in mid-air (kinematic Rigidbody) when the scene
    /// starts and places it in front of the user: at head height, spawnDistance
    /// meters away along the gaze direction. The first time the user touches it
    /// (grab via the Interaction SDK) physics is enabled: from then on the ball
    /// reacts to gravity, can be thrown and bounces inside the room.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Grabbable))]
    public class GiveKinematics : MonoBehaviour
    {
        [Tooltip("Distance from the user's head at which the ball spawns, in meters.")]
        [SerializeField] float spawnDistance = 0.3f;

        Rigidbody _rigidbody;
        Grabbable _grabbable;

        void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _grabbable = GetComponent<Grabbable>();
            // Freeze before the first physics step so the ball stays suspended.
            _rigidbody.isKinematic = true;
        }

        IEnumerator Start()
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null)
            {
                Debug.LogWarning("[GiveKinematics] No main camera found: the ball stays at its authored position.");
                yield break;
            }

            // Wait until head tracking provides a real pose (the rig starts at the
            // origin for the first frames), with a timeout so an emulator without
            // tracking still gets the ball placed.
            float timeout = Time.time + 2f;
            while (head.position == Vector3.zero && Time.time < timeout)
            {
                yield return null;
            }

            // Horizontal gaze direction, so the ball spawns at eye height even if
            // the user is looking up or down.
            var forward = head.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;

            transform.position = head.position + forward * spawnDistance;
        }

        void OnEnable()
        {
            _grabbable.WhenPointerEventRaised += OnPointerEvent;
        }

        void OnDisable()
        {
            _grabbable.WhenPointerEventRaised -= OnPointerEvent;
        }

        void OnPointerEvent(PointerEvent evt)
        {
            // Select = the user grabbed/touched the ball: hand over to physics.
            if (evt.Type == PointerEventType.Select)
            {
                _rigidbody.isKinematic = false;
                // Job done: stop listening, the Grabbable handles everything else
                // (including the throw velocity on release).
                enabled = false;
            }
        }
    }
}
