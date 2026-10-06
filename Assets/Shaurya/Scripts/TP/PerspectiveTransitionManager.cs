using System.Collections;
using UnityEngine;
using Unity.Cinemachine;
using Akshat;

namespace Shaurya
{
    using MovementMode = Akshat.MovementMode;

    /// <summary>
    /// Central manager for fade-to-black room and perspective transitions.
    /// Operates as a singleton service so doors do not require manual manager references.
    /// </summary>
    [DisallowMultipleComponent]
    public class PerspectiveTransitionManager : MonoBehaviour
    {
        public static PerspectiveTransitionManager Instance { get; private set; }

        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Required References (Auto-Located If Unassigned)")]
        [Tooltip("The ScreenFader script attached to the full-screen black Image panel.")]
        [SerializeField] private ScreenFader screenFader;

        [Tooltip("The PlayerMovement script on the Player GameObject.")]
        [SerializeField] private PlayerMovement playerMovement;

        [Header("Cinemachine Multi-Room Camera System")]
        [Tooltip("The initial CinemachineCamera active at scene start (e.g. Hallway 1).\n" +
                 "If assigned, it will automatically be activated on Start.")]
        [SerializeField] private CinemachineCamera startingCamera;

        [Tooltip("Optional list of cameras to disable at scene start. If empty, cameras are managed on-demand.")]
        [SerializeField] private CinemachineCamera[] allCameras;

        [Header("Legacy CameraController (Optional Fallback)")]
        [Tooltip("Leave empty if using Cinemachine.")]
        [SerializeField] private CameraController cameraController;
#pragma warning disable CS0414
        [SerializeField] private float hallwayCameraSize = 5f;
#pragma warning restore CS0414
        [SerializeField] private float topDownCameraSize = 8f;
        [SerializeField] private float cameraTransitionSpeed = 10f;

        [Header("Room Management (Culling)")]
        [Tooltip("The initial RoomZone active at scene start.")]
        [SerializeField] private Akshat.RoomSystem.RoomZone startingRoom;

        [Tooltip("Optional list of rooms to manage. Auto-located if empty.")]
        [SerializeField] private Akshat.RoomSystem.RoomZone[] allRooms;

        [Header("Room Ambience / Background Music")]
        [Tooltip("Master volume multiplier for room ambient audio.")]
        [Range(0f, 1f)]
        [SerializeField] private float masterAmbienceVolume = 1f;

        [Tooltip("Duration of crossfade when switching room music tracks (in seconds).")]
        [SerializeField] private float musicFadeDuration = 0.5f;

        // ── State ─────────────────────────────────────────────────────────────

        public bool IsTransitioning { get; private set; }
        public Akshat.RoomSystem.RoomZone CurrentRoom => currentRoom;

        private CinemachineCamera currentCamera;
        private Akshat.RoomSystem.RoomZone currentRoom;
        private CinemachineBrain brain;

        private AudioSource musicSourceA;
        private AudioSource musicSourceB;
        private bool isUsingSourceA = true;
        private Coroutine activeMusicFadeCoroutine;
        private AudioClip currentPlayingClip;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            // Singleton registration
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            InitAudioSources();

            // Auto-locate player if unassigned
            if (playerMovement == null)
            {
                playerMovement = FindAnyObjectByType<PlayerMovement>();
            }

            // Auto-locate screen fader if unassigned
            if (screenFader == null)
            {
                screenFader = FindAnyObjectByType<ScreenFader>();
            }

            // Cache the CinemachineBrain from the main camera
            var mainCam = Camera.main;
            if (mainCam != null)
                brain = mainCam.GetComponent<CinemachineBrain>();

            if (brain == null)
                Debug.LogWarning("[PerspectiveTransitionManager] No CinemachineBrain found on Main Camera. Instant cuts won't work.");
        }

