// Editor automation for the VCR AR Explorer project.
//
//   Menu:  VCR ▸ Setup Project        (idempotent – safe to run again after regenerating the GLB)
//   Batch: Unity.exe -batchmode -projectPath <proj> -executeMethod VCR.EditorTools.VCRProjectSetup.SetupFromCommandLine -quit
//
// Creates URP settings, URP materials (from the GLB's own material table), the VCRDatabase asset,
// the VCR_Rig prefab (colliders + VCRSelectable on every component), the MainMenu, VCRExplorer
// and VCR_AR scenes, build settings and Android/ARCore player settings.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using VCR.AR;
using Object = UnityEngine.Object;

namespace VCR.EditorTools
{
    public static class VCRProjectSetup
    {
        public const string ModelPath = "Assets/Models/VCR_System.glb";
        public const string DataDir = "Assets/Data";
        public const string MatDir = "Assets/Materials";
        public const string ScenesDir = "Assets/Scenes";
        public const string PrefabPath = "Assets/Prefabs/VCR_Rig.prefab";
        public const string DatabasePath = "Assets/Data/VCRDatabase.asset";
        public static readonly string[] SceneNames = { "MainMenu", "VCRExplorer", "VCR_AR" };
        public static readonly Color Paper = new Color(0.965f, 0.953f, 0.918f);

        [MenuItem("VCR/Setup Project", priority = 1)]
        public static void SetupAll()
        {
            var log = new StringBuilder();
            EnsureFolders();
            bool urp = ConfigureURP(log);
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var mats = CreateMaterials(log);
            var db = CreateDatabase();
            var prefab = CreateRigPrefab(db, mats, log);
            if (prefab == null) throw new Exception("VCR_Rig prefab could not be created – is the GLB imported (glTFast installed)?\n" + log);
            var planePrefab = CreateARPlanePrefab(mats);
            BuildMainMenu(db, prefab);
            BuildExplorer(db, prefab, mats);
            BuildAR(db, prefab, mats, planePrefab);
            EditorBuildSettings.scenes = SceneNames.Select(n => new EditorBuildSettingsScene($"{ScenesDir}/{n}.unity", true)).ToArray();
            ConfigurePlayer(log);
            AssetDatabase.SaveAssets();
            log.AppendLine($"URP configured: {urp}");
            Debug.Log("[VCR] Setup complete.\n" + log);
            WriteLog("setup.txt", log.ToString());
        }

        public static void SetupFromCommandLine()
        {
            try
            {
                SetupAll();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[VCR] Setup failed: " + e);
                WriteLog("setup.txt", "FAILED\n" + e);
                EditorApplication.Exit(1);
            }
        }

        // ------------------------------------------------------------------ helpers
        private static void EnsureFolders()
        {
            foreach (var d in new[] { "Scenes", "Scripts", "Models", "Materials", "Prefabs", "UI", "Data", "AR", "Animations", "Documentation", "Settings" })
                if (!AssetDatabase.IsValidFolder("Assets/" + d)) AssetDatabase.CreateFolder("Assets", d);
        }

        public static void WriteLog(string file, string text)
        {
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../logs"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, file), text);
        }

        private static bool ConfigureURP(StringBuilder log)
        {
            try
            {
                var assetPath = "Assets/Settings/VCR_URP.asset";
                var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
                if (urp == null)
                {
                    var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(rendererData, "Assets/Settings/VCR_URP_Renderer.asset");
                    urp = UniversalRenderPipelineAsset.Create(rendererData);
                    urp.msaaSampleCount = 4;
                    urp.shadowDistance = 6f;
                    AssetDatabase.CreateAsset(urp, assetPath);
                }
                GraphicsSettings.defaultRenderPipeline = urp;
                for (int i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    QualitySettings.renderPipeline = urp;
                }
                PlayerSettings.colorSpace = ColorSpace.Linear;
                log.AppendLine("URP asset: " + assetPath);
                return true;
            }
            catch (Exception e)
            {
                log.AppendLine("URP setup failed, staying on built-in pipeline: " + e.Message);
                return false;
            }
        }

