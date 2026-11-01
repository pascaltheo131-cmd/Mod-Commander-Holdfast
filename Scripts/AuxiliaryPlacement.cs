using UnityEngine;
namespace HoldfastCommander {
public static class AuxiliaryPlacement {
 // Une rangée vide sépare les soldats des auxiliaires. Deux auxiliaires
 // côte à côte ; un auxiliaire seul reste centré.
 public static Vector2 Slot(int soldiers,int ranks,bool column,int width,bool flag,bool both) {
 width=Mathf.Max(1,width);
 var row=column?(soldiers+width-1)/width+1:Mathf.Max(1,ranks)+1;
 return new Vector2(both?(flag?-0.5f:0.5f):0f,row);
 }
}}
