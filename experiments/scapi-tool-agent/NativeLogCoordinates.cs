using Engine;
// Engine.Vector3 exposes recursive computed XY/YX properties to the default JSON serializer.
// Preserve all three coordinates explicitly; never mask cycles or drop evidence.
public static class NativeLogCoordinates {
 public static float[] Position(Vector3 value){
  if(!float.IsFinite(value.X)||!float.IsFinite(value.Y)||!float.IsFinite(value.Z))throw new ArgumentException("Finite native log position required");
  return [value.X,value.Y,value.Z];
 }
}
