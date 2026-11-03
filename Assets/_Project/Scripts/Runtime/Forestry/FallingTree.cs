using UnityEngine;

namespace Cabin.Forestry
{
    /// <summary>
    /// A felled top on its way down: creaks as the hinge goes, lets go of the stump once it's well over, crashes when
    /// it hits the ground, then settles as a log on the forest floor.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class FallingTree : MonoBehaviour
    {
        const float LetGoAngle = 55f;

        Forest forest;
        Rigidbody body;
        AudioSource audioSource;
        Vector3 startUp;
        bool crashed;

        public void Begin(Forest owner, Vector3 lean)
        {
            forest = owner;
            body = GetComponent<Rigidbody>();
            startUp = transform.up;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.maxDistance = 120f;
            audioSource.minDistance = 4f;
            var creak = forest != null ? forest.FallClip() : null;
            if (creak != null) audioSource.PlayOneShot(creak, 0.9f);
        }

        void FixedUpdate()
        {
            var joint = GetComponent<HingeJoint>();
            if (joint != null && Vector3.Angle(startUp, transform.up) > LetGoAngle) Destroy(joint);
        }

        void OnCollisionEnter(Collision c)
        {
            if (crashed || GetComponent<HingeJoint>() != null || c.relativeVelocity.magnitude < 2f) return;
            crashed = true;
            var thud = forest != null ? forest.LandClip() : null;
            if (thud != null) audioSource.PlayOneShot(thud, Mathf.Clamp01(c.relativeVelocity.magnitude / 8f));
        }
    }
}
