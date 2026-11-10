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
        private const float MinDistance = 10f;     // pleine force en deçà
        private const float MaxDistance = 350f;    // silence au-delà (baisse progressive jusque-là)
        private const float Spread = 40f;          // degrés : un peu de largeur de près
        private const float Volume = 0.6f;         // ≈ -4,5 dB
        private const float FadeIn = 1.5f;         // s
        private const float FadeOut = 2.5f;        // s
        private const float SilentGrace = 5f;      // s sans air du jeu (pause, musicien hors de portée du jeu) avant d'arrêter
        private const double PickPeriod = 90;      // s : le tirage change toutes les 90 s de temps de serveur

        private static AmbianceMusic _instance;
        /// <summary>Musiques du mod jouées à la place des airs du jeu.</summary>
        public static bool Enabled;
        /// <summary>Joueur ou bot le plus proche d'un point, -1 si aucun (fourni par AmbianceMod).</summary>
        public static Func<Vector3, int> MusicianAt;
        /// <summary>Corps d'un joueur ou bot (fourni par AmbianceMod).</summary>
        public static Func<int, Transform> BodyOf;
        /// <summary>Vrai si ce joueur ou bot est britannique (fourni par AmbianceMod).</summary>
        public static Func<int, bool> IsBritish;
        /// <summary>Temps du serveur, le même chez tous les joueurs (OnUpdateSyncedTime).</summary>
        public static double SyncedTime;

        private readonly List<AudioClip> _fr = new List<AudioClip>();
        private readonly List<AudioClip> _en = new List<AudioClip>();

        // Un musicien qui joue : source du jeu (coupée) et la nôtre. Notre musique ne suit pas chaque arrêt de l'air du jeu
        // (pauses entre deux airs, source coupée par le jeu au loin) : elle continue jusqu'au bout, puis enchaîne.
        private class Slot
        {
            public AudioSource Game, Ours;
            public int Musician;
            public float SilentSince = -1f;
            public float FadeFrom = -1f;   // début du fondu de sortie
        }
        private readonly List<Slot> _slots = new List<Slot>();
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
            if (_instance != null && !on) _instance.StopAll(true);
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

        // Tirage commun à tous les joueurs : musicien + tranche de temps du serveur, jamais deux fois de suite la même.
        private static int Pick(int musician, string gameClip, int count, double time)
        {
            if (count <= 1) return 0;
            var key = musician >= 0 ? "m" + musician : gameClip;
            var bucket = (long)Math.Floor(time / PickPeriod);
            var prev = StableHash(key + "#" + (bucket - 1)) % count;
            var pick = StableHash(key + "#" + bucket) % count;
            return pick == prev ? (pick + 1) % count : pick;
        }

        private void Update()
        {
            var now = Time.time;
            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                var s = _slots[i];
                if (s.Ours == null) { Drop(i); continue; }
                var gameOn = s.Game != null && s.Game.isPlaying;
                if (gameOn && Enabled) { s.SilentSince = -1f; s.FadeFrom = -1f; s.Game.mute = true; }
                else if (!Enabled && s.FadeFrom < 0f) s.FadeFrom = now;
                else if (s.SilentSince < 0f) s.SilentSince = now;
                // Musicien arrêté pour de bon : fondu de sortie.
                if (s.FadeFrom < 0f && s.SilentSince >= 0f && now - s.SilentSince > SilentGrace) s.FadeFrom = now;
                if (s.FadeFrom >= 0f)
                {
                    var k = 1f - (now - s.FadeFrom) / FadeOut;
                    if (k <= 0f) { Drop(i); continue; }
                    s.Ours.volume = Volume * k;
                    continue;
                }
                if (s.Ours.volume < Volume) s.Ours.volume = Mathf.Min(Volume, s.Ours.volume + Volume * Time.deltaTime / FadeIn);
                // Notre musique finie alors que le musicien joue encore : la suivante.
                if (!s.Ours.isPlaying) Start(s, 0f);
            }
            if (!Enabled || now < _nextScan) return;
            _nextScan = now + ScanEvery;
            foreach (var src in FindObjectsOfType<AudioSource>())
            {
                if (src == null || !src.isPlaying || !IsGameMusic(src) || src.transform.IsChildOf(transform)) continue;
                var known = false;
                foreach (var s in _slots) if (s.Game == src || s.Ours == src) { known = true; break; }
                if (known) continue;
                var musician = MusicianAt != null ? MusicianAt(src.transform.position) : -1;
                // Même musicien déjà suivi (le jeu a changé de source) : on garde sa musique.
                Slot same = null;
                if (musician >= 0) foreach (var s in _slots) if (s.Musician == musician) { same = s; break; }
                if (same != null)
                {
                    if (same.Game != null && same.Game != src) same.Game.mute = false;
                    same.Game = src;
                    src.mute = true;
                    same.SilentSince = -1f;
                    same.FadeFrom = -1f;
                    continue;
                }
                var slot = new Slot { Game = src, Musician = musician };
                var go = new GameObject("Musique");
                // Accrochée au corps du musicien : elle le suit quand la ligne marche (la source du jeu peut être ailleurs).
                var body = musician >= 0 && BodyOf != null ? BodyOf(musician) : null;
                var anchor = body != null ? body : src.transform;
                go.transform.SetParent(anchor, false);
                go.transform.localPosition = body != null ? new Vector3(0f, 1.5f, 0f) : Vector3.zero;
                var lis = FindObjectOfType<AudioListener>();
                Debug.Log("[AMB] Air du jeu sur « " + src.gameObject.name + " » (parent « "
                          + (src.transform.parent != null ? src.transform.parent.name : "-") + " », 3D " + src.spatialBlend.ToString("0.0")
                          + ", à " + (lis != null ? Vector3.Distance(lis.transform.position, src.transform.position).ToString("0") : "?")
                          + " m) ; musicien #" + musician + ", accroché à " + (body != null ? "son corps" : "la source du jeu") + ".");
                slot.Ours = go.AddComponent<AudioSource>();
                Setup(slot.Ours, src);
                src.mute = true;
                _slots.Add(slot);
                Start(slot, src.time);
            }
        }

        private static void Setup(AudioSource ours, AudioSource game)
        {
            ours.outputAudioMixerGroup = game.outputAudioMixerGroup;   // réglage « musique » du joueur respecté
            ours.priority = 0;                // jamais coupée par le jeu quand beaucoup de sons jouent
            ours.spatialBlend = 1f;
            ours.spread = Spread;
            ours.dopplerLevel = 0f;
            ours.playOnAwake = false;
            ours.loop = false;
            ours.minDistance = MinDistance;
            ours.maxDistance = MaxDistance;
            // Baisse progressive jusqu'au silence à MaxDistance (la courbe logarithmique de Unity ne s'éteint jamais et
            // la source du jeu, coupée au loin, arrêtait tout net).
            ours.rolloffMode = AudioRolloffMode.Custom;
            var curve = new AnimationCurve();
            var r = MinDistance / MaxDistance;
            curve.AddKey(new Keyframe(0f, 1f));
            curve.AddKey(new Keyframe(r, 1f));
            for (var i = 1; i <= 8; i++)
            {
                var x = r + (1f - r) * i / 8f;
                var d = x * MaxDistance;
                // Loi en 1/d adoucie, ramenée à 0 au bout.
                var y = (MinDistance / d - MinDistance / MaxDistance) / (1f - MinDistance / MaxDistance);
                curve.AddKey(new Keyframe(x, Mathf.Clamp01(Mathf.Max(y, (1f - x) * 0.35f))));
            }
            curve.AddKey(new Keyframe(1f, 0f));
            ours.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve);
        }

        private void Start(Slot s, float at)
        {
            var british = IsBritish != null && s.Musician >= 0 && IsBritish(s.Musician);
            var list = british && _en.Count > 0 ? _en : _fr;
            if (list.Count == 0) { s.FadeFrom = Time.time; return; }
            var gameClip = s.Game != null && s.Game.clip != null ? s.Game.clip.name : "";
            var clip = list[Pick(s.Musician, gameClip, list.Count, SyncedTime)];
            s.Ours.clip = clip;
            s.Ours.time = Mathf.Min(at, Mathf.Max(0f, clip.length - 0.1f));
            s.Ours.volume = 0f;   // fondu d'entrée
            s.Ours.Play();
            Debug.Log("[AMB] Musicien #" + s.Musician + " (" + gameClip + ") → « " + clip.name + " » ("
                      + (british ? "britannique" : "français") + ").");
        }

        private void Drop(int i)
        {
            var s = _slots[i];
            if (s.Ours != null) Destroy(s.Ours.gameObject);
            if (s.Game != null) s.Game.mute = false;
            _slots.RemoveAt(i);
        }

        private void StopAll(bool fade)
        {
            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                if (fade && _slots[i].Ours != null) { if (_slots[i].FadeFrom < 0f) _slots[i].FadeFrom = Time.time; if (_slots[i].Game != null) _slots[i].Game.mute = false; }
                else Drop(i);
            }
        }

        private void OnDestroy() => StopAll(false);
    }
}
