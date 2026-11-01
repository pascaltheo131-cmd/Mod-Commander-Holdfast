using System.Collections.Generic;
namespace HoldfastCommander {
/// <summary>Authentification de connexion, indépendante des fiches de manche.
/// Aucune persistance disque ; révocation à la déconnexion réseau.</summary>
public static class AdminSessions {
 private static readonly Dictionary<int,string> Identities=new Dictionary<int,string>();
 private static readonly HashSet<string> Authenticated=new HashSet<string>();
 public static bool Connect(int id,string backend,bool autoAdmin) {
 if(Identities.TryGetValue(id,out var old) && old!=backend)Disconnect(id);
 if(string.IsNullOrEmpty(backend)) { Disconnect(id);return false; }
 // Une même connexion peut recevoir un nouvel id à la rotation.
 var obsolete=new List<int>();foreach(var pair in Identities)if(pair.Key!=id && pair.Value==backend)obsolete.Add(pair.Key);
 foreach(var prior in obsolete)Identities.Remove(prior);
 Identities[id]=backend;if(autoAdmin)Authenticated.Add(backend);return IsAdmin(id);
 }
 public static bool IsAdmin(int id) {return Identities.TryGetValue(id,out var key) && Authenticated.Contains(key);}
 public static void Login(int id) {if(Identities.TryGetValue(id,out var key))Authenticated.Add(key);}
 public static void Disconnect(int id) {
 if(!Identities.TryGetValue(id,out var key))return;
 Identities.Remove(id);Authenticated.Remove(key);
 }
}}