        private static Shader LitShader => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        private static Shader UnlitShader => Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

        // --- GLB material table (read directly from the GLB JSON chunk)
        [Serializable] private class GPbr { public float[] baseColorFactor; public float metallicFactor = 1; public float roughnessFactor = 1; }
        [Serializable] private class GMat { public string name; public GPbr pbrMetallicRoughness; public string alphaMode; }
        [Serializable] private class GDoc { public GMat[] materials; }

        private static GDoc ReadGltfJson(string path)
        {
            var bytes = File.ReadAllBytes(path);
            int len = BitConverter.ToInt32(bytes, 12);
            return JsonUtility.FromJson<GDoc>(Encoding.UTF8.GetString(bytes, 20, len));
        }

        private static Dictionary<string, Material> CreateMaterials(StringBuilder log)
        {
            var result = new Dictionary<string, Material>();
            var doc = ReadGltfJson(ModelPath);
            foreach (var g in doc.materials)
            {
                var p = g.pbrMetallicRoughness;
                var lin = p.baseColorFactor;
                var col = new Color(lin[0], lin[1], lin[2], 1f).gamma; // glTF stores linear colour
                col.a = lin[3];
                var mat = LoadOrCreate($"{MatDir}/{g.name}.mat", LitShader);
                SetLit(mat, col, p.metallicFactor, 1f - p.roughnessFactor, g.alphaMode == "BLEND");
                result[g.name] = mat;
            }
            var ghost = LoadOrCreate($"{MatDir}/Ghost.mat", LitShader);
            SetLit(ghost, new Color(0.62f, 0.67f, 0.74f, 0.12f), 0f, 0.3f, true);
            result["__Ghost"] = ghost;
            var arrow = LoadOrCreate($"{MatDir}/FlowArrow.mat", Shader.Find("VCR/FlowArrowOnTop") ?? UnlitShader);
            arrow.SetColor("_BaseColor", Color.white);
            arrow.color = Color.white;
            result["__Arrow"] = arrow;
            var floor = LoadOrCreate($"{MatDir}/Floor.mat", LitShader);
            SetLit(floor, new Color(0.93f, 0.92f, 0.89f), 0f, 0.15f, false);
            result["__Floor"] = floor;
            var plane = LoadOrCreate($"{MatDir}/ARPlane.mat", LitShader);
            SetLit(plane, new Color(0.08f, 0.57f, 0.69f, 0.25f), 0f, 0.2f, true);
            result["__Plane"] = plane;
            foreach (var m in result.Values) EditorUtility.SetDirty(m);
            log.AppendLine($"Materials: {result.Count} ({string.Join(", ", result.Keys)})");
            return result;
        }

        private static Material LoadOrCreate(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = shader;
            return m;
        }

        private static void SetLit(Material m, Color c, float metallic, float smoothness, bool transparent)
        {
            m.SetColor("_BaseColor", c);
            m.color = c;
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Glossiness", smoothness);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (transparent)
            {
                m.SetFloat("_Surface", 1);
                m.SetFloat("_Blend", 0);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                m.SetFloat("_Surface", 0);
                m.SetFloat("_ZWrite", 1);
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Opaque");
                m.renderQueue = -1;
            }
        }

        private static VCRDatabase CreateDatabase()
        {
            var db = AssetDatabase.LoadAssetAtPath<VCRDatabase>(DatabasePath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<VCRDatabase>();
                AssetDatabase.CreateAsset(db, DatabasePath);
            }
            db.componentsJson = AssetDatabase.LoadAssetAtPath<TextAsset>($"{DataDir}/components.json");
            db.flowJson = AssetDatabase.LoadAssetAtPath<TextAsset>($"{DataDir}/flow_paths.json");
            db.Reload();
            EditorUtility.SetDirty(db);
            return db;
        }

