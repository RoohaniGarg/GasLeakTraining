using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Phase 9: Springy scale for UI elements. Buttons squash a little while pressed
/// and bounce back; any element can Punch() (bounce), Wiggle() (a "no" shake that
/// rotates, so it never fights the layout groups) or SetScale() to pop in.
/// TrainingUI adds it where needed.
/// </summary>
public class UISpring : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public bool pressable;
    [Tooltip("Scale the spring rests at (the alarm button breathes by changing this)")]
    public float restScale = 1f;
    public float stiffness = 380f;
    public float damping = 20f;

    private float scale = 1f, velocity;
    private bool pressed;
    private float wiggleAmp, wiggleTime = 99f;
    private bool settled = true;

    public void SetScale(float s)
    {
        scale = s;
        velocity = 0f;
        settled = false;
    }

    /// <summary>Bounce outwards (positive) or inwards (negative), e.g. 0.08 = about 8 %.</summary>
    public void Punch(float amount = 0.08f)
    {
        velocity += amount * 20f;
        settled = false;
    }

    /// <summary>Quick left-right rotation shake that dies out.</summary>
    public void Wiggle(float degrees = 5f)
    {
        wiggleAmp = degrees;
        wiggleTime = 0f;
        settled = false;
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (!pressable) return;
        pressed = true;
        settled = false;
        if (EffectsManager.Instance != null) EffectsManager.Instance.Tap();
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (!pressable || !pressed) return;
        pressed = false;
        Punch(0.03f);
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (!pressable) return;
        pressed = false;
        settled = false;
    }

    void OnDisable()
    {
        pressed = false;
        scale = restScale;
        velocity = 0f;
        wiggleTime = 99f;
        transform.localScale = Vector3.one * scale;
        transform.localRotation = Quaternion.identity;
        settled = true;
    }

    void Update()
    {
        float target = restScale * (pressed ? 0.94f : 1f);
        if (settled && Mathf.Abs(target - scale) < 0.0005f) return;

        float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        velocity += (stiffness * (target - scale) - damping * velocity) * dt;
        scale += velocity * dt;

        float rot = 0f;
        if (wiggleTime < 0.8f)
        {
            wiggleTime += dt;
            rot = wiggleAmp * Mathf.Exp(-wiggleTime * 6f) * Mathf.Sin(wiggleTime * 42f);
        }

        transform.localScale = Vector3.one * scale;
        transform.localRotation = Quaternion.Euler(0f, 0f, rot);

        settled = Mathf.Abs(target - scale) < 0.0005f && Mathf.Abs(velocity) < 0.001f && wiggleTime >= 0.8f;
        if (settled)
        {
            scale = target;
            transform.localScale = Vector3.one * scale;
            transform.localRotation = Quaternion.identity;
        }
    }
}
