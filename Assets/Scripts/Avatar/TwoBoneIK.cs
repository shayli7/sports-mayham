using UnityEngine;

namespace SportsMayhem.Avatar
{
    /// <summary>Analytic two-bone IK (shoulder - elbow - hand).</summary>
    public static class TwoBoneIK
    {
        /// <summary>
        /// Places the elbow so the arm (lengths <paramref name="upper"/>, <paramref name="lower"/>) reaches
        /// from <paramref name="root"/> towards <paramref name="target"/>, bending towards <paramref name="pole"/>.
        /// </summary>
        /// <param name="end">Where the hand ends up (the target, pulled in if it's out of reach).</param>
        /// <returns>The elbow position.</returns>
        public static Vector3 Solve(Vector3 root, Vector3 target, float upper, float lower, Vector3 pole, out Vector3 end)
        {
            var toTarget = target - root;
            var direction = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : Vector3.forward;
            var distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upper - lower) + 1e-3f, (upper + lower) * 0.999f);
            end = root + direction * distance;

            // Law of cosines: how far along the root->hand line the elbow sits, and how far off it.
            var along = (upper * upper - lower * lower + distance * distance) / (2f * distance);
            var offset = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));

            var bend = Vector3.ProjectOnPlane(pole - root, direction);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.down, direction);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.back, direction);

            return root + direction * along + bend.normalized * offset;
        }
    }
}
