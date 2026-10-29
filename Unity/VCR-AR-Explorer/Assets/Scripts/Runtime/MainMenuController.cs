using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VCR
{
    /// <summary>Main menu: explore in 3D, AR (Android build), open the website, quit.</summary>
    public class MainMenuController : MonoBehaviour
    {
        public VCRDatabase database;
        public string explorerScene = "VCRExplorer";
        public string arScene = "VCR_AR";
        public Transform turntable;
        public float turntableSpeed = 12f;

        private Text _message;

        private void Start()
        {
            UIFactory.TextScale = PlayerPrefs.GetFloat("vcr.textScale", 1f);
            var canvas = UIFactory.CreateCanvas("MainMenuUI", 10);
            var panel = UIFactory.Panel(canvas.transform, "Menu", VCRColors.Card);
            UIFactory.Anchor(panel, new Vector2(0, 0), new Vector2(0, 1), new Vector2(40, 40), new Vector2(620, -40));
            var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(32, 32, 32, 32);
            v.spacing = 14;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;

            UIFactory.Label(panel, "DRG. VCR-001 - AR LEARNING MODULE", 15, VCRColors.Red, FontStyle.Bold);
            UIFactory.Label(panel, "Vapour Compression\nRefrigeration", 46, VCRColors.Navy, FontStyle.Bold);
            UIFactory.Label(panel, "Explore an original 3D model of a lab refrigeration rig. Tap the compressor, condenser, expansion device and evaporator to learn what each one does.", 19, VCRColors.Ink);
            UIFactory.Button(panel, "Explore in 3D", () => Load(explorerScene), VCRColors.Copper, Color.white, 24);
            UIFactory.Button(panel, "AR mode (Android, ARCore)", OpenAR, VCRColors.Cyan, Color.white, 22);
            UIFactory.Button(panel, "Open website", OpenWebsite, VCRColors.Navy, Color.white, 22);
            UIFactory.Button(panel, "Aa+  Larger text", () => ScalableText.SetScale(UIFactory.TextScale > 1.05f ? 1f : 1.25f), VCRColors.Paper, VCRColors.Navy, 20);
#if !UNITY_WEBGL
            UIFactory.Button(panel, "Quit", Application.Quit, VCRColors.Paper, VCRColors.Navy, 20);
#endif
            var lab = database != null ? database.Data?.labData : null;
            if (lab?.team != null && lab.team.Length > 0)
            {
                var names = string.Join("\n", System.Array.ConvertAll(lab.team, m => $"<b>{m.name}</b>  <color=#5d6677>{m.roll}</color>"));
                UIFactory.Label(panel, $"<color=#b8682f><b>PROJECT BY - {lab.branch?.ToUpperInvariant()}</b></color>\n{names}", 18, VCRColors.Navy);
            }
            if (!string.IsNullOrEmpty(lab?.guide))
                UIFactory.Label(panel, $"<color=#b8682f><b>GUIDE</b></color>  {lab.guide}\n<color=#5d6677>{lab.institution}</color>", 16, VCRColors.Navy);
            _message = UIFactory.Label(panel, "", 16, VCRColors.Red);
            UIFactory.Label(panel, "Educational visualisation - not CAD. No measured data is shown.", 14, VCRColors.Muted);
        }

        private void Update()
        {
            if (turntable != null) turntable.Rotate(0f, turntableSpeed * Time.deltaTime, 0f, Space.World);
        }

        private void Load(string scene)
        {
            if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
            else _message.text = $"Scene '{scene}' is not in the build settings. Run VCR > Setup Project in the Editor.";
        }

        private void OpenAR()
        {
#if UNITY_ANDROID || UNITY_IOS
            Load(arScene);
#else
            _message.text = "AR mode needs the Android (ARCore) build of this app on a supported phone. On this device, use the 3D explorer - or open the website on your phone for install-free WebAR.";
#endif
        }

        private void OpenWebsite()
        {
            if (database != null && !string.IsNullOrEmpty(database.websiteUrl)) Application.OpenURL(database.websiteUrl);
            else _message.text = "Website URL not set yet (VCRDatabase > Website Url).";
        }
    }
}