        private void Start()
        {
            // Auto-locate rooms if not assigned
            if (allRooms == null || allRooms.Length == 0)
            {
                allRooms = FindObjectsByType<Akshat.RoomSystem.RoomZone>(FindObjectsInactive.Include);
            }

            // Fallback starting room if unassigned
            if (startingRoom == null && allRooms != null && allRooms.Length > 0)
            {
                foreach (var room in allRooms)
                {
                    if (room != null && room.gameObject.activeSelf)
                    {
                        startingRoom = room;
                        break;
                    }
                }
                if (startingRoom == null)
                    startingRoom = allRooms[0];
            }

            // Disable all rooms except the starting room
            if (allRooms != null && allRooms.Length > 0)
            {
                foreach (var room in allRooms)
                {
                    if (room != null && room != startingRoom)
                    {
                        room.gameObject.SetActive(false);
                    }
                }
            }

            if (startingRoom != null)
            {
                startingRoom.gameObject.SetActive(true);
                currentRoom = startingRoom;
                PlayRoomAmbience(currentRoom, instant: false);
            }

            // Disable all cameras if explicitly configured, then enable only the starting one
            if (allCameras != null && allCameras.Length > 0)
            {
                foreach (var cam in allCameras)
                    if (cam != null) cam.gameObject.SetActive(false);
            }

            if (startingCamera != null)
            {
                startingCamera.gameObject.SetActive(true);
                currentCamera = startingCamera;
            }

            // Set the Brain's default blend to Cut so all transitions are instant
            if (brain != null)
                brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void BeginTransition(Transform destination, Akshat.MovementMode targetMode, CinemachineCamera targetCamera = null, Akshat.RoomSystem.RoomZone targetRoom = null)
        {
            if (destination == null)
            {
                Debug.LogError("[PerspectiveTransitionManager] Destination is null. Aborted.");
                return;
            }

            if (targetRoom == null && targetCamera != null)
            {
                targetRoom = targetCamera.GetComponentInParent<Akshat.RoomSystem.RoomZone>(true);
            }

            BeginTransition(destination.position, targetMode, targetCamera, targetRoom);
        }

        public void BeginTransition(Vector3 destinationPosition, Akshat.MovementMode targetMode, CinemachineCamera targetCamera = null, Akshat.RoomSystem.RoomZone targetRoom = null)
        {
            if (IsTransitioning)
            {
                Debug.LogWarning("[PerspectiveTransitionManager] Already transitioning. Ignored.");
                return;
            }

            if (targetRoom == null && targetCamera != null)
            {
                targetRoom = targetCamera.GetComponentInParent<Akshat.RoomSystem.RoomZone>(true);
            }

            StartCoroutine(TransitionCoroutine(destinationPosition, targetMode, targetCamera, targetRoom));
        }

        // ── Coroutine ─────────────────────────────────────────────────────────

        private IEnumerator TransitionCoroutine(Vector3 destinationPosition, Akshat.MovementMode targetMode, CinemachineCamera targetCamera, Akshat.RoomSystem.RoomZone targetRoom)
        {
            IsTransitioning = true;

            try
            {
                // ── 1. Disable player movement immediately to freeze input & cancel velocity ──
                if (playerMovement != null)
                    playerMovement.SetMovementEnabled(false);

                // ── 2. Fade to black ───────────────────────────────────────────────
                if (screenFader != null)
                    yield return StartCoroutine(screenFader.FadeOut());
                else
                    yield return new WaitForSecondsRealtime(0.35f);

                // ── 3. Room Culling & Teleport ──────────────────────────────────────
                // First: Turn ON the destination room so its floor and colliders exist
                if (targetRoom != null && targetRoom != currentRoom)
                {
                    targetRoom.gameObject.SetActive(true);
                }

                // Second: Teleport the player safely into the new room
                if (playerMovement != null)
                {
                    playerMovement.Teleport(destinationPosition);
                }

                // Third: Deactivate the old room now that the player is safely in the new room
                if (currentRoom != null && targetRoom != null && currentRoom != targetRoom)
                {
                    currentRoom.gameObject.SetActive(false);
                    currentRoom = targetRoom;
                }
                else if (targetRoom != null)
                {
                    currentRoom = targetRoom;
                }

                // ── 4. Room Ambience / Music Transition ───────────────────────────
                PlayRoomAmbience(currentRoom, instant: false);

                // ── 5. Switch movement mode (updates gravity scale & resets velocity) ──
                if (playerMovement != null)
                    playerMovement.SetMode(targetMode);

                // ── 6. Instant camera switch ───────────────────────────────────────
                if (targetCamera != null)
                    SwitchCameraInstant(targetCamera, destinationPosition);
                else
                    ApplyCameraForMode(targetMode, destinationPosition);

                // ── 7. Physics & Render Sync ───────────────────────────────────────
                Physics2D.SyncTransforms();
                yield return new WaitForFixedUpdate();
                yield return null;
                yield return new WaitForEndOfFrame();

                // Re-confirm player position & zero velocity after fixed update settling
                if (playerMovement != null)
                {
                    playerMovement.Teleport(destinationPosition);
                    playerMovement.SetMovementEnabled(true);
                }

                // ── 8. Fade back in ────────────────────────────────────────────────
                if (screenFader != null)
                    yield return StartCoroutine(screenFader.FadeIn());
            }
            finally
            {
                IsTransitioning = false;
                if (playerMovement != null)
                    playerMovement.SetMovementEnabled(true);
            }
        }

        // ── Room Ambience & Music System ──────────────────────────────────────

        private void InitAudioSources()
        {
            var sources = GetComponents<AudioSource>();
            if (sources.Length > 0) musicSourceA = sources[0];
            if (sources.Length > 1) musicSourceB = sources[1];

            if (musicSourceA == null) musicSourceA = gameObject.AddComponent<AudioSource>();
            if (musicSourceB == null) musicSourceB = gameObject.AddComponent<AudioSource>();

            musicSourceA.playOnAwake = false;
            musicSourceA.spatialBlend = 0f;
            musicSourceA.loop = true;

            musicSourceB.playOnAwake = false;
            musicSourceB.spatialBlend = 0f;
            musicSourceB.loop = true;
        }

        /// <summary>
        /// Plays or transitions to the ambient audio configured on the given room.
        /// If the room has the same audio clip already playing, it continues seamlessly.
        /// </summary>
        public void PlayRoomAmbience(Akshat.RoomSystem.RoomZone room, bool instant = false)
        {
            if (musicSourceA == null || musicSourceB == null)
                InitAudioSources();

            if (room == null || room.AmbientAudio == null)
            {
                StopRoomAmbience(instant ? 0f : musicFadeDuration);
                return;
            }

            AudioClip newClip = room.AmbientAudio;
            float targetVolume = Mathf.Clamp01(room.AmbientVolume * masterAmbienceVolume);
            bool loop = room.LoopAmbient;

            // If the same clip is already playing, preserve playback position and smoothly update volume
            if (currentPlayingClip == newClip && ((isUsingSourceA && musicSourceA.isPlaying) || (!isUsingSourceA && musicSourceB.isPlaying)))
            {
                AudioSource activeSrc = isUsingSourceA ? musicSourceA : musicSourceB;
                activeSrc.volume = targetVolume;
                activeSrc.loop = loop;
                return;
            }

            currentPlayingClip = newClip;

            AudioSource fadeOutSource = isUsingSourceA ? musicSourceA : musicSourceB;
            AudioSource fadeInSource = isUsingSourceA ? musicSourceB : musicSourceA;
            isUsingSourceA = !isUsingSourceA;

            fadeInSource.clip = newClip;
            fadeInSource.loop = loop;

            if (activeMusicFadeCoroutine != null)
            {
                StopCoroutine(activeMusicFadeCoroutine);
                activeMusicFadeCoroutine = null;
            }

            if (instant || musicFadeDuration <= 0f)
            {
                if (fadeOutSource != null && fadeOutSource.isPlaying)
                {
                    fadeOutSource.Stop();
                    fadeOutSource.volume = 0f;
                }
                fadeInSource.volume = targetVolume;
                fadeInSource.Play();
            }
            else
            {
                activeMusicFadeCoroutine = StartCoroutine(CrossfadeMusicCoroutine(fadeOutSource, fadeInSource, targetVolume, musicFadeDuration));
            }
        }

        /// <summary>
        /// Stops the currently playing room music, fading out to silence.
        /// </summary>
        public void StopRoomAmbience(float fadeDuration = -1f)
        {
            if (fadeDuration < 0f) fadeDuration = musicFadeDuration;

            currentPlayingClip = null;

            if (activeMusicFadeCoroutine != null)
            {
                StopCoroutine(activeMusicFadeCoroutine);
                activeMusicFadeCoroutine = null;
            }

            AudioSource activeSource = isUsingSourceA ? musicSourceA : musicSourceB;

            if (fadeDuration <= 0f)
            {
                if (musicSourceA != null) { musicSourceA.Stop(); musicSourceA.volume = 0f; }
                if (musicSourceB != null) { musicSourceB.Stop(); musicSourceB.volume = 0f; }
            }
            else if (activeSource != null && activeSource.isPlaying)
            {
                activeMusicFadeCoroutine = StartCoroutine(FadeOutMusicCoroutine(activeSource, fadeDuration));
            }
        }

        /// <summary>
        /// Sets master volume multiplier for room ambient audio.
        /// </summary>
        public void SetMasterAmbienceVolume(float volume)
        {
            masterAmbienceVolume = Mathf.Clamp01(volume);
            if (currentRoom != null)
            {
                AudioSource activeSource = isUsingSourceA ? musicSourceA : musicSourceB;
                if (activeSource != null && activeSource.isPlaying)
                {
                    activeSource.volume = currentRoom.AmbientVolume * masterAmbienceVolume;
                }
            }
        }

        private IEnumerator CrossfadeMusicCoroutine(AudioSource fadeOutSource, AudioSource fadeInSource, float targetVolume, float duration)
        {
            float elapsed = 0f;
            float startOutVolume = (fadeOutSource != null && fadeOutSource.isPlaying) ? fadeOutSource.volume : 0f;

            if (fadeInSource != null)
            {
                fadeInSource.volume = 0f;
                fadeInSource.Play();
            }

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                if (fadeOutSource != null && fadeOutSource.isPlaying)
                {
                    fadeOutSource.volume = Mathf.Lerp(startOutVolume, 0f, t);
                }

                if (fadeInSource != null)
                {
                    fadeInSource.volume = Mathf.Lerp(0f, targetVolume, t);
                }

                yield return null;
            }

            if (fadeOutSource != null)
            {
                fadeOutSource.Stop();
                fadeOutSource.volume = 0f;
            }

            if (fadeInSource != null)
            {
                fadeInSource.volume = targetVolume;
            }

            activeMusicFadeCoroutine = null;
        }

