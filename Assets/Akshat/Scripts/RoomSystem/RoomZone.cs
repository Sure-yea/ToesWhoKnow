using UnityEngine;
using Unity.Cinemachine;
using Akshat;

namespace Akshat.RoomSystem
{
    /// <summary>
    /// Represents a self-contained room or hallway area.
    /// Holds the camera, movement mode, and optional ambience for this zone.
    /// </summary>
    [SelectionBase]
    [DisallowMultipleComponent]
    public class RoomZone : MonoBehaviour
    {
        [Header("Room Identity")]
        [Tooltip("Descriptive name for this room (e.g. 'Living Room', 'Hallway 1', 'Basement').")]
        [SerializeField] private string roomName = "New Room";

        [Header("Perspective & Movement")]
        [Tooltip("The movement mode the player uses inside this room.")]
        [SerializeField] private MovementMode movementMode = MovementMode.TopDown;

        [Header("Camera Configuration")]
        [Tooltip("The CinemachineCamera assigned to film this room.\nAuto-located in children if unassigned.")]
        [SerializeField] private CinemachineCamera roomCamera;

        [Header("Optional Room Ambience")]
        [Tooltip("Optional background music or ambient audio clip for this room.")]
        [SerializeField] private AudioClip ambientAudio;

        [Tooltip("Volume multiplier for this room's ambient audio (0 to 1).")]
        [Range(0f, 1f)]
        [SerializeField] private float ambientVolume = 1f;

        [Tooltip("Whether this room's ambient audio should loop while inside.")]
        [SerializeField] private bool loopAmbient = true;

        public string RoomName => roomName;
        public MovementMode MovementMode => movementMode;
        public CinemachineCamera RoomCamera => roomCamera;
        public AudioClip AmbientAudio => ambientAudio;
        public float AmbientVolume => ambientVolume;
        public bool LoopAmbient => loopAmbient;

        private AudioSource localAudioSource;

        private void Reset()
        {
            roomName = gameObject.name;
            if (roomCamera == null)
            {
                roomCamera = GetComponentInChildren<CinemachineCamera>();
            }
        }

        private void OnValidate()
        {
            if (roomCamera == null)
            {
                roomCamera = GetComponentInChildren<CinemachineCamera>();
            }
        }

        private void Awake()
        {
            if (roomCamera == null)
            {
                roomCamera = GetComponentInChildren<CinemachineCamera>();
            }
        }

        private void Start()
        {
            // If PerspectiveTransitionManager is not handling room transitions in this scene,
            // play ambience locally if this room is currently active.
            if (Shaurya.PerspectiveTransitionManager.Instance == null && ambientAudio != null && gameObject.activeInHierarchy)
            {
                PlayLocalAmbience();
            }
        }

        private void OnEnable()
        {
            if (Shaurya.PerspectiveTransitionManager.Instance == null && ambientAudio != null)
            {
                PlayLocalAmbience();
            }
        }

        private void OnDisable()
        {
            if (Shaurya.PerspectiveTransitionManager.Instance == null && localAudioSource != null)
            {
                localAudioSource.Stop();
            }
        }

        /// <summary>
        /// Request this room's ambient audio to play.
        /// </summary>
        public void PlayAmbience()
        {
            if (Shaurya.PerspectiveTransitionManager.Instance != null)
            {
                Shaurya.PerspectiveTransitionManager.Instance.PlayRoomAmbience(this);
            }
            else
            {
                PlayLocalAmbience();
            }
        }

        /// <summary>
        /// Request this room's ambient audio to stop.
        /// </summary>
        public void StopAmbience()
        {
            if (Shaurya.PerspectiveTransitionManager.Instance != null)
            {
                Shaurya.PerspectiveTransitionManager.Instance.StopRoomAmbience();
            }
            else if (localAudioSource != null)
            {
                localAudioSource.Stop();
            }
        }

        private void PlayLocalAmbience()
        {
            if (ambientAudio == null) return;

            if (localAudioSource == null)
            {
                localAudioSource = GetComponent<AudioSource>();
                if (localAudioSource == null)
                {
                    localAudioSource = gameObject.AddComponent<AudioSource>();
                }
                localAudioSource.playOnAwake = false;
                localAudioSource.spatialBlend = 0f;
            }

            localAudioSource.clip = ambientAudio;
            localAudioSource.volume = ambientVolume;
            localAudioSource.loop = loopAmbient;
            if (!localAudioSource.isPlaying)
            {
                localAudioSource.Play();
            }
        }
    }
}
