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
        private const float MinDistance = 8f;      // pleine force en deçà
        private const float MaxDistance = 900f;    // plus rien au-delà
        private const float NearSkip = 6f;         // tout près, le son du jeu suffit
        private const float Volume = 0.8f;
        private const float FarCutoff = 900f;      // Hz, tir le plus lointain
        private const float NearCutoff = 16000f;   // Hz, tir proche
        private const float SmokeLife = 8f;        // s

        private static AmbianceLoop _instance;
        /// <summary>Tirs du mod joués (!musket on / off) ; désactivés tant que le joueur ne l'a pas demandé.</summary>
        public static bool Enabled;
        // Tirs encore détaillés dans le journal (!musket debug ; les 30 premiers par défaut).
        public static int DebugShots = 30;

        // ---- Son de tir du jeu (mixage « weaponsVol » du jeu, celui de l'option de volume des tirs) ----
        /// <summary>Baisse demandée par le joueur, en dB (0 = son du jeu intact).</summary>
        public static float GameShotCut { get; private set; }
        private AudioMixer _mixer;
        private float _gameLevel = float.NaN;   // niveau réglé par le jeu (option du joueur)
        private float _ourLevel = float.NaN;    // dernier niveau posé par le mod
        private float _nextMixerCheck;

        public static void SetGameShotCut(float db)
        {
            GameShotCut = db;
            if (_instance != null) _instance.ApplyMixer(true);
            Debug.Log("[AMB] Son de tir du jeu : " + (db > 0f ? "-" + db + " dB" : "normal") + ".");
        }

        private void Update()
        {
            if (Time.time >= _nextMixerCheck) ApplyMixer(false);
        }

        /// <summary>
        /// Pose « niveau du jeu - GameShotCut ». Le jeu repose son niveau quand le joueur touche à ses options : un niveau qui
        /// ne vient pas du mod devient le nouveau niveau du jeu, puis la baisse est réappliquée.
        /// </summary>
        private void ApplyMixer(bool force)
        {
            _nextMixerCheck = Time.time + 2f;
            if (_mixer == null)
            {
                foreach (var m in Resources.FindObjectsOfTypeAll<AudioMixer>())
                    if (m != null && m.GetFloat("weaponsVol", out _)) { _mixer = m; break; }
                if (_mixer == null) return;
            }
            if (!_mixer.GetFloat("weaponsVol", out var now)) return;
            if (float.IsNaN(_ourLevel) || Mathf.Abs(now - _ourLevel) > 0.05f) _gameLevel = now;
            var want = GameShotCut > 0f ? Mathf.Max(-80f, _gameLevel - GameShotCut) : _gameLevel;
            if (!force && Mathf.Abs(now - want) < 0.05f) return;
            _mixer.SetFloat("weaponsVol", want);
            _ourLevel = want;
        }
        private readonly List<AudioClip> _clips = new List<AudioClip>();
        private readonly List<AudioSource> _sources = new List<AudioSource>();
        private readonly List<AudioLowPassFilter> _filters = new List<AudioLowPassFilter>();
        private readonly List<float> _busyUntil = new List<float>();
        private ParticleSystem _smoke;
        private int _lastClip = -1;

        public static void Ensure(List<AudioClip> clips)
        {
            if (_instance == null)
            {
                var go = new GameObject("AmbianceLoop");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<AmbianceLoop>();
                _instance.Build();
            }
            _instance._clips.Clear();
            _instance._clips.AddRange(clips);
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
            if (_smoke != null && !local) Puff(muzzle, forward);
            var why = !Enabled ? "mod off" : _clips.Count == 0 ? "aucun son chargé"
                : distance < NearSkip && GameShotCut <= 0f && !local ? "trop proche" : distance > MaxDistance ? "trop loin" : null;
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
            var i = Random.Range(0, _clips.Count);
            if (_clips.Count > 1 && i == _lastClip) i = (i + 1) % _clips.Count;
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
                    startSize = Random.Range(0.35f, 0.7f),
                    startLifetime = SmokeLife * Random.Range(0.6f, 1.2f),
                    startColor = new Color(0.82f, 0.81f, 0.78f, Random.Range(0.18f, 0.32f)),
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
            var shader = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended")
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
            renderer.material = mat;
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
