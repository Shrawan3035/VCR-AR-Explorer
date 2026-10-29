using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace VCR.AR
{
    /// <summary>
    /// Native AR (AR Foundation + ARCore) placement for the optional Android build:
    /// detect horizontal planes → tap to place the rig → pinch to scale, two-finger twist to
    /// rotate. After placement, taps select components (SelectionManager) and show the
    /// information panel. Handles unsupported devices and denied camera permission.
    /// </summary>
    public class ARPlacementController : MonoBehaviour
    {
        public ARRaycastManager raycastManager;
        public ARPlaneManager planeManager;
        public ARSession session;
        public GameObject rig;            // VCR_Rig instance (disabled until placed)
        public SelectionManager selection;
        public float minScale = 0.1f, maxScale = 1.5f;

        private static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();
        private Text _status;
        private bool _placed;
        private float _lastPinch, _lastAngle;
        private int _lastCount;

        private void Start()
        {
            if (rig != null) rig.SetActive(false);
            if (selection != null) selection.inputEnabled = false;
            var canvas = UIFactory.CreateCanvas("ARUI", 20);
            var bar = UIFactory.Panel(canvas.transform, "ARStatus", new Color(0.075f, 0.13f, 0.23f, 0.88f), false);
            UIFactory.Anchor(bar, new Vector2(0, 1), Vector2.one, new Vector2(16, -150), new Vector2(-16, -40));
            _status = UIFactory.Label(bar, "Starting AR…", 22, Color.white, FontStyle.Normal, TextAnchor.MiddleCenter);
            UIFactory.Stretch(_status.rectTransform, 12, 6, 12, 6);
            var row = UIFactory.Empty(canvas.transform, "ARButtons");
            UIFactory.Anchor(row, Vector2.zero, new Vector2(1, 0), new Vector2(16, 24), new Vector2(-16, 100));
            UIFactory.HRow(row, 12, 0).childForceExpandWidth = true;
            UIFactory.Button(row, "Back", () => SceneManager.LoadScene("MainMenu"), VCRColors.Navy, Color.white, 22);
            UIFactory.Button(row, "Re-place", Replace, VCRColors.Paper, VCRColors.Navy, 22);
            StartCoroutine(CheckSupport());
        }

        private System.Collections.IEnumerator CheckSupport()
        {
            if (ARSession.state == ARSessionState.None || ARSession.state == ARSessionState.CheckingAvailability)
                yield return ARSession.CheckAvailability();
            if (ARSession.state == ARSessionState.Unsupported)
                _status.text = "This device does not support ARCore. Use the 3D explorer instead, or open the website for the 3D view.";
            else if (ARSession.state == ARSessionState.NeedsInstall)
                _status.text = "Google Play Services for AR is required. Install it from the Play Store, then reopen AR mode.";
        }

        private void Replace()
        {
            _placed = false;
            if (rig != null) rig.SetActive(false);
            if (selection != null) { selection.Select(null); selection.inputEnabled = false; }
            planeManager.enabled = true;
            foreach (var p in planeManager.trackables) p.gameObject.SetActive(true);
        }

        private void Update()
        {
            if (ARSession.state == ARSessionState.Unsupported || ARSession.state == ARSessionState.NeedsInstall) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                _status.text = "Camera permission is needed for AR. Allow it in Settings ▸ Apps ▸ VCR AR Explorer ▸ Permissions, or go Back and use the 3D explorer.";
                return;
            }
#endif
            var touches = VCRInput.Touches;
            if (!_placed)
            {
                _status.text = planeManager.trackables.count == 0 ? "Move your phone slowly over a table or the floor…" : "Tap a highlighted surface to place the refrigeration rig.";
                if (touches.Count == 1 && touches[0].began && !VCRInput.IsOverUI(touches[0].position) &&
                    raycastManager.Raycast(touches[0].position, Hits, TrackableType.PlaneWithinPolygon))
                {
                    var pose = Hits[0].pose;
                    rig.transform.SetPositionAndRotation(pose.position, Quaternion.Euler(0, Camera.main.transform.eulerAngles.y + 180f, 0));
                    rig.transform.localScale = Vector3.one * 0.5f;
                    rig.SetActive(true);
                    _placed = true;
                    if (selection != null) selection.inputEnabled = true;
                    foreach (var p in planeManager.trackables) p.gameObject.SetActive(false);
                    planeManager.enabled = false;
                }
                return;
            }
            _status.text = "Tap a part to learn about it · pinch to scale · twist to rotate";
            if (touches.Count == 2)
            {
                var a = touches[0].position; var b = touches[1].position;
                float pinch = Vector2.Distance(a, b);
                float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                if (_lastCount == 2 && _lastPinch > 1f)
                {
                    float s = Mathf.Clamp(rig.transform.localScale.x * pinch / _lastPinch, minScale, maxScale);
                    rig.transform.localScale = Vector3.one * s;
                    rig.transform.Rotate(0f, -Mathf.DeltaAngle(_lastAngle, angle), 0f, Space.World);
                }
                _lastPinch = pinch;
                _lastAngle = angle;
            }
            _lastCount = touches.Count;
        }
    }
}
