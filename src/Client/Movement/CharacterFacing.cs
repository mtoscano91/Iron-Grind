using UnityEngine;

namespace IronGrind.Client.Movement
{
    /// <summary>
    /// The character's facing: a unit forward vector on the XZ plane and its yaw (movement-system.md CR-MOV-3,
    /// AC-MOV-6, EC-MOV-18; Movement Story 011). Owned and updated by <see cref="AutoRunState"/>; a mutable holder, so
    /// reading it never allocates. It is never a zero vector.
    /// </summary>
    public sealed class CharacterFacing
    {
        private const float FULL_TURN_DEG = 360f;
        private const float MIN_DIRECTION_MAGNITUDE = 0.001f;

        /// <summary>Creates a facing of <see cref="Vector3.forward"/>, yaw 0 (EC-MOV-18).</summary>
        public CharacterFacing()
        {
            Forward = Vector3.forward;
            YawDeg = 0f;
        }

        /// <summary>The facing as a unit vector on the XZ plane (Y = 0).</summary>
        public Vector3 Forward { get; private set; }

        /// <summary>The facing yaw in degrees, clockwise from <see cref="Vector3.forward"/>, in [0, 360).</summary>
        public float YawDeg { get; private set; }

        /// <summary>
        /// Points the facing along the horizontal part of <paramref name="direction"/>. A direction whose horizontal
        /// part is shorter than 0.001, or not a number, is ignored, so the facing never becomes a zero or NaN vector
        /// (EC-MOV-18).
        /// <c>YawDeg = atan2(x, z)</c> in degrees, wrapped to [0, 360).
        /// </summary>
        /// <param name="direction">Any vector; only its XZ part is used.</param>
        internal void Face(Vector3 direction)
        {
            float length = Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z);
            // Written as !(>=) so a NaN length is rejected too; an infinite length is rejected because the
            // division below would turn it into NaN.
            if (!(length >= MIN_DIRECTION_MAGNITUDE) || float.IsInfinity(length))
            {
                return;
            }

            float x = direction.x / length;
            float z = direction.z / length;
            Forward = new Vector3(x, 0f, z);

            float yaw = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
            if (yaw < 0f)
            {
                yaw += FULL_TURN_DEG;
            }

            if (yaw >= FULL_TURN_DEG)
            {
                yaw -= FULL_TURN_DEG;
            }

            YawDeg = yaw;
        }
    }
}
