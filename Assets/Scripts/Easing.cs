using UnityEngine;

/// <summary>
/// Phase 9: Easing curves used by every animation, so motion starts and stops
/// softly instead of linearly. All take t in 0..1 (clamped) and return 0..1
/// (Back / Elastic briefly overshoot).
/// </summary>
public static class Easing
{
    public static float OutCubic(float t) { t = Mathf.Clamp01(t); return 1f - Mathf.Pow(1f - t, 3f); }
    public static float InCubic(float t) { t = Mathf.Clamp01(t); return t * t * t; }
    public static float InQuad(float t) { t = Mathf.Clamp01(t); return t * t; }

    public static float InOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }

    public static float InOutSine(float t) { t = Mathf.Clamp01(t); return -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f; }

    /// <summary>Ends with a small overshoot, then settles (nice for "pop in").</summary>
    public static float OutBack(float t, float s = 1.70158f)
    {
        t = Mathf.Clamp01(t) - 1f;
        return 1f + t * t * ((s + 1f) * t + s);
    }

    /// <summary>Falls and bounces a little (a body hitting the floor).</summary>
    public static float OutBounce(float t)
    {
        t = Mathf.Clamp01(t);
        const float n = 7.5625f, d = 2.75f;
        if (t < 1f / d) return n * t * t;
        if (t < 2f / d) { t -= 1.5f / d; return n * t * t + 0.75f; }
        if (t < 2.5f / d) { t -= 2.25f / d; return n * t * t + 0.9375f; }
        t -= 2.625f / d;
        return n * t * t + 0.984375f;
    }

    /// <summary>Frame-rate independent smoothing factor for Lerp (higher speed = snappier).</summary>
    public static float Damp(float speed, float dt) => 1f - Mathf.Exp(-speed * dt);
}
