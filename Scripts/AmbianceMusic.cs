using System;
using System.Collections.Generic;
using UnityEngine;

namespace HoldfastAmbiance
{
    /// <summary>
    /// Musiques des musiciens (!realist on) : quand un musicien (joueur ou bot) joue un air du jeu (groupe de mixage
    /// « Musician »), l'air du jeu est coupé et une musique du mod est jouée à sa place, en 3D sur le musicien. Musiques
    /// FR_xx pour tous les camps sauf les Britanniques (EN_xx). Le choix ne dépend que du nom de l'air du jeu, le même
    /// chez tous les joueurs : tous ceux qui ont !realist on entendent la même musique pour la même ligne.
    /// </summary>
    public class AmbianceMusic : MonoBehaviour
    {
        // ---- Réglages ----
        private const float ScanEvery = 0.25f;     // s entre deux recherches des airs du jeu
        private const float MinDistance = 8f;      // pleine force en deçà
        private const float MaxDistance = 350f;    // plus rien au-delà
        private const float Spread = 40f;          // degrés : un peu de largeur de près
        private const float Volume = 1f;

        private static AmbianceMusic _instance;
        /// <summary>Musiques du mod jouées à la place des airs du jeu.</summary>
        public static bool Enabled;
        /// <summary>Vrai si le musicien proche de ce point est britannique (fourni par AmbianceMod).</summary>
        public static Func<Vector3, bool> IsBritishAt;

        private readonly List<AudioClip> _fr = new List<AudioClip>();
        private readonly List<AudioClip> _en = new List<AudioClip>();

        // Air du jeu coupé → notre source qui le remplace.
        private readonly Dictionary<AudioSource, AudioSource> _playing = new Dictionary<AudioSource, AudioSource>();
        private readonly Dictionary<AudioSource, AudioClip> _gameClip = new Dictionary<AudioSource, AudioClip>();
        private float _nextScan;

        public static void Ensure(List<AudioClip> music)
        {
            if (_instance == null)
            {
                for (var old = GameObject.Find("AmbianceMusic"); old != null; old = GameObject.Find("AmbianceMusic"))
                {
                    old.name = "AmbianceMusic (ancien)";
                    Destroy(old);
                }
                var go = new GameObject("AmbianceMusic");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<AmbianceMusic>();
            }
            _instance._fr.Clear();
            _instance._en.Clear();
            foreach (var c in music)
            {
                if (c == null) continue;
                var n = c.name.ToUpperInvariant();
                if (n.StartsWith("FR_")) _instance._fr.Add(c);
                else if (n.StartsWith("EN_")) _instance._en.Add(c);
            }
            // Ordre fixe (par nom) : le même index désigne la même musique chez tous les joueurs.
            _instance._fr.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            _instance._en.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            Debug.Log("[AMB] Musiques : " + _instance._fr.Count + " françaises, " + _instance._en.Count + " anglaises.");
        }

        public static void SetEnabled(bool on)
        {
            Enabled = on;
            if (_instance != null && !on) _instance.StopAll();
            Debug.Log("[AMB] Musiques du mod : " + (on ? "on" : "off") + ".");
        }

        private static bool IsGameMusic(AudioSource src)
        {
            var g = src.outputAudioMixerGroup;
            return g != null && g.name == "Musician" && src.clip != null;
        }

        // Hachage stable (identique sur tous les PC, contrairement à string.GetHashCode).
        private static int StableHash(string s)
        {
            unchecked
            {
                var h = 2166136261u;
                foreach (var ch in s) { h ^= ch; h *= 16777619u; }
                return (int)(h & 0x7fffffff);
            }
        }

        private void Update()
        {
            // Fin ou changement d'air du jeu : on arrête la nôtre et on rend le son à la source.
            if (_playing.Count > 0)
            {
                List<AudioSource> done = null;
                foreach (var kv in _playing)
                {
                    var src = kv.Key;
                    if (src == null || !src.isPlaying || src.clip != _gameClip[src])
                        (done ?? (done = new List<AudioSource>())).Add(src);
                }
                if (done != null) foreach (var src in done) Release(src);
            }
            if (!Enabled || Time.time < _nextScan) return;
            _nextScan = Time.time + ScanEvery;
            foreach (var src in FindObjectsOfType<AudioSource>())
            {
                if (src == null || !src.isPlaying || _playing.ContainsKey(src) || !IsGameMusic(src)) continue;
                if (src.transform.IsChildOf(transform)) continue;
                Replace(src);
            }
        }

        private void Replace(AudioSource src)
        {
            var british = IsBritishAt != null && IsBritishAt(src.transform.position);
            var list = british && _en.Count > 0 ? _en : _fr;
            if (list.Count == 0) return;
            var clip = list[StableHash(src.clip.name) % list.Count];

            var go = new GameObject("Musique " + clip.name);
            go.transform.SetParent(src.transform, false);   // suit le musicien
            var ours = go.AddComponent<AudioSource>();
            ours.clip = clip;
            ours.outputAudioMixerGroup = src.outputAudioMixerGroup;   // réglage « musique » du joueur respecté
            ours.spatialBlend = 1f;
            ours.spread = Spread;
            ours.rolloffMode = AudioRolloffMode.Logarithmic;
            ours.minDistance = MinDistance;
            ours.maxDistance = MaxDistance;
            ours.dopplerLevel = 0f;
            ours.volume = Volume;
            ours.playOnAwake = false;
            // Arrivé en cours d'air : on reprend au même moment (borné à notre musique).
            ours.time = Mathf.Min(src.time, Mathf.Max(0f, clip.length - 0.1f));
            ours.Play();

            src.mute = true;
            _playing[src] = ours;
            _gameClip[src] = src.clip;
            Debug.Log("[AMB] Air du jeu « " + src.clip.name + " » → « " + clip.name + " » (" + (british ? "britannique" : "français") + ").");
        }

        private void Release(AudioSource src)
        {
            if (_playing.TryGetValue(src, out var ours) && ours != null) Destroy(ours.gameObject);
            if (src != null) src.mute = false;
            _playing.Remove(src);
            _gameClip.Remove(src);
        }

        private void StopAll()
        {
            foreach (var src in new List<AudioSource>(_playing.Keys)) Release(src);
        }

        private void OnDestroy() => StopAll();
    }
}
