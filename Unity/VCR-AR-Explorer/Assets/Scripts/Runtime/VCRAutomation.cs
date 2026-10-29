using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VCR
{
    /// <summary>
    /// Command-line hooks for smoke tests and documentation screenshots of a built player:
    ///   -vcrScene VCRExplorer  -vcrSelect compressor  -vcrExplode 0.6  -vcrCycle 2
    ///   -vcrShot C:/path/shot.png   (captures after the scene settles, writes a report, then quits)
    /// Has no effect when no -vcr* arguments are given.
    /// </summary>
    public class VCRAutomation : MonoBehaviour
    {
        private static bool _sceneSwitched;

        private static string Arg(string name)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        private IEnumerator Start()
        {
            var scene = Arg("-vcrScene");
            if (!_sceneSwitched && scene != null && SceneManager.GetActiveScene().name != scene)
            {
                _sceneSwitched = true;
                SceneManager.LoadScene(scene);
                yield break;
            }
            var shot = Arg("-vcrShot");
            if (shot == null) yield break;

            yield return new WaitForSeconds(1.5f);
            var sel = FindAnyObjectByType<SelectionManager>();
            var ui = FindAnyObjectByType<VCRExplorerUI>();
            var cycle = FindAnyObjectByType<CycleController>();
            var binder = FindAnyObjectByType<VCRModelBinder>();
            if (float.TryParse(Arg("-vcrExplode") ?? "", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ex) && ui != null) ui.SetExplodeExternal(ex);
            if (int.TryParse(Arg("-vcrCycle") ?? "", out var st) && cycle != null) cycle.Go(Mathf.Clamp(st - 1, 0, 3));
            else if (Arg("-vcrSelect") != null && sel != null) sel.Select(Arg("-vcrSelect"));
            yield return new WaitForSeconds(2.5f);

            Directory.CreateDirectory(Path.GetDirectoryName(shot));
            ScreenCapture.CaptureScreenshot(shot);
            string report = $"scene={SceneManager.GetActiveScene().name}\nbound={(binder != null && binder.IsBound)}\nparts={(binder != null ? binder.Parts.Count : 0)}\n" +
                            $"missing={(binder != null ? string.Join(",", binder.MissingNodes) : "n/a")}\nselected={(sel != null ? sel.Selected : "n/a")}\n";
            File.WriteAllText(Path.ChangeExtension(shot, ".txt"), report);
            yield return new WaitForSeconds(1f);
            Application.Quit();
        }
    }
}
