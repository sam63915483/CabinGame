using UnityEngine;

namespace Cabin.Player
{
    /// <summary>
    /// The player's own sounds: a footstep every stride while walking (grass, or wood when standing on lumber), a
    /// push-off when jumping, and a thud on landing — after any real fall, jumped or walked off an edge, louder the
    /// harder you come down. Punches: a whoosh as one is thrown and a knock when it connects. Tools: the nail gun's
    /// thunk and hiss, a few saw strokes per cut.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class PlayerSounds : MonoBehaviour
    {
        [SerializeField] FirstPersonController controller;
        [SerializeField] AudioClip[] grassSteps, woodSteps, jumps, grassLands, woodLands;
        [SerializeField] AudioClip[] punchWhooshes, punchHits;
        [SerializeField] AudioClip[] nailGunShots, sawCuts;
        [Tooltip("Layers that sound like wood underfoot; anything else is grass.")]
        [SerializeField] LayerMask woodLayers;
        [Tooltip("Metres between footsteps at the walk clip's speed (2.5 m/s) and the run's (6.5 m/s): what the legs cover per step.")]
        [SerializeField] float walkStride = 1.3f, runStride = 2f;
        [SerializeField] float stepVolume = 0.55f, crouchStepVolume = 0.3f;
        [Tooltip("Airborne at least this long, falling at least this fast (m/s), before landing makes a sound.")]
        [SerializeField] float minAirTime = 0.2f, minFallSpeed = 3f;

        CharacterController body;
        AudioSource source;
        float strideLeft, airTime, fallSpeed;
        bool wasGrounded = true;
        AudioClip last;

        public void Set(FirstPersonController fpc, AudioClip[] grass, AudioClip[] wood, AudioClip[] jump,
            AudioClip[] landGrass, AudioClip[] landWood, LayerMask woodMask)
        {
            controller = fpc;
            grassSteps = grass;
            woodSteps = wood;
            jumps = jump;
            grassLands = landGrass;
            woodLands = landWood;
            woodLayers = woodMask;
        }

        public void SetPunches(AudioClip[] whooshes, AudioClip[] hits)
        {
            punchWhooshes = whooshes;
            punchHits = hits;
        }

        public void SetTools(AudioClip[] nailGun, AudioClip[] saw)
        {
            nailGunShots = nailGun;
            sawCuts = saw;
        }

        /// <summary>The nail gun fired: pneumatic thunk and hiss.</summary>
        public void NailGunFired() => Play(nailGunShots, 0.7f);

        /// <summary>A saw cut went through.</summary>
        public void SawCut() => Play(sawCuts, 0.65f);

        /// <summary>A punch thrown; <paramref name="power"/> 0–1 (how long it was wound up).</summary>
        public void PunchThrown(float power) => Play(punchWhooshes, Mathf.Lerp(0.35f, 0.75f, power));

        /// <summary>A punch landed on something.</summary>
        public void PunchLanded(float power) => Play(punchHits, Mathf.Lerp(0.45f, 1f, power));

        void Awake()
        {
            body = controller.GetComponent<CharacterController>();
            source = GetComponent<AudioSource>();
        }

        void Update()
        {
            if (controller.ExternalControl || controller.Flying || !body.enabled)
            {
                wasGrounded = true;
                airTime = 0f;
                return;
            }
            Vector3 v = body.velocity;
            bool grounded = body.isGrounded;

            if (!grounded)
            {
                if (wasGrounded && v.y > 1f) Play(jumps, 0.6f);
                airTime += Time.deltaTime;
                fallSpeed = Mathf.Max(fallSpeed, -v.y);
            }
            else
            {
                if (!wasGrounded && airTime >= minAirTime && fallSpeed >= minFallSpeed)
                {
                    float hard = Mathf.InverseLerp(minFallSpeed, 10f, fallSpeed);
                    Play(OnWood() ? woodLands : grassLands, Mathf.Lerp(0.7f, 1f, hard));
                    strideLeft = walkStride * 0.5f; // the next step comes a half stride after landing
                }
                airTime = 0f;
                fallSpeed = 0f;

                v.y = 0f;
                float speed = v.magnitude;
                if (speed > 0.5f)
                {
                    strideLeft -= speed * Time.deltaTime;
                    if (strideLeft <= 0f)
                    {
                        strideLeft += Mathf.Lerp(walkStride, runStride, Mathf.InverseLerp(2.5f, 6.5f, speed));
                        float volume = controller.Crouching ? crouchStepVolume : stepVolume * Mathf.Lerp(1f, 1.3f, Mathf.InverseLerp(2.5f, 6.5f, speed));
                        Play(OnWood() ? woodSteps : grassSteps, volume);
                    }
                }
                else strideLeft = Mathf.Min(strideLeft, walkStride * 0.25f); // starting off, the first step is quick
            }
            wasGrounded = grounded;
        }

        /// <summary>Whether what's underfoot is lumber (or concrete) rather than the ground.</summary>
        bool OnWood()
        {
            var ray = new Ray(transform.position + Vector3.up * 0.3f, Vector3.down);
            return Physics.Raycast(ray, out var hit, 0.8f, Physics.DefaultRaycastLayers & ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore)
                && (woodLayers.value & (1 << hit.collider.gameObject.layer)) != 0;
        }

        /// <summary>A random one of the clips (not the one just played), slightly re-pitched so repeats don't stand out.</summary>
        void Play(AudioClip[] clips, float volume)
        {
            if (clips == null || clips.Length == 0) return;
            var clip = clips[Random.Range(0, clips.Length)];
            if (clip == last && clips.Length > 1) clip = clips[(System.Array.IndexOf(clips, clip) + 1) % clips.Length];
            last = clip;
            source.pitch = Random.Range(0.93f, 1.07f);
            source.PlayOneShot(clip, volume);
        }
    }
}
