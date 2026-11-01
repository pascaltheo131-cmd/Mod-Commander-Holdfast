using UnityEngine; namespace HoldfastCommander { public static class SlotExchange {
public static bool Worth(float a,float b,float nextA,float nextB) {return a>=6f && a+b-nextA-nextB>2f && Mathf.Max(nextA,nextB)<Mathf.Max(a,b)-1f;}
}}
