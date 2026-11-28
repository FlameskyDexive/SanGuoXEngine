var asm=System.AppDomain.CurrentDomain.GetAssemblies();
System.Type? gen=null;
foreach(var a in asm){ gen=a.GetType("SanGuoXEngine.Editor.SanguoHeroGenerator"); if(gen!=null) break; }
if(gen==null)return "generator-null";
var model=XEngine.Runtime.AssetDatabase.Get(new System.Guid("32954e89-8bcb-520b-a1ee-a0ad06b568db")) as XEngine.Runtime.Resources.PrefabAsset;
if(model==null)return "model-null";
var tex=XEngine.Runtime.AssetDatabase.Get(new System.Guid("9d42083e-1a78-522a-a084-ea32be7ede8a")) as XEngine.Runtime.Resources.Texture2D;
if(tex==null)return "texture-null";
var mat=new XEngine.Runtime.Resources.Material(XEngine.Runtime.Resources.Shader.LoadDefault(XEngine.Runtime.Resources.DefaultShader.Standard));
mat.Name="Monster_YellowArmy_Body_Yellow";
mat.SetTexture("_MainTex", new XEngine.Runtime.AssetRef<XEngine.Runtime.Resources.Texture2D>(tex));
mat.SetColor("_MainColor", XEngine.Vector.Color.White);
mat.SetFloat("_AlphaClip",1f);
mat.SetFloat("_AlphaCutoff",0.5f);
var writeNative=gen.GetMethod("WriteNative",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
writeNative!.Invoke(null,new object[]{mat,"Actor/Monster/Monster_YellowArmy/Monster_YellowArmy_Body_Yellow.mat",new System.Guid("ff65ac6c-3a12-59e2-8d48-01cdd6088150"),"MaterialImporter"});
var instance=model.Instantiate(); if(instance==null)return "instance-null";
int sk=0;
foreach(var r in instance.GetComponentsInChildren<XEngine.Runtime.SkinnedMeshRenderer>(true)){r.Materials.Clear();r.Materials.Add(new XEngine.Runtime.AssetRef<XEngine.Runtime.Resources.Material>(new System.Guid("ff65ac6c-3a12-59e2-8d48-01cdd6088150")));sk++;}
var writePrefab=gen.GetMethod("WritePrefab",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
if(writePrefab==null){instance.Dispose();return "writePrefab-null";}
writePrefab.Invoke(null,new object[]{instance,"Actor/Monster/Monster_YellowArmy/Monster_YellowArmy_Yellow.prefab",new System.Guid("6f864fb6-05e4-58fd-92ba-a3e291b018d6")});
instance.Dispose();
return "generated skinned="+sk;