        private IEnumerator FadeOutMusicCoroutine(AudioSource source, float duration)
        {
            float elapsed = 0f;
            float startVolume = (source != null && source.isPlaying) ? source.volume : 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                if (source != null)
                {
                    source.volume = Mathf.Lerp(startVolume, 0f, t);
                }

                yield return null;
            }

            if (source != null)
            {
                source.Stop();
                source.volume = 0f;
            }

            activeMusicFadeCoroutine = null;
        }

        // ── Camera Switching ──────────────────────────────────────────────────

        public void SwitchCameraInstant(CinemachineCamera newCamera, Vector3 snapPosition)
        {
            if (newCamera == null) return;

            if (currentCamera != null && currentCamera != newCamera)
                currentCamera.gameObject.SetActive(false);

            newCamera.gameObject.SetActive(true);

            Vector3 camPos = new Vector3(snapPosition.x, snapPosition.y, -10f);
            newCamera.ForceCameraPosition(camPos, Quaternion.identity);

            if (playerMovement != null)
            {
                CinemachineCore.OnTargetObjectWarped(
                    playerMovement.transform,
                    snapPosition - playerMovement.transform.position);
            }

            currentCamera = newCamera;
            Debug.Log($"[PerspectiveTransitionManager] <color=cyan>Cut</color> → <b>{newCamera.name}</b>");
        }

        // ── Legacy Fallback ───────────────────────────────────────────────────

        private void ApplyCameraForMode(Akshat.MovementMode mode, Vector3 playerPosition)
        {
            if (cameraController == null) return;
            if (mode == Akshat.MovementMode.TopDown)
                cameraController.EnterTriggerZone(playerPosition, topDownCameraSize, cameraTransitionSpeed);
            else
                cameraController.ExitTriggerZone(cameraTransitionSpeed);
        }
    }
}
