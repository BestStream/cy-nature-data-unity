using UnityEngine;
using UnityEditor.AssetImporters;

[ScriptedImporter(1, "kml")]
public class KmlScriptedImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        string text = System.IO.File.ReadAllText(ctx.assetPath);
        var textAsset = new TextAsset(text);
        ctx.AddObjectToAsset("text", textAsset);
        ctx.SetMainObject(textAsset);
    }
}