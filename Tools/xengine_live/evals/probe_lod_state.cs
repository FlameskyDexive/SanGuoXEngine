// At the mid camera pose, inspects one t_shanshi04 LODGroup: size, distance, share, chosen level,
// and whether its renderers are hidden — the far-distance cull hypothesis.

System.Type manager = null;
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies()) {
    var type = assembly.GetType("XEngine.Editor.GUI.SceneView.EditorSceneManager");
    if (type != null) { manager = type; break; }
}
bool opened = (bool)manager.GetMethod("OpenScene").Invoke(null, new object[] { "Scenes/TaoYuan.scene" });
if (!opened) return "OpenScene returned false.";

var scene = XEngine.Runtime.Resources.Scene.Current;
XEngine.Runtime.Camera? camera = null;
XEngine.Runtime.LODGroup? rock = null;
foreach (var go in scene.AllObjects) {
    foreach (var component in go.GetComponents()) {
        if (component is XEngine.Runtime.Camera c && camera == null) camera = c;
        if (component is XEngine.Runtime.LODGroup g && rock == null && go.Name.Contains("shanshi"))
            rock = g;
    }
}
if (camera == null || rock == null) return "camera or rock LODGroup not found.";

var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(
    System.IO.Path.Combine(XEngine.Editor.Projects.Project.Current.RootPath, "Export/scene/scene.json")));
var frame = document.RootElement.GetProperty("frames").GetProperty("mid");
var position = frame.GetProperty("position");
camera.Transform.Position = new XEngine.Vector.Float3(position[0].GetSingle(), position[1].GetSingle(), position[2].GetSingle());
var rotation = frame.GetProperty("rotation");
camera.Transform.Rotation = new XEngine.Vector.Quaternion(rotation[0].GetSingle(), rotation[1].GetSingle(),
    rotation[2].GetSingle(), rotation[3].GetSingle());
camera.UpdateRenderData();
XEngine.Runtime.Rendering.QualitySettings.LodBias = 0.4f;

var go2 = rock.GameObject;
var distance = XEngine.Vector.Float3.Distance(camera.Transform.Position, go2.Transform.Position);
float share = rock.GetRelativeHeight(camera);
int chosen = rock.SelectLevel(camera);
var sb = new System.Text.StringBuilder();
sb.Append("rock: ").Append(go2.Name).Append(" at ").Append(go2.Transform.Position.X).Append(',').Append(go2.Transform.Position.Y).Append(',').Append(go2.Transform.Position.Z).Append('\n');
sb.Append("camera: ").Append(camera.Transform.Position.X).Append(',').Append(camera.Transform.Position.Y).Append(',').Append(camera.Transform.Position.Z).Append('\n');
sb.Append("distance: ").Append(distance).Append('\n');
sb.Append("lod Size: ").Append(rock.Size).Append(" levels: ").Append(rock.Levels.Count).Append('\n');
for (int i = 0; i < rock.Levels.Count; i++)
    sb.Append("  level ").Append(i).Append(" threshold ").Append(rock.Levels[i].ScreenRelativeTransitionHeight)
      .Append(" renderers ").Append(rock.Levels[i].Renderers.Count).Append('\n');
sb.Append("share: ").Append(share).Append(" chosen level: ").Append(chosen)
  .Append(" forceLevel: ").Append(rock.ForceLevel).Append('\n');
sb.Append("currentLevel: ").Append(rock.CurrentLevel).Append('\n');
// The five largest LODGroups, with their mid-pose selection.
var largest = new System.Collections.Generic.List<(XEngine.Runtime.LODGroup, float, string)>();
foreach (var go3 in scene.AllObjects)
    foreach (var component in go3.GetComponents())
        if (component is XEngine.Runtime.LODGroup g2 && g2.Levels.Count > 0)
            largest.Add((g2, g2.Size, go3.Name));
largest.Sort((a, b) => b.Item2.CompareTo(a.Item2));
for (int i = 0; i < largest.Count && i < 5; i++) {
    var g = largest[i].Item1;
    float shareG = g.GetRelativeHeight(camera);
    int chosenG = g.SelectLevel(camera);
    sb.Append("big ").Append(i).Append(": ").Append(largest[i].Item3)
      .Append(" size ").Append(largest[i].Item2)
      .Append(" levels ").Append(g.Levels.Count);
    for (int l = 0; l < g.Levels.Count; l++)
        sb.Append(" [").Append(l).Append("] ").Append(g.Levels[l].ScreenRelativeTransitionHeight)
          .Append("/").Append(g.Levels[l].Renderers.Count);
    sb.Append(" share ").Append(shareG).Append(" chosen ").Append(chosenG)
      .Append(" current ").Append(g.CurrentLevel).Append('\n');
}
return sb.ToString();
