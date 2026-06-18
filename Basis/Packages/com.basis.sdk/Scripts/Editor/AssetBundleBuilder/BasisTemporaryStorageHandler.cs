using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
public static class TemporaryStorageHandler
{
    public static string SavePrefabToTemporaryStorage(GameObject prefab, BasisAssetBundleObject settings, ref bool wasModified, out string uniqueID)
    {
        EnsureDirectoryExists(settings.TemporaryStorage);
        uniqueID = BasisGenerateUniqueID.GenerateUniqueID();
        string prefabPath = Path.Combine(settings.TemporaryStorage, $"{uniqueID}.prefab");
        prefab = PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        wasModified = true;
        return prefabPath;
    }
    public static string SaveScene(Scene sceneToCopy, BasisAssetBundleObject settings, out string uniqueID)
    {
        uniqueID = BasisGenerateUniqueID.GenerateUniqueID();
        EnsureDirectoryExists(settings.TemporaryStorage);
        string scenePath = Path.Combine(settings.TemporaryStorage, $"{uniqueID}.unity");
        return SaveSceneToTemporaryStorage(sceneToCopy, scenePath, ref uniqueID);
    }

    public static string SaveSceneToTemporaryStorage(Scene scene, string scenePath, ref string uniqueID)
    {
        if (string.IsNullOrEmpty(scenePath))
        {
            uniqueID = null;
            return null;
        }

        EnsureDirectoryExists(Path.GetDirectoryName(scenePath));
        if (EditorSceneManager.SaveScene(scene, scenePath))
        {
            uniqueID = Path.GetFileNameWithoutExtension(scenePath);
            return scenePath;
        }

        uniqueID = null;
        return null;
    }
    public static void EnsureDirectoryExists(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }
    public static void ClearTemporaryStorage(string tempStoragePath)
    {
        if (Directory.Exists(tempStoragePath))
        {
            Directory.Delete(tempStoragePath, true);
        }
    }
}
