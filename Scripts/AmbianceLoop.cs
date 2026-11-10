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
        private const int Voices = 48;
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
        private static readonly string[] GameShotPrefixes = { "au_musket_", "au_rifle_", "au_carbine_", "au_pistol_", "au_blunderbuss_", "au_musketoon_" };
        /// <summary>Sons de tir du jeu coupés (!musket on).</summary>
        public static bool MuteGameShots { get; private set; }
        // Source coupée → heure où la rendre : le jeu joue aussi le rechargement sur la même source (sans changer son clip),
        // on ne la coupe donc que le temps du tir.
        private readonly Dictionary<AudioSource, float> _muted = new Dictionary<AudioSource, float>();
        private const float ShotMuteMax = 1.2f;   // s
        private int _scanFrames;
        private AudioSource[] _gameSources;
        private float _nextSourceRefresh;   // images où chercher les sources de tir du jeu après un tir

        public static void SetMuteGameShots(bool mute)
        {
            MuteGameShots = mute;
            if (_instance != null && !mute) _instance.UnmuteAll();
            if (_instance != null) _instance.SetWeaponsGroup(mute);
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
            foreach (var src in _muted.Keys) if (src != null) src.mute = false;
            _muted.Clear();
        }

        // Le jeu réutilise ses sources : une source coupée qui joue ensuite un autre son (rechargement…) est rétablie.
        private void TickGameShots()
        {
            if (_muted.Count > 0)
            {
                List<AudioSource> back = null;
                foreach (var kv in _muted)
                    if (kv.Key == null || !IsGameShot(kv.Key.clip) || Time.time >= kv.Value) (back ?? (back = new List<AudioSource>())).Add(kv.Key);
                if (back != null)
                    foreach (var src in back) { if (src != null) src.mute = false; _muted.Remove(src); }
            }
            if (!MuteGameShots) { _shooters.Clear(); return; }
            // 1) Sur le tireur lui-même (le jeu joue son tir sur le corps ou l'arme) : rapide, même avec 500 bots.
            for (var i = _shooters.Count - 1; i >= 0; i--)
            {
                var sh = _shooters[i];
                if (sh.Body != null)
                    foreach (var src in sh.Body.GetComponentsInChildren<AudioSource>()) StopIfGameShot(src);
                sh.Frames--;
                if (sh.Body == null || sh.Frames <= 0) _shooters.RemoveAt(i);
                else _shooters[i] = sh;
            }
            if (_scanFrames <= 0) return;
            _scanFrames--;
            // 2) En secours, toutes les sources du jeu (liste refaite au plus toutes les 0,25 s : parcourir toute la scène à
            // chaque image serait trop lourd).
            if (_gameSources == null || Time.time >= _nextSourceRefresh)
            {
                _nextSourceRefresh = Time.time + 0.25f;
                var r0 = Time.realtimeSinceStartupAsDouble;
                var all = FindObjectsOfType<AudioSource>();
                var list = new List<AudioSource>(all.Length);
                foreach (var a in all) if (a != null && !a.transform.IsChildOf(transform)) list.Add(a);
                _gameSources = list.ToArray();
                var rt = Time.realtimeSinceStartupAsDouble - r0;
                _refreshes++;
                if (rt > _maxRefresh) _maxRefresh = rt;
            }
            foreach (var src in _gameSources) StopIfGameShot(src);
        }

        // Tout le groupe « Weapons » du jeu est rendu muet (attrape aussi les tirs lointains, PlayOneShot…) ; les sources
        // de ce groupe qui portent un autre son (rechargement, visée) sont renvoyées vers « Body Movement » pour rester audibles.
        private AudioMixer _mixer;
        private AudioMixerGroup _weapons, _other;
        private float _weaponsVol, _nextReroute;
        private bool _weaponsMuted;
        private readonly List<AudioSource> _rerouted = new List<AudioSource>();

        private void SetWeaponsGroup(bool mute)
        {
            if (_mixer == null)
                foreach (var m in Resources.FindObjectsOfTypeAll<AudioMixer>())
                {
                    if (m == null) continue;
                    var w = m.FindMatchingGroups("Weapons");
                    var o = m.FindMatchingGroups("Body Movement");
                    if (w.Length == 0 || o.Length == 0) continue;
                    _mixer = m; _weapons = w[0]; _other = o[0];
                    break;
                }
            if (_mixer == null) { Debug.Log("[AMB] Groupe « Weapons » introuvable."); return; }
            if (mute && !_weaponsMuted)
            {
                if (!_mixer.GetFloat("weaponsVol", out _weaponsVol)) _weaponsVol = 0f;
                _weaponsMuted = _mixer.SetFloat("weaponsVol", -80f);
                Debug.Log("[AMB] Groupe « Weapons » du jeu : " + (_weaponsMuted ? "muet." : "échec."));
            }
            else if (!mute && _weaponsMuted)
            {
                _mixer.SetFloat("weaponsVol", _weaponsVol);
                _weaponsMuted = false;
                foreach (var src in _rerouted) if (src != null) src.outputAudioMixerGroup = _weapons;
                _rerouted.Clear();
            }
        }

        private void Reroute()
        {
            if (!_weaponsMuted || Time.time < _nextReroute) return;
            _nextReroute = Time.time + 0.1f;
            if (_mixer.GetFloat("weaponsVol", out var v) && v > -79f) _mixer.SetFloat("weaponsVol", -80f);   // réglages du jeu
            for (var i = _rerouted.Count - 1; i >= 0; i--)
            {
                var src = _rerouted[i];
                if (src == null) { _rerouted.RemoveAt(i); continue; }
                if (IsGameShot(src.clip)) { src.outputAudioMixerGroup = _weapons; if (src.time <= 0.5f) src.Stop(); _rerouted.RemoveAt(i); }
            }
            if (_gameSources == null || Time.time >= _nextSourceRefresh + 0.75f)
            {
                _nextSourceRefresh = Time.time + 0.25f;
                var all = FindObjectsOfType<AudioSource>();
                var list = new List<AudioSource>(all.Length);
                foreach (var a in all) if (a != null && !a.transform.IsChildOf(transform)) list.Add(a);
                _gameSources = list.ToArray();
                _refreshes++;
            }
            foreach (var src in _gameSources)
                if (src != null && src.outputAudioMixerGroup == _weapons && src.clip != null && !IsGameShot(src.clip))
                {
                    src.outputAudioMixerGroup = _other;
                    _rerouted.Add(src);
                }
        }

        private void StopIfGameShot(AudioSource src)
        {
            if (src == null || !src.isPlaying || !IsGameShot(src.clip) || src.transform.IsChildOf(transform)) return;
            if (src.time > 0.5f) { _missed++; return; }   // déjà entendu : trouvé trop tard
            // Arrêté plutôt que rendu muet : une source muette occupe toujours une des voix réelles d'Unity.
            src.Stop();
            _stopped++;
        }

        private struct Shooter { public Transform Body; public int Frames; }
        private readonly List<Shooter> _shooters = new List<Shooter>();
        private static int _stopped, _missed;

        private void OnDestroy()
        {
            UnmuteAll();
            SetWeaponsGroup(false);
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
            var t0 = Time.realtimeSinceStartupAsDouble;
            if (_probes.Count > 0 && Time.time >= _probes[0]) { _probes.RemoveAt(0); ProbeGameSounds(); }
            TickGameShots();
            if (MuteGameShots) Reroute();
            AddTime(t0);
            if (PerfDebug) CheckShots();
            TickPerf();
        }

        // ---- Mesure des performances (!realist debug) : rapport dans le journal toutes les 10 s ----
        /// <summary>Rapport de performances actif.</summary>
        public static bool PerfDebug;
        private const float PerfEvery = 10f;
        private static double _frameMod, _sumMod, _maxMod;
        private static int _shots, _played, _grouped, _skipped, _refreshes;
        private static double _maxRefresh;
        private int _frames, _lastFrame = -1;
        private float _perfStart, _worstFrame;

        private static int _heard, _virtual, _notStarted;
        private readonly List<int> _checkVoice = new List<int>();
        private readonly List<float> _checkAt = new List<float>();

        private static int PlayingSources()
        {
            var n = 0;
            foreach (var a in FindObjectsOfType<AudioSource>()) if (a != null && a.isPlaying) n++;
            return n;
        }

        // En debug : 0,1 s après le départ prévu, le tir joue-t-il vraiment, et Unity l'entend-il (voix réelle) ?
        private void CheckShots()
        {
            for (var i = _checkAt.Count - 1; i >= 0; i--)
            {
                if (Time.time < _checkAt[i]) continue;
                var src = _sources[_checkVoice[i]];
                if (!src.isPlaying) _notStarted++;
                else if (src.isVirtual) _virtual++;
                else _heard++;
                if (DebugShots > 0)
                    Debug.Log("[AMB] Contrôle voix " + _checkVoice[i] + " : " + (!src.isPlaying ? "ne joue pas" : src.isVirtual ? "VIRTUELLE (inaudible)" : "audible")
                              + ", volume " + src.volume.ToString("0.00") + ", muette " + src.mute + ", activée " + src.enabled
                              + ", objet actif " + src.gameObject.activeInHierarchy + ", sortie « "
                              + (src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : "directe") + " », écouteur à "
                              + Vector3.Distance(ListenerPosition(), src.transform.position).ToString("0") + " m, AudioListener.volume "
                              + AudioListener.volume.ToString("0.00") + ", pause " + AudioListener.pause + ".");
                _checkAt.RemoveAt(i);
                _checkVoice.RemoveAt(i);
            }
        }

        /// <summary>Ajoute au compte de l'image le temps passé dans le mod depuis t0.</summary>
        public static void AddTime(double t0) => _frameMod += Time.realtimeSinceStartupAsDouble - t0;

        private void TickPerf()
        {
            if (!PerfDebug) { _perfStart = Time.unscaledTime; return; }
            if (Time.frameCount != _lastFrame)
            {
                _lastFrame = Time.frameCount;
                _frames++;
                _worstFrame = Mathf.Max(_worstFrame, Time.unscaledDeltaTime);
            }
            // Le temps du mod de l'image précédente (tirs reçus, musique, cette boucle).
            _sumMod += _frameMod;
            if (_frameMod > _maxMod) _maxMod = _frameMod;
            _frameMod = 0;
            var span = Time.unscaledTime - _perfStart;
            if (span < PerfEvery) return;
            var busy = 0;
            for (var i = 0; i < _busyUntil.Count; i++) if (Time.time < _busyUntil[i]) busy++;
            var cfg = AudioSettings.GetConfiguration();
            Debug.Log("[AMB] Perf " + span.ToString("0") + " s : " + (_frames / span).ToString("0") + " FPS (pire image "
                      + (_worstFrame * 1000f).ToString("0") + " ms) ; mod " + (_sumMod * 1000.0 / Mathf.Max(1, _frames)).ToString("0.00")
                      + " ms/image en moyenne, pire " + (_maxMod * 1000.0).ToString("0.0") + " ms ; recherche des sources "
                      + _refreshes + "×, pire " + (_maxRefresh * 1000.0).ToString("0.0") + " ms ; tirs reçus " + _shots
                      + " (" + (_shots / span).ToString("0") + "/s) : joués " + _played + ", décalés " + _grouped
                      + ", sautés " + _skipped + " ; voix occupées " + busy + "/" + _sources.Count + " ; fumée "
                      + (_smoke != null ? _smoke.particleCount.ToString() : "-") + " bouffées ; musiques "
                      + AmbianceMusic.Playing + " ; tirs du jeu arrêtés " + _stopped + ", passés (trouvés trop tard) " + _missed
                      + " ; tirs vérifiés : audibles " + _heard + ", virtuels (inaudibles) " + _virtual
                      + ", pas lancés " + _notStarted + " ; voix réelles Unity " + cfg.numRealVoices + ", sources jouant dans la scène "
                      + PlayingSources() + ".");
            _heard = _virtual = _notStarted = 0;
            _stopped = _missed = 0;
            _perfStart = Time.unscaledTime;
            _frames = 0; _worstFrame = 0f; _sumMod = 0; _maxMod = 0;
            _shots = _played = _grouped = _skipped = _refreshes = 0; _maxRefresh = 0;
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

        /// <summary>!realist test : un tir du mod joué tout près de l'écouteur, et la même chose en son 2D (sans 3D).</summary>
        public static void Test()
        {
            if (_instance == null) { Debug.Log("[AMB] Test : boucle d'ambiance absente."); return; }
            var was = Enabled;
            Enabled = true;
            var at = _instance.ListenerPosition();
            _instance.Play(at + Vector3.forward * 3f - Vector3.up * 1.5f, Vector3.back, false);
            Enabled = was;
            if (_instance._clips.Count == 0) return;
            var go = new GameObject("AmbianceTest2D");
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.priority = 0;
            src.clip = _instance._clips[0];
            src.PlayDelayed(1.5f);
            Destroy(go, 4f);
            Debug.Log("[AMB] Test : un tir 3D à 3 m maintenant, puis le même en 2D dans 1,5 s (clip " + src.clip.name + ", "
                      + src.clip.length.ToString("0.00") + " s, " + src.clip.channels + " canal, " + src.clip.frequency + " Hz, état "
                      + src.clip.loadState + ").");
        }

        public static void Shot(Vector3 shooter, Vector3 forward, bool local, Transform body = null)
        {
            if (_instance == null)
            {
                if (DebugShots > 0) { DebugShots--; Debug.Log("[AMB] Tir ignoré : boucle d'ambiance absente."); }
                return;
            }
            var t0 = Time.realtimeSinceStartupAsDouble;
            if (body != null && MuteGameShots && _instance._shooters.Count < 400)
                _instance._shooters.Add(new Shooter { Body = body, Frames = 12 });
            _instance.Play(shooter, forward, local);
            AddTime(t0);
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
                src.priority = 16;   // devant les sons ordinaires du jeu (128) quand les voix réelles manquent
                _sources.Add(src);
                _filters.Add(v.AddComponent<AudioLowPassFilter>());
                _busyUntil.Add(0f);
                _voiceDistance.Add(0f);
                _startAt.Add(-10f);
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
            if (MuteGameShots) _scanFrames = 12;   // le son du jeu peut partir quelques images après l'avis de tir
            // Cadence des tirs (moyenne glissante sur ~1 s) : moins de fumée par tir quand la bataille est grosse.
            _rate = _rate * Mathf.Exp(-(Time.time - _rateAt)) + 1f;
            _rateAt = Time.time;
            if (_smoke != null && !local) Puff(muzzle, forward, _rate > 150f ? 2 : _rate > 60f ? 4 : _rate > 25f ? 7 : 12);
            // Juste après le tir, puis pendant le rechargement (~10 s).
            if (local && DebugShots > 0)
            {
                _probes.Clear();
                foreach (var dt in new[] { 0.05f, 1.5f, 3f, 4.5f, 6f, 7.5f, 9f, 10.5f }) _probes.Add(Time.time + dt);
            }
            var why = !Enabled ? "mod off" : _clips.Count == 0 ? "aucun son chargé"
                : distance < NearSkip && !MuteGameShots && !local ? "trop proche" : distance > MaxDistance ? "trop loin" : null;
            var voice = why == null ? FreeVoice(distance) : -1;
            if (why == null && voice < 0) why = "plus de voix libre";
            if (DebugShots > 0)
            {
                DebugShots--;
                Debug.Log("[AMB] Tir à " + distance.ToString("0") + " m (local " + local + ", écouteur " + (_listener != null)
                          + ") : fumée " + (_smoke != null && !local ? "oui" : _smoke == null ? "ABSENTE" : "non (local)")
                          + ", son " + (why ?? "joué (" + _clips.Count + " sons)") + ".");
            }
            _shots++;
            if (why != null && why != "mod off") _skipped++;
            if (why != null) return;
            _played++;
            var src = _sources[voice];
            var clip = PickClip();
            src.transform.position = muzzle;
            src.clip = clip;
            src.volume = Volume;
            src.pitch = Random.Range(0.88f, 1.12f);   // 23 tirs, mais chacun jamais deux fois pareil
            // Au loin, les aigus se perdent : coupure de NearCutoff (proche) à FarCutoff (MaxDistance), en échelle logarithmique.
            var t = Mathf.InverseLerp(Mathf.Log(MinDistance), Mathf.Log(MaxDistance), Mathf.Log(Mathf.Max(distance, MinDistance)));
            _filters[voice].cutoffFrequency = Mathf.Exp(Mathf.Lerp(Mathf.Log(NearCutoff), Mathf.Log(FarCutoff), t));
            var delay = distance / SpeedOfSound;
            // Plus de SimultaneousShots tirs qui partent en même temps : les suivants sont étalés sur 0,5 s au plus (feu roulant),
            // aucun n'est supprimé.
            var starting = 0;
            for (var i = 0; i < _startAt.Count; i++) if (Mathf.Abs(_startAt[i] - (Time.time + delay)) < 0.1f) starting++;
            if (starting >= SimultaneousShots) { delay += Random.Range(0.03f, MaxStagger); _grouped++; }
            _startAt[voice] = Time.time + delay;
            src.PlayDelayed(delay);
            if (PerfDebug && _checkAt.Count < 200) { _checkVoice.Add(voice); _checkAt.Add(Time.time + delay + 0.1f); }
            _busyUntil[voice] = Time.time + delay + clip.length / src.pitch;
            _voiceDistance[voice] = distance;
        }

        // Voix libre ; sinon on reprend celle du tir le plus lointain, si le nouveau tir est plus proche.
        private int FreeVoice(float distance)
        {
            var now = Time.time;
            var far = -1;
            var farDist = distance;
            for (var i = 0; i < _sources.Count; i++)
            {
                if (now >= _busyUntil[i]) return i;
                if (_voiceDistance[i] > farDist) { farDist = _voiceDistance[i]; far = i; }
            }
            if (far >= 0) _sources[far].Stop();
            return far;
        }

        private const int SimultaneousShots = 23;  // tirs qui partent ensemble au plus ; les suivants sont décalés
        private const float MaxStagger = 0.5f;     // s
        private readonly List<float> _startAt = new List<float>();
        private readonly List<float> _voiceDistance = new List<float>();
        private float _rate, _rateAt;

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

        private void Puff(Vector3 at, Vector3 forward, int count)
        {
            // Nuage de poudre : une douzaine de petites bouffées (moins en grosse bataille), chassées devant le canon puis vite
            // freinées, qui s'étalent.
            for (var i = 0; i < count; i++)
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
