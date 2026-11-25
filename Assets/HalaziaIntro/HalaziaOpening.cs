using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace Halazia.Presentation
{
    // A self-contained real-time cinematic. Put this in a separate opening scene.
    public sealed class HalaziaOpening : MonoBehaviour
    {
        [SerializeField] private string gameplayScene = "Assets/Scenes/halazia_Ship_World.unity";
        [SerializeField] private AudioClip openingMusic;
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.35f;
        private Camera lens;
        private AudioSource music;
        private AudioClip generatedMusic;
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Light> warningLights = new List<Light>();
        private float elapsed;
        private bool leaving;
        private string loadError;
        private AsyncOperation loading;
        private GUIStyle title, subtitle, small;
        private const float Duration = 60f;
        private readonly string[] captions = {
            "A transmission breaks the silence.",
            "Inside HALAZIA, the crew has vanished.",
            "Its automated defenders remain on watch.",
            "Somewhere above the cargo deck, an artifact waits.",
            "Enter through cargo. Retrieve the artifact. Return alive.",
            "The lost signal is still calling."
        };

        private void Start()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RenderSettings.ambientLight = new Color(0.035f, 0.055f, 0.08f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.008f, 0.018f, 0.03f);
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.025f;
            BuildSet();
            var cameraObject = new GameObject("Opening Camera");
            cameraObject.transform.SetParent(transform);
            lens = cameraObject.AddComponent<Camera>();
            lens.clearFlags = CameraClearFlags.SolidColor;
            lens.backgroundColor = Color.black;
            lens.nearClipPlane = 0.05f;
            lens.farClipPlane = 90f;
            lens.fieldOfView = 55f;
            cameraObject.AddComponent<AudioListener>();
            music = cameraObject.AddComponent<AudioSource>();
            music.spatialBlend = 0f;
            music.loop = true;
            music.clip = openingMusic != null ? openingMusic : (generatedMusic = MakeMusic());
            music.Play();
            PositionCamera();
        }

        private void Update()
        {
            if (leaving) return;
            elapsed += Time.unscaledDeltaTime;
            PositionCamera();
            for (int i = 0; i < warningLights.Count; i++)
                warningLights[i].intensity = 1.1f + 0.5f * Mathf.Sin(elapsed * 2.2f + i);
            music.volume = musicVolume * Mathf.Clamp01(elapsed / 3f) * Mathf.Clamp01((Duration - elapsed) / 3f);
            bool skip = Keyboard.current != null &&
                (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.escapeKey.wasPressedThisFrame);
            if (elapsed >= Duration || skip) StartCoroutine(EnterGame());
        }

        private IEnumerator EnterGame()
        {
            if (!Application.CanStreamedLevelBeLoaded(gameplayScene))
            {
                loadError = "Gameplay scene is missing from the build scene list. Run HALAZIA > Create Opening Scene again.";
                leaving = true;
                yield break;
            }
            leaving = true;
            float initial = music.volume;
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime)
            {
                music.volume = initial * (1f - t);
                yield return null;
            }
            loading = SceneManager.LoadSceneAsync(gameplayScene, LoadSceneMode.Single);
            yield return loading;
        }

        private void PositionCamera()
        {
            int shot = Mathf.Min(5, (int)(elapsed / 10f));
            float t = Mathf.SmoothStep(0f, 1f, (elapsed % 10f) / 10f);
            Vector3 from, to, look;
            switch (shot)
            {
                case 0: from = new Vector3(0, 1.65f, -10); to = new Vector3(0, 1.65f, -3); look = new Vector3(0, 1.6f, 17); break;
                case 1: from = new Vector3(-0.3f, 1.6f, 1); to = new Vector3(-0.4f, 1.5f, 4); look = new Vector3(-4.6f, 1.25f, 4); break;
                case 2: from = new Vector3(1.5f, 1.1f, 5); to = new Vector3(0.8f, 1.8f, 8); look = new Vector3(4.8f, 1.3f, 8); break;
                case 3: from = new Vector3(-1.7f, 1.4f, 11); to = new Vector3(-0.8f, 1.7f, 14); look = new Vector3(0, 1.45f, 17.6f); break;
                case 4: from = new Vector3(0, 2.5f, 16); to = new Vector3(0, 1.7f, 11); look = new Vector3(0, 1.3f, -10); break;
                default: from = new Vector3(0, 1.65f, -8); to = new Vector3(0, 1.65f, -4); look = new Vector3(0, 1.65f, 17); break;
            }
            lens.transform.position = Vector3.Lerp(from, to, t);
            lens.transform.LookAt(look);
        }

        private Material Surface(string label, Color color, float metal = 0.65f, Color? emission = null)
        {
            Shader shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null ? Shader.Find("Standard") : Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader) { name = label };
            mat.color = color;
            mat.SetFloat("_Metallic", metal);
            mat.SetFloat("_Smoothness", 0.52f);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
            }
            materials.Add(mat);
            return mat;
        }

        private GameObject Box(string label, Vector3 position, Vector3 scale, Material material)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = label;
            obj.transform.SetParent(transform, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(obj.GetComponent<Collider>());
            return obj;
        }

        private void Lamp(Vector3 pos, Color color, float power, float range, bool pulse = false)
        {
            var obj = new GameObject("Practical light");
            obj.transform.SetParent(transform, false);
            obj.transform.localPosition = pos;
            var light = obj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = power;
            light.range = range;
            light.shadows = LightShadows.None;
            if (pulse) warningLights.Add(light);
        }

        private void BuildSet()
        {
            var steel = Surface("Cold steel", new Color(0.18f, 0.24f, 0.29f));
            var dark = Surface("Blackened frame", new Color(0.035f, 0.05f, 0.065f));
            var floor = Surface("Deck plates", new Color(0.11f, 0.15f, 0.18f));
            var fabric = Surface("Bunk fabric", new Color(0.16f, 0.2f, 0.21f), 0f);
            var amber = Surface("Hazard yellow", new Color(0.65f, 0.4f, 0.08f), 0.2f);
            var blue = Surface("Blue fixtures", Color.white, 0f, new Color(0.7f, 1.7f, 2.5f));
            var red = Surface("Red fixtures", new Color(0.4f, 0.01f, 0.01f), 0f, new Color(2.8f, 0.04f, 0.02f));
            Box("Deck", new Vector3(0, -0.1f, 4), new Vector3(12, 0.2f, 32), floor);
            Box("Ceiling", new Vector3(0, 3.6f, 4), new Vector3(12, 0.2f, 32), dark);
            for (int z = -11; z <= 19; z += 2)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Box("Wall panel", new Vector3(side * 5.9f, 1.75f, z), new Vector3(0.2f, 3.5f, 1.96f), steel);
                    Box("Rib", new Vector3(side * 5.7f, 1.75f, z - 1), new Vector3(0.2f, 3.5f, 0.09f), dark);
                    Box("Ceiling pipe", new Vector3(side * 1.5f, 3.35f, z), new Vector3(0.12f, 0.12f, 2), steel);
                    for (int y = 0; y < 3; y++)
                        Box("Panel fastener", new Vector3(side * 5.78f, 0.5f + y, z - 0.7f), new Vector3(0.05f, 0.07f, 0.07f), dark);
                }
                Box("Deck seam", new Vector3(0, 0.009f, z), new Vector3(12, 0.015f, 0.035f), dark);
                Box("Ceiling strip", new Vector3(0, 3.44f, z), new Vector3(0.18f, 0.05f, 0.95f), blue);
                if ((z + 11) % 6 == 0) Lamp(new Vector3(0, 3.05f, z), new Color(0.5f, 0.76f, 1), 2.5f, 8);
                for (int side = -1; side <= 1; side += 2)
                    Box("Hazard edge", new Vector3(side * 1.9f, 0.018f, z), new Vector3(0.1f, 0.02f, 0.85f), amber);
            }
            // Holding cells on the left, with open barred fronts and two-tier bunks.
            for (int z = -2; z <= 10; z += 4)
            {
                Box("Cell partition", new Vector3(-4, 1.75f, z - 1.9f), new Vector3(3.5f, 3.5f, 0.12f), steel);
                for (int n = 0; n < 12; n++)
                    Box("Cell bar", new Vector3(-2.3f, 1.65f, z - 1.7f + n * 0.31f), new Vector3(0.055f, 3.3f, 0.055f), dark);
                for (int level = 0; level < 2; level++)
                {
                    Box("Bunk frame", new Vector3(-4.7f, 0.5f + level * 1.25f, z), new Vector3(1.4f, 0.12f, 2.9f), steel);
                    Box("Mattress", new Vector3(-4.7f, 0.62f + level * 1.25f, z), new Vector3(1.3f, 0.16f, 2.7f), fabric);
                    Box("Pillow", new Vector3(-4.7f, 0.76f + level * 1.25f, z + 1), new Vector3(0.8f, 0.12f, 0.5f), fabric);
                }
                Box("Cell lock", new Vector3(-2.22f, 1.25f, z), new Vector3(0.15f, 0.3f, 0.23f), steel);
                Box("Lock indicator", new Vector3(-2.12f, 1.27f, z), new Vector3(0.04f, 0.08f, 0.08f), red);
            }
            for (int z = -2; z <= 10; z += 4)
            {
                Box("Storage crate", new Vector3(4.5f, 0.65f, z), new Vector3(1.8f, 1.3f, 2.1f), steel);
                Box("Crate band", new Vector3(4.5f, 0.67f, z), new Vector3(1.86f, 0.15f, 2.16f), dark);
                Box("Wall console", new Vector3(5.6f, 1.9f, z), new Vector3(0.4f, 0.8f, 0.7f), dark);
                Box("Console screen", new Vector3(5.37f, 1.95f, z), new Vector3(0.03f, 0.35f, 0.5f), blue);
            }
            Box("Far bulkhead", new Vector3(0, 1.75f, 20), new Vector3(12, 3.5f, 0.3f), steel);
            Box("Sealed hatch", new Vector3(0, 1.5f, 19.78f), new Vector3(2.7f, 3, 0.2f), dark);
            Box("Hatch left", new Vector3(-0.65f, 1.5f, 19.6f), new Vector3(1.22f, 2.8f, 0.14f), steel);
            Box("Hatch right", new Vector3(0.65f, 1.5f, 19.6f), new Vector3(1.22f, 2.8f, 0.14f), steel);
            Box("Warning strip", new Vector3(0, 3.12f, 19.4f), new Vector3(2.7f, 0.1f, 0.12f), red);
            Lamp(new Vector3(0, 2.8f, 18.8f), new Color(1, 0.04f, 0.02f), 2f, 8, true);
            Box("Signal pedestal", new Vector3(0, 0.55f, 17.6f), new Vector3(0.8f, 1.1f, 0.8f), dark);
            var signal = Box("Signal visualization", new Vector3(0, 1.45f, 17.6f), new Vector3(0.24f, 0.5f, 0.24f), blue);
            signal.transform.localRotation = Quaternion.Euler(25, 45, 20);
            Lamp(new Vector3(0, 1.8f, 17.6f), new Color(0.1f, 0.7f, 1), 1.6f, 5);
        }

        private AudioClip MakeMusic()
        {
            // Original synthesized ambient bed: drone, slow minor chord and sonar pulses.
            const int rate = 22050, seconds = 20;
            var samples = new float[rate * seconds];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / rate;
                float envelope = Mathf.Pow(Mathf.Sin(Mathf.PI * t / seconds), 2f);
                float chord = Mathf.Sin(2 * Mathf.PI * 110 * t) + 0.5f * Mathf.Sin(2 * Mathf.PI * 130.8128f * t)
                    + 0.3f * Mathf.Sin(2 * Mathf.PI * 164.8138f * t);
                float beat = t % 4f;
                float ping = Mathf.Sin(2 * Mathf.PI * 880 * t) * Mathf.Exp(-beat * 5f);
                samples[i] = envelope * (0.08f * chord + 0.07f * ping);
            }
            var clip = AudioClip.Create("HALAZIA original ambient signal", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnGUI()
        {
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 46, fontStyle = FontStyle.Bold };
                subtitle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 23, wordWrap = true };
                small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontSize = 14 };
            }
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1920f, Screen.height / 1080f, 1));
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, 1920, 115), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, 920, 1920, 160), Texture2D.whiteTexture);
            GUI.color = new Color(0.58f, 0.82f, 0.94f);
            GUI.Label(new Rect(90, 30, 1740, 50), "H A L A Z I A   /   T H E   L O S T   S I G N A L", subtitle);
            int shot = Mathf.Min(5, (int)(elapsed / 10));
            float local = elapsed % 10;
            float alpha = Mathf.Clamp01(local / 1.4f) * Mathf.Clamp01((10 - local) / 1.4f);
            GUI.color = new Color(0.85f, 0.92f, 0.98f, alpha);
            GUI.Label(new Rect(200, 945, 1520, 65), captions[shot], subtitle);
            if (shot == 5)
                GUI.Label(new Rect(200, 410, 1520, 110), "HALAZIA: THE LOST SIGNAL", title);
            GUI.color = new Color(0.65f, 0.74f, 0.8f);
            GUI.Label(new Rect(1300, 1020, 530, 35), "SPACE / ESC TO SKIP", small);
            if (leaving || elapsed < 2f)
            {
                GUI.color = new Color(0, 0, 0, leaving ? 1 : 1 - Mathf.Clamp01(elapsed / 2));
                GUI.DrawTexture(new Rect(0, 0, 1920, 1080), Texture2D.whiteTexture);
                if (leaving)
                {
                    GUI.color = Color.white;
                    GUI.Label(new Rect(200, 470, 1520, 140), loadError ?? "Entering cargo deck...", subtitle);
                }
            }
            GUI.color = Color.white;
            GUI.matrix = previous;
        }

        private void OnDestroy()
        {
            foreach (var material in materials) if (material != null) Destroy(material);
            if (generatedMusic != null) Destroy(generatedMusic);
        }
    }
}
