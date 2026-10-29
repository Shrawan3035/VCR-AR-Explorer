// Batch entry points:
//   -executeMethod VCR.EditorTools.VCRBuildAndValidate.Validate       → Unity/logs/validation.txt (exit 1 on failure)
//   -executeMethod VCR.EditorTools.VCRBuildAndValidate.BuildWindows   → Builds/Windows/VCR-AR-Explorer.exe
//   -executeMethod VCR.EditorTools.VCRBuildAndValidate.BuildAndroid   → Builds/Android/VCR-AR-Explorer.apk (needs Android SDK/NDK/JDK modules)
using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VCR.EditorTools
{
    public static class VCRBuildAndValidate
    {
        [MenuItem("VCR/Validate Project", priority = 2)]
        public static void ValidateMenu() => Debug.Log(RunValidation(out _));

        public static void Validate()
        {
            var report = RunValidation(out bool ok);
            VCRProjectSetup.WriteLog("validation.txt", report);
            Debug.Log(report);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static string RunValidation(out bool ok)
        {
            var sb = new StringBuilder();
            bool allOk = true; // locals can be captured by the local function; out params cannot
            void Check(bool cond, string what)
            {
                sb.AppendLine((cond ? "PASS  " : "FAIL  ") + what);
                if (!cond) allOk = false;
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(VCRProjectSetup.ModelPath);
            Check(model != null, "GLB imported as model asset: " + VCRProjectSetup.ModelPath);
            var db = AssetDatabase.LoadAssetAtPath<VCRDatabase>(VCRProjectSetup.DatabasePath);
            Check(db != null && db.Data != null, "VCRDatabase parses components.json");
            Check(db?.Data?.components?.Length == 12, $"12 component records (found {db?.Data?.components?.Length})");
            Check(db?.Data?.stages?.Length == 4, "4 cycle stages");
            Check(db?.Flow?.segments?.Length == 7, $"7 flow segments (found {db?.Flow?.segments?.Length})");
            foreach (var n in VCRProjectSetup.SceneNames)
                Check(EditorBuildSettings.scenes.Any(s => s.path.EndsWith(n + ".unity") && s.enabled), $"Scene in build settings: {n}");

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/VCRExplorer.unity", OpenSceneMode.Single);
            var binder = UnityEngine.Object.FindAnyObjectByType<VCRModelBinder>();
            Check(binder != null, "VCRExplorer contains a VCRModelBinder");
            if (binder != null)
            {
                binder.Bind();
                Check(binder.MissingNodes.Count == 0, "All component nodes found in model" + (binder.MissingNodes.Count > 0 ? ": missing " + string.Join(",", binder.MissingNodes) : ""));
                foreach (var kv in binder.Parts)
                {
                    int colliders = kv.Value.GetComponentsInChildren<Collider>(true).Length;
                    int renderers = kv.Value.GetComponentsInChildren<Renderer>(true).Length;
                    Check(colliders > 0 && renderers > 0, $"{kv.Value.name} → '{kv.Key}' ({renderers} renderers, {colliders} colliders)");
                }
                // ray from in front of the compressor must hit the compressor's collider
                Physics.SyncTransforms();
                var comp = binder.Parts["compressor"];
                var c = comp.WorldBounds().center;
                bool hit = Physics.Raycast(c + Vector3.forward * 2f, Vector3.back, out var h, 5f) && h.collider.GetComponentInParent<VCRSelectable>() == comp;
                Check(hit, "Raycast from the front selects the compressor");
            }
            foreach (var t in new[] { typeof(SelectionManager), typeof(ExplodeController), typeof(FlowAnimator), typeof(CycleController), typeof(LabelManager), typeof(VCRExplorerUI) })
                Check(UnityEngine.Object.FindAnyObjectByType(t) != null, "VCRExplorer has " + t.Name);
            Check(UnityEngine.Object.FindAnyObjectByType<OrbitCameraController>() != null, "VCRExplorer has OrbitCameraController");
            ok = allOk;
            sb.Insert(0, (ok ? "VALIDATION PASSED" : "VALIDATION FAILED") + $"  ({DateTime.Now:yyyy-MM-dd HH:mm})\n");
            return sb.ToString();
        }

        public static void BuildWindows()
        {
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/MainMenu.unity", "Assets/Scenes/VCRExplorer.unity" },
                locationPathName = "Builds/Windows/VCR-AR-Explorer.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            Finish(BuildPipeline.BuildPlayer(opts), "build_windows.txt");
        }

        public static void BuildAndroid()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            var opts = new BuildPlayerOptions
            {
                scenes = VCRProjectSetup.SceneNames.Select(n => $"Assets/Scenes/{n}.unity").ToArray(),
                locationPathName = "Builds/Android/VCR-AR-Explorer.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            Finish(BuildPipeline.BuildPlayer(opts), "build_android.txt");
        }

        private static void Finish(BuildReport r, string logName)
        {
            var s = r.summary;
            var text = $"result={s.result}\nerrors={s.totalErrors}\nwarnings={s.totalWarnings}\nsize={s.totalSize}\noutput={s.outputPath}\ntime={s.totalTime}\n";
            foreach (var step in r.steps)
                foreach (var m in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                    text += "ERROR: " + m.content + "\n";
            VCRProjectSetup.WriteLog(logName, text);
            EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
