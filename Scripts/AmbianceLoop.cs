using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace HoldfastAmbiance
{
    /// <summary>
    /// Sons et fumée de tir. Un objet créé dans le code (pas de scène : le serveur reste compatible console).
    /// Son : un tir au hasard, joué en 3D à la position du tireur, avec le retard du son (343 m/s), un volume qui baisse avec
    /// la distance et un filtre qui coupe les aigus au loin (une salve lointaine gronde). Nombre de voix limité : une salve de
    /// 50 bots ne sature pas le mixage.
    /// </summary>
    public class AmbianceLoop : MonoBehaviour
    {
        // ---- Réglages ----
        private const int Voices = 24;
        private const float SpeedOfSound = 343f;
        private const float MinDistance = 20f;     // pleine force en deçà
        private const float MaxDistance = 900f;    // plus rien au-delà
        private const float NearSkip = 6f;         // tout près, le son du jeu suffit
        private const float Volume = 1f;
        private const float FarCutoff = 900f;      // Hz, tir le plus lointain
        private const float NearCutoff = 16000f;   // Hz, tir proche
        private const float SmokeLife = 8f;        // s

        private static AmbianceLoop _instance;
        /// <summary>Tirs du mod joués (!musket on / off) ; désactivés tant que le joueur ne l'a pas demandé.</summary>
        public static bool Enabled;
        // Tirs encore détaillés dans le journal (!musket debug ; les 30 premiers par défaut).
        public static int DebugShots = 30;

        // ---- Son de tir du jeu : seuls les sons de tir (clips « au_musket_N », groupe Weapons) sont coupés ; rechargement,
        // visée, mêlée et tous les autres sons du jeu restent intacts. Le réglage de volume du jeu n'est plus touché. ----
        private static readonly string[] GameShotPrefixes = { "au_musket_" };
        /// <summary>Sons de tir du jeu coupés (!musket on).</summary>
        public static bool MuteGameShots { get; private set; }
        private readonly HashSet<AudioSource> _muted = new HashSet<AudioSource>();
        private int _scanFrames;   // images où chercher les sources de tir du jeu après un tir

        public static void SetMuteGameShots(bool mute)
        {
            MuteGameShots = mute;
            if (_instance != null && !mute) _instance.UnmuteAll();
            Debug.Log("[AMB] Son de tir du jeu : " + (mute ? "coupé (tirs seulement)" : "normal") + ".");
        }

        private static bool IsGameShot(AudioClip clip)
        {
            if (clip == null) return false;
            foreach (var p in GameShotPrefixes) if (clip.name.StartsWith(p)) return true;
            return false;
        }

        private void UnmuteAll()
        {
            foreach (var src in _muted) if (src != null) src.mute = false;
            _muted.Clear();
        }

        // Le jeu réutilise ses sources : une source coupée qui joue ensuite un autre son (rechargement…) est rétablie.
        private void TickGameShots()
        {
            if (_muted.Count > 0)
            {
                List<AudioSource> back = null;
                foreach (var src in _muted)
                    if (src == null || !IsGameShot(src.clip)) (back ?? (back = new List<AudioSource>())).Add(src);
                if (back != null)
                    foreach (var src in back) { if (src != null) src.mute = false; _muted.Remove(src); }
            }
            if (!MuteGameShots || _scanFrames <= 0) return;
            _scanFrames--;
            foreach (var src in FindObjectsOfType<AudioSource>())
            {
                if (src == null || src.mute || _sources.Contains(src) || !IsGameShot(src.clip)) continue;
                src.mute = true;
                _muted.Add(src);
            }
        }

        private void OnDestroy()
        {
            UnmuteAll();
            if (_smoke != null) Destroy(_smoke.gameObject);
        }

        // ---- Debug : structure du mixage du jeu et sons de tir du jeu (pour couper le tir sans couper le rechargement) ----
        private static readonly string[] ParamGuesses =
            { "weaponsVol", "weaponVol", "firingVol", "fireVol", "shotVol", "gunshotVol", "reloadVol", "sfxVol", "effectsVol",
              "masterVol", "musicVol", "voiceVol", "ambientVol", "ambienceVol", "uiVol", "footstepsVol", "explosionsVol" };
        private readonly List<float> _probes = new List<float>();

        public static void DumpMixers()
        {
            foreach (var m in Resources.FindObjectsOfTypeAll<AudioMixer>())
            {
                if (m == null) continue;
                var line = "[AMB] Mixage « " + m.name + " » groupes :";
                foreach (var g in m.FindMatchingGroups(string.Empty)) line += " " + g.name + ";";
                line += " paramètres :";
                foreach (var n in ParamGuesses) if (m.GetFloat(n, out var v)) line += " " + n + "=" + v.ToString("0.0") + ";";
                Debug.Log(line);
            }
        }

        // Sons du jeu qui jouent près de l'écouteur juste après son propre tir : nom du son et groupe de mixage.
        private void ProbeGameSounds()
        {
            var at = ListenerPosition();
            foreach (var src in FindObjectsOfType<AudioSource>())
            {
                if (src == null || !src.isPlaying || src.clip == null || _sources.Contains(src)) continue;
                if ((src.transform.position - at).sqrMagnitude > 25f) continue;
                Debug.Log("[AMB] Son du jeu : « " + src.clip.name + " » groupe « "
                          + (src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : "aucun") + " ».");
            }
        }

        private void Update()
        {
            if (_probes.Count > 0 && Time.time >= _probes[0]) { _probes.RemoveAt(0); ProbeGameSounds(); }
            TickGameShots();
        }

        private readonly List<AudioClip> _clips = new List<AudioClip>();
        private readonly List<AudioSource> _sources = new List<AudioSource>();
        private readonly List<AudioLowPassFilter> _filters = new List<AudioLowPassFilter>();
        private readonly List<float> _busyUntil = new List<float>();
        private ParticleSystem _smoke;
        private int _lastClip = -1;
        private readonly List<int> _bag = new List<int>();

        public static void Ensure(List<AudioClip> clips)
        {
            if (_instance == null)
            {
                // Le jeu recharge le mod à chaque connexion ou changement de carte, mais l'objet de la fois précédente survit
                // (DontDestroyOnLoad) et continuait d'imposer son réglage du son des tirs : on le supprime.
                for (var old = GameObject.Find("AmbianceLoop"); old != null; old = GameObject.Find("AmbianceLoop"))
                {
                    old.name = "AmbianceLoop (ancien)";
                    Destroy(old);
                    Debug.Log("[AMB] Ancienne boucle d'ambiance supprimée.");
                }
                var go = new GameObject("AmbianceLoop");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<AmbianceLoop>();
                _instance.Build();
            }
            _instance._clips.Clear();
            _instance._clips.AddRange(clips);
            _instance._bag.Clear();
        }

        public static void Shot(Vector3 shooter, Vector3 forward, bool local)
        {
            if (_instance == null)
            {
                if (DebugShots > 0) { DebugShots--; Debug.Log("[AMB] Tir ignoré : boucle d'ambiance absente."); }
                return;
            }
            _instance.Play(shooter, forward, local);
        }

        private void Build()
        {
            for (var i = 0; i < Voices; i++)
            {
                var v = new GameObject("AmbianceVoice" + i);
                v.transform.SetParent(transform, false);
                var src = v.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = MinDistance;
                src.maxDistance = MaxDistance;
                src.dopplerLevel = 0f;
                src.spread = 30f;
                _sources.Add(src);
                _filters.Add(v.AddComponent<AudioLowPassFilter>());
                _busyUntil.Add(0f);
            }
            _smoke = BuildSmoke();
        }

        private void Play(Vector3 shooter, Vector3 forward, bool local)
        {
            var listener = ListenerPosition();
            if (local) shooter = listener - Vector3.up * 1.5f;
            var distance = local ? 0f : Vector3.Distance(listener, shooter);
            var muzzle = shooter + Vector3.up * 1.5f + forward * 1.1f;
            // Son propre tir : pas de fumée ajoutée (celle du jeu suffit ; la nôtre se formait devant la caméra).
            if (MuteGameShots) _scanFrames = 3;
            if (_smoke != null && !local) Puff(muzzle, forward);
            // Juste après le tir, puis pendant le rechargement (~10 s).
            if (local && DebugShots > 0)
            {
                _probes.Clear();
                foreach (var dt in new[] { 0.05f, 1.5f, 3f, 4.5f, 6f, 7.5f, 9f, 10.5f }) _probes.Add(Time.time + dt);
            }
            var why = !Enabled ? "mod off" : _clips.Count == 0 ? "aucun son chargé"
                : distance < NearSkip && !MuteGameShots && !local ? "trop proche" : distance > MaxDistance ? "trop loin" : null;
            var voice = why == null ? FreeVoice() : -1;
            if (why == null && voice < 0) why = "plus de voix libre";
            if (DebugShots > 0)
            {
                DebugShots--;
                Debug.Log("[AMB] Tir à " + distance.ToString("0") + " m (local " + local + ", écouteur " + (_listener != null)
                          + ") : fumée " + (_smoke != null && !local ? "oui" : _smoke == null ? "ABSENTE" : "non (local)")
                          + ", son " + (why ?? "joué (" + _clips.Count + " sons)") + ".");
            }
            if (why != null) return;
            var src = _sources[voice];
            var clip = PickClip();
            src.transform.position = muzzle;
            src.clip = clip;
            src.volume = Volume;
            src.pitch = Random.Range(0.94f, 1.06f);
            // Au loin, les aigus se perdent : coupure de NearCutoff (proche) à FarCutoff (MaxDistance), en échelle logarithmique.
            var t = Mathf.InverseLerp(Mathf.Log(MinDistance), Mathf.Log(MaxDistance), Mathf.Log(Mathf.Max(distance, MinDistance)));
            _filters[voice].cutoffFrequency = Mathf.Exp(Mathf.Lerp(Mathf.Log(NearCutoff), Mathf.Log(FarCutoff), t));
            var delay = distance / SpeedOfSound;
            src.PlayDelayed(delay);
            _busyUntil[voice] = Time.time + delay + clip.length / src.pitch;
        }

        private int FreeVoice()
        {
            var now = Time.time;
            for (var i = 0; i < _sources.Count; i++) if (now >= _busyUntil[i]) return i;
            return -1;
        }

        private AudioClip PickClip()
        {
            // Tirage sans remise : les 40 tirs passent tous avant qu'un tir revienne.
            if (_bag.Count == 0)
            {
                for (var k = 0; k < _clips.Count; k++) _bag.Add(k);
                for (var k = _bag.Count - 1; k > 0; k--) { var r = Random.Range(0, k + 1); var x = _bag[k]; _bag[k] = _bag[r]; _bag[r] = x; }
                if (_bag.Count > 1 && _bag[_bag.Count - 1] == _lastClip) { var x = _bag[0]; _bag[0] = _bag[_bag.Count - 1]; _bag[_bag.Count - 1] = x; }
            }
            var i = _bag[_bag.Count - 1];
            _bag.RemoveAt(_bag.Count - 1);
            _lastClip = i;
            return _clips[i];
        }

        private AudioListener _listener;
        private float _nextListenerLookup;

        // Écouteur du joueur (caméra) : recherché au plus une fois par seconde, la recherche parcourt toute la scène.
        private Vector3 ListenerPosition()
        {
            if ((_listener == null || !_listener.isActiveAndEnabled) && Time.time >= _nextListenerLookup)
            {
                _nextListenerLookup = Time.time + 1f;
                _listener = FindObjectOfType<AudioListener>();
            }
            var l = _listener;
            if (l != null) return l.transform.position;
            var cam = Camera.main;
            return cam != null ? cam.transform.position : Vector3.zero;
        }

        // ---- Fumée ----

        private void Puff(Vector3 at, Vector3 forward)
        {
            // Nuage de poudre : une douzaine de petites bouffées, chassées devant le canon puis vite freinées, qui s'étalent.
            for (var i = 0; i < 12; i++)
            {
                var along = Random.Range(0f, 1.6f);
                var p = new ParticleSystem.EmitParams
                {
                    position = at + forward * along + Random.insideUnitSphere * 0.15f,
                    velocity = forward * Random.Range(0.6f, 2.4f) + Random.insideUnitSphere * 0.25f + Vector3.up * 0.05f,
                    startSize = Random.Range(0.6f, 1.1f),
                    startLifetime = SmokeLife * Random.Range(0.6f, 1.2f),
                    startColor = new Color(0.85f, 0.84f, 0.8f, Random.Range(0.4f, 0.6f)),
                    rotation = Random.Range(0f, 360f),
                    angularVelocity = Random.Range(-12f, 12f),
                };
                _smoke.Emit(p, 1);
            }
        }

        /// <summary>
        /// Système de particules partagé, en coordonnées du monde. Bouffées petites et transparentes qui grossissent (×3),
        /// freinent, tournent lentement, dérivent avec un léger vent et s'effacent. Texture de nuage irrégulière (bruit de
        /// Perlin sur un disque estompé), faite dans le code : pas de cercle net.
        /// </summary>
        private ParticleSystem BuildSmoke()
        {
            // Shader simple et transparent d'abord ; « Particles/Standard Unlit » créé dans le code est opaque par défaut
            // (réglé en mode Fade plus bas).
            var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Alpha Blended")
                         ?? Shader.Find("Mobile/Particles/Alpha Blended") ?? Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.Log("[AMB] Aucun shader de particules : fumée désactivée.");
                return null;
            }
            var go = new GameObject("AmbianceSmoke");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 4000;
            main.gravityModifier = -0.003f;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.15f, 1.8f), new Keyframe(1f, 3f)));
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.25f;
            limit.dampen = 0.2f;
            // Léger vent constant : le nuage dérive d'un côté au lieu de monter tout droit.
            var force = ps.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.World;
            force.x = new ParticleSystem.MinMaxCurve(0.03f);
            force.z = new ParticleSystem.MinMaxCurve(0.015f);
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.92f, 0.92f, 0.9f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.05f), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0f, 1f) });
            color.color = g;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.15f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.1f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sortingFudge = 10f;
            var mat = new Material(shader) { mainTexture = CloudTexture(128) };
            if (shader.name == "Particles/Standard Unlit")
            {
                mat.SetFloat("_Mode", 2f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_ALPHABLEND_ON");
            }
            mat.renderQueue = 3000;
            renderer.material = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            Debug.Log("[AMB] Fumée : shader « " + shader.name + " ».");
            ps.Play();
            return ps;
        }

        /// <summary>Texture de nuage : bruit de Perlin à plusieurs échelles, estompé vers les bords (pas de disque net).</summary>
        private static Texture2D CloudTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var c = (size - 1) * 0.5f;
            var ox = Random.Range(0f, 100f);
            var oy = Random.Range(0f, 100f);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                var n = 0.5f * Mathf.PerlinNoise(ox + u * 4f, oy + v * 4f)
                        + 0.3f * Mathf.PerlinNoise(ox + u * 9f, oy + v * 9f)
                        + 0.2f * Mathf.PerlinNoise(ox + u * 19f, oy + v * 19f);
                var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                // Bord déformé par le bruit, fondu très progressif.
                var edge = Mathf.Clamp01(1f - (d + (n - 0.5f) * 0.6f));
                var a = Mathf.Clamp01(edge * edge * (0.4f + n));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            return tex;
        }
    }
}
