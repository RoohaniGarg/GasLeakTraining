using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Phase 5: The ONE place that turns screen taps into taps on 3D objects.
/// Fires a ray from the camera through the finger position; if it hits a
/// collider that belongs to an InteractableObject, that object is notified.
/// Lives on the ScenarioManager GameObject.
/// </summary>
public class TapInputManager : MonoBehaviour
{
    [Tooltip("Leave empty to use Main Camera")]
    public Camera arCamera;
    public float maxDistance = 10f;
    public LayerMask layers = ~0;

    [Tooltip("Turn off to ignore all object taps (e.g. while a quiz panel is open)")]
    public bool inputEnabled = true;

    void Update()
    {
        if (!inputEnabled) return;

        Vector2 screenPos;
        int pointerId;

        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase != TouchPhase.Began) return;
            screenPos = touch.position;
            pointerId = touch.fingerId;
        }
        else if (Input.GetMouseButtonDown(0))   // lets you test in the Editor with a mouse
        {
            screenPos = Input.mousePosition;
            pointerId = -1;
        }
        else return;

        // Ignore taps on UI buttons/panels (Phase 6)
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId))
            return;

        if (arCamera == null) arCamera = Camera.main;
        if (arCamera == null) return;

        Ray ray = arCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, layers, QueryTriggerInteraction.Ignore))
        {
            var target = hit.collider.GetComponentInParent<InteractableObject>();
            if (target != null) target.NotifyTapped();
        }
    }
}
