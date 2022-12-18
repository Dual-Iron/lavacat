#nullable enable
using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using LavaCat;

public static class CustomAtlasLoader
{
    /// <summary>
    /// Helper for parsing the unity .meta file format
    /// </summary>
    /// <param name="input">a line in .meta format</param>
    /// <returns>string:string pairs for building a dictionary</returns>
    private static KeyValuePair<string, string> MetaEntryToKeyVal(string input)
    {
        if (string.IsNullOrEmpty(input)) return new KeyValuePair<string, string>("", "");
        string[] pieces = input.Split(new char[] { ':' }, 2); // No trim option in framework 3.5
        if (pieces.Length == 0) return new KeyValuePair<string, string>("", "");
        if (pieces.Length == 1) return new KeyValuePair<string, string>(pieces[0].Trim(), "");
        return new KeyValuePair<string, string>(pieces[0].Trim(), pieces[1].Trim());
    }

    /// <summary>
    /// Loads an atlas or single-image into futile.
    /// <list type="bullet">
    ///     <item>If only a png stream is provided, it'll load as a single-image element of same name.</item>
    ///     <item>If slicing data is provided, it'll load as an atlas with elements.</item>
    ///     <item>If metadata is provided, it'll be applied to the texture settings.</item>
    /// </list>
    /// An atlas loaded through this method can overwrite other loaded atlases based on their name, or overwrite single elements from other atlases. If the name colides, but doesn't replace all the elements from the conflicting atlas, the name of the resulting atlas is salted.
    /// </summary>
    /// <param name="atlasName">Name of the atlas.</param>
    /// <param name="textureStream">PNG stream.</param>
    /// <param name="slicerStream">Atlas slicer data stream, or null.</param>
    /// <param name="metaStream">Unity texture metadata stream, or null.</param>
    /// <returns>A reference to the loaded atlas, which is available through Futile.</returns>
    public static FAtlas LoadCustomAtlas(string atlasName, Stream textureStream, Stream? slicerStream = null, Stream? metaStream = null)
    {
        try {
            Texture2D imageData = new(0, 0, TextureFormat.ARGB32, false);
            byte[] bytes = new byte[textureStream.Length];
            textureStream.Read(bytes, 0, (int)textureStream.Length);
            imageData.LoadImage(bytes);

            Dictionary<string, object>? slicerData = null;
            if (slicerStream != null) {
                StreamReader sr = new(slicerStream, Encoding.UTF8);
                slicerData = sr.ReadToEnd().dictionaryFromJson();
            }

            Dictionary<string, string>? metaData = null;
            if (metaStream != null) {
                StreamReader sr = new(metaStream, Encoding.UTF8);
                metaData = new Dictionary<string, string>(); // Boooooo no linq and no splitlines, shame on you c#
                for (string fullLine = sr.ReadLine(); fullLine != null; fullLine = sr.ReadLine()) {
                    (metaData as IDictionary<string, string>).Add(MetaEntryToKeyVal(fullLine));
                }
            }

            return LoadCustomAtlas(atlasName, imageData, slicerData, metaData);
        }
        finally {
            textureStream.Close();
            slicerStream?.Close();
            metaStream?.Close();
        }
    }