        private static GameObject CreateRigPrefab(VCRDatabase db, Dictionary<string, Material> mats, StringBuilder log)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                log.AppendLine("GLB not imported as a GameObject (glTFast missing?)");
                return null;
            }
            var rig = new GameObject("VCR_Rig");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = "VCR_System_Model";
            inst.transform.SetParent(rig.transform, false);

            // swap glTFast's imported materials for the editable URP materials in Assets/Materials
            int swapped = 0;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var arr = r.sharedMaterials;
                for (int i = 0; i < arr.Length; i++)
                {
                    var key = arr[i] != null ? arr[i].name.Replace(" (Instance)", "") : "";
                    if (mats.TryGetValue(key, out var m)) { arr[i] = m; swapped++; }
                }
                r.sharedMaterials = arr;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            var binder = rig.AddComponent<VCRModelBinder>();
            binder.database = db;
            binder.modelRoot = inst.transform;
            binder.ghostMaterial = mats["__Ghost"];
            binder.Bind();
            log.AppendLine($"Rig: {binder.Parts.Count} components bound, {swapped} material slots remapped, missing: [{string.Join(", ", binder.MissingNodes)}]");
            foreach (var p in binder.Parts.Values) EditorUtility.SetDirty(p);
            var prefab = PrefabUtility.SaveAsPrefabAsset(rig, PrefabPath);
            Object.DestroyImmediate(rig);
            return prefab;
        }

        private static GameObject CreateARPlanePrefab(Dictionary<string, Material> mats)
        {
            const string path = "Assets/AR/ARPlane.prefab";
            var go = new GameObject("ARPlane", typeof(ARPlane), typeof(ARPlaneMeshVisualizer), typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            go.GetComponent<MeshRenderer>().sharedMaterial = mats["__Plane"];
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ------------------------------------------------------------------ scenes
        private static void AddLights()
        {
            var sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.4f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -150f, 0f);
            var fill = new GameObject("Fill Light").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(25f, 40f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.85f, 0.87f, 0.9f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.63f, 0.64f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.3f, 0.32f);
            RenderSettings.skybox = null;
        }

        private static Camera AddCamera(string name)
        {
            var go = new GameObject(name, typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Paper;
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 50f;
            return cam;
        }

        private static void AddEventSystem()
        {
            var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        private static void AddFloor(Dictionary<string, Material> mats)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 0.8f;
            floor.GetComponent<MeshRenderer>().sharedMaterial = mats["__Floor"];
            Object.DestroyImmediate(floor.GetComponent<Collider>()); // never intercept component picking
        }

        private static GameObject PlaceRig(GameObject prefab, Transform parent = null)
        {
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (parent) rig.transform.SetParent(parent, false);
            return rig;
        }

        private static void Save(Scene s, string name)
        {
            EditorSceneManager.SaveScene(s, $"{ScenesDir}/{name}.unity");
        }

        private static void BuildMainMenu(VCRDatabase db, GameObject prefab)
        {
            var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AddLights();
            var cam = AddCamera("Main Camera");
            cam.transform.position = new Vector3(-0.55f, 0.75f, 2.1f);
            cam.transform.LookAt(new Vector3(-0.55f, 0.38f, 0f));
            var turntable = new GameObject("Turntable").transform;
            turntable.position = new Vector3(-1.05f, 0f, 0f);
            PlaceRig(prefab, turntable);
            var menu = new GameObject("MainMenu").AddComponent<MainMenuController>();
            menu.database = db;
            menu.turntable = turntable;
            new GameObject("Automation").AddComponent<VCRAutomation>();
            AddEventSystem();
            Save(s, "MainMenu");
        }

        private static GameObject AddManagers(VCRDatabase db, VCRModelBinder binder, Camera cam, Dictionary<string, Material> mats, OrbitCameraController orbit, bool arMode)
        {
            var mgr = new GameObject("VCR_Managers");
            var sel = mgr.AddComponent<SelectionManager>();
            sel.binder = binder;
            sel.cam = cam;
            var ex = mgr.AddComponent<ExplodeController>();
            ex.binder = binder;
            var flow = mgr.AddComponent<FlowAnimator>();
            flow.database = db;
            flow.modelSpace = binder.modelRoot;
            flow.explode = ex;
            flow.arrowMaterial = mats["__Arrow"];
            var cycle = mgr.AddComponent<CycleController>();
            cycle.database = db;
            cycle.selection = sel;
            cycle.flow = flow;
            cycle.explode = ex;
            var labels = mgr.AddComponent<LabelManager>();
            labels.binder = binder;
            labels.selection = sel;
            labels.explode = ex;
            labels.cam = cam;
            var ui = mgr.AddComponent<VCRExplorerUI>();
            ui.database = db;
            ui.binder = binder;
            ui.selection = sel;
            ui.orbit = orbit;
            ui.explode = ex;
            ui.flow = flow;
            ui.cycle = cycle;
            ui.labels = labels;
            ui.arMode = arMode;
            mgr.AddComponent<VCRAutomation>();
            return mgr;
        }

        private static void BuildExplorer(VCRDatabase db, GameObject prefab, Dictionary<string, Material> mats)
        {
            var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AddLights();
            var cam = AddCamera("Main Camera");
            var orbit = cam.gameObject.AddComponent<OrbitCameraController>();
            AddFloor(mats);
            var rig = PlaceRig(prefab);
            AddManagers(db, rig.GetComponent<VCRModelBinder>(), cam, mats, orbit, false);
            AddEventSystem();
            Save(s, "VCRExplorer");
        }

        private static void BuildAR(VCRDatabase db, GameObject prefab, Dictionary<string, Material> mats, GameObject planePrefab)
        {
            var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var sessionGo = new GameObject("AR Session", typeof(ARSession), typeof(ARInputManager));
            var originGo = new GameObject("XR Origin", typeof(XROrigin), typeof(ARPlaneManager), typeof(ARRaycastManager));
            var offset = new GameObject("Camera Offset").transform;
            offset.SetParent(originGo.transform, false);
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(ARCameraManager), typeof(ARCameraBackground));
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(offset, false);
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.05f;
            var tpd = camGo.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            var pos = new InputAction("Position", expectedControlType: "Vector3");
            pos.AddBinding("<XRHMD>/centerEyePosition");
            pos.AddBinding("<HandheldARInputDevice>/devicePosition");
            var rot = new InputAction("Rotation", expectedControlType: "Quaternion");
            rot.AddBinding("<XRHMD>/centerEyeRotation");
            rot.AddBinding("<HandheldARInputDevice>/deviceRotation");
            tpd.positionInput = new InputActionProperty(pos);
            tpd.rotationInput = new InputActionProperty(rot);
            var origin = originGo.GetComponent<XROrigin>();
            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offset.gameObject;
            var planes = originGo.GetComponent<ARPlaneManager>();
            planes.planePrefab = planePrefab;
            planes.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Horizontal;

            var rig = PlaceRig(prefab);
            var binder = rig.GetComponent<VCRModelBinder>();
            var mgr = AddManagers(db, binder, cam, mats, null, true);
            var place = mgr.AddComponent<ARPlacementController>();
            place.raycastManager = originGo.GetComponent<ARRaycastManager>();
            place.planeManager = planes;
            place.session = sessionGo.GetComponent<ARSession>();
            place.rig = rig;
            place.selection = mgr.GetComponent<SelectionManager>();
            AddEventSystem();
            Save(s, "VCR_AR");
        }

        // ------------------------------------------------------------------ player / XR
        private static void ConfigurePlayer(StringBuilder log)
        {
            PlayerSettings.companyName = "VCRARExplorer";
            PlayerSettings.productName = "VCR AR Explorer";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.vcrarexplorer.app");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            log.AppendLine("Android: IL2CPP, ARM64, min API 26, OpenGLES3");
            try
            {
                VCRXRConfig.EnableARCore(log);
            }
            catch (Exception e)
            {
                log.AppendLine("ARCore loader not configured automatically: " + e.Message + " – enable it in Project Settings ▸ XR Plug-in Management ▸ Android ▸ ARCore.");
            }
        }
    }
}
