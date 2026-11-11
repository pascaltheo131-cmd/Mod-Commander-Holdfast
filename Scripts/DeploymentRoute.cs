using System.Collections.Generic; using UnityEngine;
namespace HoldfastCommander {
public static class DeploymentRoute {
 // Repère local : x latéral, z vers l'avant. Contour complet du rectangle
 // occupé, puis approche par le côté du rang cible.
 public static List<Vector3> Build(Vector3 from,Vector3 slot,Vector3 anchor,float yaw,float halfWidth,float depth,bool front,int attempt,Bot bot=null) {
 var f=Formation.Forward(yaw);var r=Formation.Right(yaw);
 var rel=Line.Flat(from-anchor);var x=Vector3.Dot(rel,r);var z=Vector3.Dot(rel,f);
 var sx=Vector3.Dot(Line.Flat(slot-anchor),r);
 var margin=1.2f+Mathf.Min(attempt,2)*0.4f;
 var near=z>=-depth*0.5f?margin:-depth-margin;
 var approach=front?margin:-depth-margin;
 var side=x>=0?1f:-1f;if(attempt%2!=0)side=-side;
 var wing=side*(halfWidth+margin);
 var path=new List<Vector3>();
 if(Mathf.Abs(x)<halfWidth+margin){path.Add(anchor+r*x+f*near);}
 // Passage vers l'autre côté des rangs : si la traversée droit devant soi est libre (rang pas encore en place à cet
 // endroit), traverser là et longer le rang, au lieu de faire le tour de toute la formation par une aile.
 if(near!=approach && bot!=null && attempt<2 && FormationNavigation.Direct(bot,from,anchor+r*x+f*approach))
 {path.Clear();path.Add(anchor+r*x+f*approach);path.Add(anchor+r*sx+f*approach);path.Add(slot);return path;}
 if(near!=approach){path.Add(anchor+r*wing+f*near);path.Add(anchor+r*wing+f*approach);}
 path.Add(anchor+r*sx+f*approach);path.Add(slot);return path;
 }
}}