    /// <summary>
    /// Loads an atlas or single-image into futile
    /// If only image data is provided, it'll load as single-image element of same name. If slicing data is provided, will load as atlas with elements, if there's metadata, it'll be applied to the texture settings.
    /// An atlas loaded through this method can overwrite other loaded atlases based on their name, or overwrite single elements from other atlases. If the name colides but it doesn't replace all the elements from the conflicting atlas, the name of the resulting atlas is salted.
    /// </summary>
    /// <param name="atlasName">Name of the atlas.</param>
    /// <param name="imageData">Texture  of the atlas.</param>
    /// <param name="slicerData">Parsed atlas slicer data, or null.</param>
    /// <param name="metaData">Parsed unity texture metadata, or null.</param>
    /// <returns>A reference to the loaded atlas, which is available through Futile.</returns>
    private static FAtlas LoadCustomAtlas(string atlasName, Texture2D imageData, Dictionary<string, object>? slicerData, Dictionary<string, string>? metaData)
    {
        // Some defaults, metadata can overwrite
        // common snense
        if (slicerData != null) // sprite atlases are mostly unaliesed
        {
            imageData.anisoLevel = 1;
            imageData.filterMode = 0;
        }
        else // Single-image should clamp
        {
            imageData.wrapMode = TextureWrapMode.Clamp;
        }

        if (metaData != null) {
            metaData.TryGetValue("aniso", out string anisoValue);
            if (!string.IsNullOrEmpty(anisoValue) && int.Parse(anisoValue) > -1) imageData.anisoLevel = int.Parse(anisoValue);
            metaData.TryGetValue("filterMode", out string filterMode);
            if (!string.IsNullOrEmpty(filterMode) && int.Parse(filterMode) > -1) imageData.filterMode = (FilterMode)int.Parse(filterMode);
            metaData.TryGetValue("wrapMode", out string wrapMode);
            if (!string.IsNullOrEmpty(wrapMode) && int.Parse(wrapMode) > -1) imageData.wrapMode = (TextureWrapMode)int.Parse(wrapMode);
        }

        // make singleimage atlas
        FAtlas fatlas = new(atlasName, imageData, FAtlasManager._nextAtlasIndex);

        if (slicerData == null) // was actually singleimage
        {
            // Done
            if (Futile.atlasManager.DoesContainAtlas(atlasName)) {
                Futile.atlasManager.ActuallyUnloadAtlasOrImage(atlasName); // Unload previous version if present
            }
            Futile.atlasManager._allElementsByName.Remove(atlasName);
            FAtlasManager._nextAtlasIndex++; // is this guy even used
            Futile.atlasManager.AddAtlas(fatlas); // Simple
            return fatlas;
        }

        // convert to full atlas
        fatlas._elements.Clear();
        fatlas._elementsByName.Clear();
        fatlas._isSingleImage = false;


        //ctrl c
        //ctrl v

        Dictionary<string, object> dictionary2 = (Dictionary<string, object>)slicerData["frames"];
        float resourceScaleInverse = Futile.resourceScaleInverse;
        int num = 0;
        foreach (KeyValuePair<string, object> keyValuePair in dictionary2) {
            FAtlasElement fatlasElement = new() {
                indexInAtlas = num++
            };
            string text = keyValuePair.Key;
            if (Futile.shouldRemoveAtlasElementFileExtensions) {
                int num2 = text.LastIndexOf(".");
                if (num2 >= 0) {
                    text = text.Substring(0, num2);
                }
            }
            fatlasElement.name = text;
            IDictionary dictionary3 = (IDictionary)keyValuePair.Value;
            fatlasElement.isTrimmed = (bool)dictionary3["trimmed"];
            if ((bool)dictionary3["rotated"]) {
                throw new NotSupportedException("Futile no longer supports TexturePacker's \"rotated\" flag. Please disable it when creating the " + fatlas._dataPath + " atlas.");
            }
            IDictionary dictionary4 = (IDictionary)dictionary3["frame"];
            float x = float.Parse(dictionary4["x"].ToString());
            float y = float.Parse(dictionary4["y"].ToString());
            float w = float.Parse(dictionary4["w"].ToString());
            float h = float.Parse(dictionary4["h"].ToString());
            Rect uvRect = new(x / fatlas._textureSize.x, (fatlas._textureSize.y - y - h) / fatlas._textureSize.y, w / fatlas._textureSize.x, h / fatlas._textureSize.y);
            fatlasElement.uvRect = uvRect;
            fatlasElement.uvTopLeft.Set(uvRect.xMin, uvRect.yMax);
            fatlasElement.uvTopRight.Set(uvRect.xMax, uvRect.yMax);
            fatlasElement.uvBottomRight.Set(uvRect.xMax, uvRect.yMin);
            fatlasElement.uvBottomLeft.Set(uvRect.xMin, uvRect.yMin);
            IDictionary dictionary5 = (IDictionary)dictionary3["sourceSize"];
            fatlasElement.sourcePixelSize.x = float.Parse(dictionary5["w"].ToString());
            fatlasElement.sourcePixelSize.y = float.Parse(dictionary5["h"].ToString());
            fatlasElement.sourceSize.x = fatlasElement.sourcePixelSize.x * resourceScaleInverse;
            fatlasElement.sourceSize.y = fatlasElement.sourcePixelSize.y * resourceScaleInverse;
            IDictionary dictionary6 = (IDictionary)dictionary3["spriteSourceSize"];
            float left = float.Parse(dictionary6["x"].ToString()) * resourceScaleInverse;
            float top = float.Parse(dictionary6["y"].ToString()) * resourceScaleInverse;
            float width = float.Parse(dictionary6["w"].ToString()) * resourceScaleInverse;
            float height = float.Parse(dictionary6["h"].ToString()) * resourceScaleInverse;
            fatlasElement.sourceRect = new Rect(left, top, width, height);
            fatlas._elements.Add(fatlasElement);
            fatlas._elementsByName.Add(fatlasElement.name, fatlasElement);
        }

        // This currently doesn't remove elements from old atlases, just removes elements from the manager.
        bool nameInUse = Futile.atlasManager.DoesContainAtlas(atlasName);
        if (!nameInUse) {
            // remove duplicated elements and add atlas
            foreach (FAtlasElement fae in fatlas._elements) {
                Futile.atlasManager._allElementsByName.Remove(fae.name);
            }
            FAtlasManager._nextAtlasIndex++;
            Futile.atlasManager.AddAtlas(fatlas);
        }
        else {
            FAtlas other = Futile.atlasManager.GetAtlasWithName(atlasName);
            bool isFullReplacement = true;
            foreach (FAtlasElement fae in other.elements) {
                if (!fatlas._elementsByName.ContainsKey(fae.name)) isFullReplacement = false;
            }
            if (isFullReplacement) {
                // Done, we're good, unload the old and load the new
                Futile.atlasManager.ActuallyUnloadAtlasOrImage(atlasName); // Unload previous version if present
                FAtlasManager._nextAtlasIndex++;
                Futile.atlasManager.AddAtlas(fatlas); // Simple
            }
            else {
                // uuuugh
                // partially unload the old
                foreach (FAtlasElement fae in fatlas._elements) {
                    Futile.atlasManager._allElementsByName.Remove(fae.name);
                }
                // load the new with a salted name
                do {
                    atlasName += UnityEngine.Random.Range(0, 9);
                }
                while (Futile.atlasManager.DoesContainAtlas(atlasName));
                fatlas._name = atlasName;
                FAtlasManager._nextAtlasIndex++;
                Futile.atlasManager.AddAtlas(fatlas); // Finally
            }
        }
        return fatlas;
    }
}
