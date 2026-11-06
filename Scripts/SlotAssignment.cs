using System; using System.Collections.Generic; using UnityEngine;
namespace HoldfastCommander {
/// <summary>Affectation exclusive minimisant la distance totale (algorithme hongrois).
/// Appelé à un changement de formation/effectif, jamais à chaque pas.</summary>
public static class SlotAssignment {
 public static int[] Solve(IList<Vector3> bots, IList<Vector3> slots, bool squared = false) {
 int n=bots.Count;if(n!=slots.Count)throw new ArgumentException("Nombre de places incorrect");
 var u=new double[n+1];var v=new double[n+1];var owner=new int[n+1];var way=new int[n+1];
 for(int i=1;i<=n;i++) {owner[0]=i;int j0=0;var min=new double[n+1];var used=new bool[n+1];for(int j=1;j<=n;j++)min[j]=double.MaxValue;
 do {used[j0]=true;int i0=owner[j0],j1=0;double delta=double.MaxValue;
 for(int j=1;j<=n;j++)if(!used[j]) {var d=bots[i0-1]-slots[j-1];double d2=d.x*d.x+d.z*d.z;double cost=(squared?d2:Math.Sqrt(d2))-u[i0]-v[j];if(cost<min[j]){min[j]=cost;way[j]=j0;}if(min[j]<delta){delta=min[j];j1=j;}}
 for(int j=0;j<=n;j++)if(used[j]){u[owner[j]]+=delta;v[j]-=delta;}else min[j]-=delta;j0=j1;
 }while(owner[j0]!=0);
 do {int j1=way[j0];owner[j0]=owner[j1];j0=j1;}while(j0!=0);
 }
 var result=new int[n];for(int j=1;j<=n;j++)result[owner[j]-1]=j-1;return result;
 }
}}
