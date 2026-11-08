namespace SurvivalcraftAgentLab;
public static class CommandPolicy {
 public static bool Valid(string? action,float x,float y,float z,float seconds,int slot,int slotsCount) {
  if(!float.IsFinite(x)||!float.IsFinite(y)||!float.IsFinite(z)||!float.IsFinite(seconds)||seconds<0||seconds>.5f)return false;
  if(action=="stop")return true;
  if(action=="move")return y==0 && Math.Abs(x)<=1 && Math.Abs(z)<=1;
  if(action=="select")return slot>=0 && slot<slotsCount;
  if(action is "dig" or "place") {float norm=x*x+y*y+z*z;return float.IsFinite(norm)&&norm>=.0001f;}
  return false;
 }
}
