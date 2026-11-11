using UnityEngine;
namespace HoldfastCommander {
/// <summary>Vérification locale à hauteur du torse ; aucune navigation globale ni téléportation.</summary>
public static class FormationNavigation {
 public static bool Clear(Vector3 from,float yaw,float distance, Bot bot=null) {
 var origin=from+Vector3.up*0.9f;
 foreach(var hit in Physics.SphereCastAll(origin,0.22f,Formation.Forward(yaw),distance,~0,QueryTriggerInteraction.Ignore)) {
 if(hit.distance<0.1f || hit.normal.y>0.65f)continue;
 // Les camarades sont traités par l'évitement de capsules au sol, pas
 // par leurs accessoires (mousquets, sacs...) ni par notre propre corps.
 bool character=false;
 if(bot!=null && hit.collider!=null) foreach(var member in bot.Line.Bots) {
 var player=Players.Get(member.PlayerId);
 if(player!=null && player.Body!=null && hit.collider.transform.IsChildOf(player.Body.transform)){character=true;break;}
 }
 if(character)continue;
 return false;
 } return true;
 }
 public static bool Direct(Bot bot,Vector3 from,Vector3 to) {
 var delta=Line.Flat(to-from);var distance=delta.magnitude;
 if(distance<0.01f)return true;
 // Trajet direct vers une place : seuls le décor et les camarades immobiles (en place, ou qui ont renoncé) le barrent.
 // Un camarade en marche n'est pas un obstacle durable : la priorité de passage de Bot.Steer le gère sur place,
 // sans envoyer le bot faire le tour de toute la formation.
 var yaw=Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg;
 if(!Clear(from,yaw,distance,bot))return false;
 var dir=delta/distance;
 foreach(var other in bot.Line.Bots) {
 if(other==bot || !other.Alive || !(other.Settled || other.GaveUp) || !other.TryGetPosition(out var pos))continue;
 // « En place » périmé : après un nouvel ordre, un camarade qui recharge ou se relève reste noté en place à son
 // ancienne place. Il va en partir : ce n'est pas un obstacle durable, on attend son départ au lieu de contourner.
 if(!other.GaveUp && Line.Flat(pos-other.SlotPosition).sqrMagnitude>0.09f)continue;
 var rel=Line.Flat(pos-from);var along=Vector3.Dot(rel,dir);
 if(along<=0f)continue;
 var t=Mathf.Min(along,distance);
 if((rel-dir*t).magnitude<2f*Formation.BodyRadius+0.05f)return false;
 }return true;
 }
 public static bool TrafficClear(Bot bot,Vector3 from,float yaw,float distance) {
 if(!Clear(from,yaw,distance,bot))return false;
 var dir=Formation.Forward(yaw);
 foreach(var other in bot.Line.Bots) {
 if(other==bot || !other.Alive || !other.TryGetPosition(out var pos))continue;
 var rel=Line.Flat(pos-from);var along=Vector3.Dot(rel,dir);
 if(along<=0f)continue; // Autoriser la sortie d'un contact en s'éloignant.
 var t=Mathf.Min(along,distance);
 if((rel-dir*t).magnitude<2f*Formation.BodyRadius+0.05f)return false;
 }return true;
 }
 public static bool Escape(Bot bot,Vector3 from,float heading,out float yaw) {
 for(int i=0;i<5;i++){float offset=i==4?180f:(i/2==0?90f:135f)*(i%2==0?1f:-1f);
 yaw=heading+offset;if(TrafficClear(bot,from,yaw,0.6f))return true;}
 yaw=heading;return false;
 }
 public static bool Detour(Bot bot, Vector3 from,float heading,int attempt,out float yaw) {
 // Ordre constant : pas de tirage aléatoire ni changement de côté à chaque tick.
 for(int i=0;i<6;i++) {float angle=(i/2+1)*30f;float side=i%2==0?1f:-1f;
 yaw=heading+side*angle;if(Clear(from,yaw,1.5f,bot))return true;}
 yaw=heading;return false;
 }
}}
