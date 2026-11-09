using System.Collections.Generic;
using UnityEngine;

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
        private const float SmokeLife = 9f;        // s

        private static AmbianceLoop _instance;
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

        public static void Shot(Vector3 shooter, Vector3 forward)
        {
            if (_instance != null) _instance.Play(shooter, forward);
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

        private void Play(Vector3 shooter, Vector3 forward)
        {
            var listener = ListenerPosition();
            var distance = Vector3.Distance(listener, shooter);
            var muzzle = shooter + Vector3.up * 1.5f + forward * 1.1f;
            if (_smoke != null) Puff(muzzle, forward);
            if (_clips.Count == 0 || distance < NearSkip || distance > MaxDistance) return;

            var voice = FreeVoice();
            if (voice < 0) return;
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
            var p = new ParticleSystem.EmitParams
            {
                position = at,
                velocity = forward * Random.Range(1.5f, 3f) + Vector3.up * 0.2f,
                startSize = Random.Range(0.8f, 1.4f),
                startLifetime = SmokeLife * Random.Range(0.8f, 1.2f),
                startColor = new Color(0.86f, 0.86f, 0.84f, 0.55f),
                rotation = Random.Range(0f, 360f),
            };
            _smoke.Emit(p, 6);
        }

        /// <summary>
        /// Système de particules partagé, en coordonnées du monde : les bouffées grossissent, ralentissent, s'éclaircissent et
        /// dérivent. Texture ronde floue faite dans le code. Sans shader de particules disponible : pas de fumée.
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
            main.maxParticles = 3000;
            main.gravityModifier = -0.01f;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 4f));
            var vel = ps.limitVelocityOverLifetime;
            vel.enabled = true;
            vel.limit = 0.4f;
            vel.dampen = 0.15f;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.3f), new GradientAlphaKey(0f, 1f) });
            color.color = g;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.3f;
            noise.frequency = 0.2f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(shader) { mainTexture = SoftDot(64) };
            renderer.material = mat;
            ps.Play();
            return ps;
        }

        private static Texture2D SoftDot(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var c = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                var a = Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
            }
            tex.Apply();
            return tex;
        }
    }
}
