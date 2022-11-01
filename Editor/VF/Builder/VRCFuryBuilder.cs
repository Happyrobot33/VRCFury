using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VF.Feature.Base;
using VF.Inspector;
using VF.Model;
using VF.Model.Feature;
using Object = UnityEngine.Object;

namespace VF.Builder {

public class VRCFuryBuilder {
    public void TestRun(GameObject originalObject) {
        if (originalObject.name.StartsWith("VRCF ")) {
            EditorUtility.DisplayDialog("VRCFury Error", "This object is already the output of a VRCF test build.", "Ok");
            return;
        }
        var cloneName = "VRCF Test Build for " + originalObject.name;
        var exists = originalObject.scene
            .GetRootGameObjects()
            .FirstOrDefault(o => o.name == cloneName);
        if (exists) {
            Object.DestroyImmediate(exists);
        }
        var clone = Object.Instantiate(originalObject);
        if (!clone.activeSelf) {
            clone.SetActive(true);
        }
        if (clone.scene != originalObject.scene) {
            SceneManager.MoveGameObjectToScene(clone, originalObject.scene);
        }
        clone.name = cloneName;
        var result = SafeRun(clone);
        if (result) {
            Selection.SetActiveObjectWithContext(clone, clone);
        } else {
            Object.DestroyImmediate(clone);
        }
    }
    
    public bool SafeRun(GameObject avatarObject) {
        Debug.Log("VRCFury invoked on " + avatarObject.name + " ...");

        var result = true;
        try {
            AssetDatabase.StartAssetEditing();
            Run(avatarObject);
            BakeOGB(avatarObject);
        } catch(Exception e) {
            result = false;
            Debug.LogException(e);
            while (e is TargetInvocationException) {
                e = (e as TargetInvocationException).InnerException;
            }
            EditorUtility.DisplayDialog("VRCFury Error", "VRCFury encountered an error.\n\n" + e.Message, "Ok");
        }

        AssetDatabase.StopAssetEditing();
        AssetDatabase.SaveAssets();
        EditorUtility.ClearProgressBar();
        return result;
    }

    private void BakeOGB(GameObject avatarObject) {
        Debug.Log("Baking OGB components ...");
        List<string> usedNames = new List<string>();
        foreach (var c in avatarObject.GetComponentsInChildren<OGBPenetrator>(true)) {
            OGBPenetratorEditor.Bake(c, usedNames);
        }
        foreach (var c in avatarObject.GetComponentsInChildren<OGBOrifice>(true)) {
            OGBOrificeEditor.Bake(c, usedNames);
        }
        Debug.Log("Done");
    }

    private void Run(GameObject avatarObject) {
        if (avatarObject.GetComponentsInChildren<VRCFury>(true).Length == 0) {
            Debug.Log("VRCFury components not found in avatar. Skipping.");
            return;
        }
        
        var progress = new ProgressBar("VRCFury is building ...");

        var name = avatarObject.name;

        // Unhook everything from our assets before we delete them
        progress.Progress(0, "Cleaning up old VRCF cruft from avatar (in case of old builds)");
        LegacyCleaner.Clean(avatarObject);

        // Nuke all our old generated assets
        progress.Progress(0.1, "Clearing generated assets");
        var avatarPath = avatarObject.scene.path;
        if (string.IsNullOrEmpty(avatarPath)) {
            throw new Exception("Failed to find file path to avatar scene");
        }
        var tmpDir = "Assets/_VRCFury/" + VRCFuryAssetDatabase.MakeFilenameSafe(name);
        if (Directory.Exists(tmpDir)) {
            foreach (var asset in AssetDatabase.FindAssets("", new[] { tmpDir })) {
                var path = AssetDatabase.GUIDToAssetPath(asset);
                AssetDatabase.DeleteAsset(path);
            }
        }
        // Don't reuse subdirs, because if unity reuses an asset path, it randomly explodes and picks up changes from the
        // old asset and messes with the new copy.
        tmpDir = tmpDir + "/" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(tmpDir);

        // Apply configs
        ApplyFuryConfigs(
            tmpDir,
            avatarObject,
            progress.Partial(0.2, 0.8)
        );

        progress.Progress(0.9, "Removing Junk Components");
        foreach (var c in avatarObject.GetComponentsInChildren<VRCFury>(true)) {
            var animator = c.gameObject.GetComponent<Animator>();
            if (animator != null && c.gameObject != avatarObject) Object.DestroyImmediate(animator);
            Object.DestroyImmediate(c);
        }

        progress.Progress(1, "Finishing Up");


        Debug.Log("VRCFury Finished!");
    }

    private static void ApplyFuryConfigs(
        string tmpDir,
        GameObject avatarObject,
        ProgressBar progress
    ) {
        var manager = new AvatarManager(avatarObject, tmpDir);
        var clipBuilder = new ClipBuilder(avatarObject);
        
        var actions = new List<FeatureBuilderAction>();
        var totalActionCount = 0;
        var totalModelCount = 0;
        var collectedModels = new List<FeatureModel>();
        var collectedBuilders = new List<FeatureBuilder>();

        void AddModel(FeatureModel model, GameObject configObject) {
            collectedModels.Add(model);
            
            var builder = FeatureFinder.GetBuilder(model, configObject);
            if (builder == null) return;
            builder.featureBaseObject = configObject;
            builder.tmpDir = tmpDir;
            builder.addOtherFeature = m => AddModel(m, configObject);
            builder.uniqueModelNum = ++totalModelCount;
            builder.allFeaturesInRun = collectedModels;
            builder.allBuildersInRun = collectedBuilders;

            collectedBuilders.Add(builder);
            var builderActions = builder.GetActions();
            actions.AddRange(builderActions);
            totalActionCount += builderActions.Count;
        }

        progress.Progress(0, "Collecting features");
        foreach (var vrcFury in avatarObject.GetComponentsInChildren<VRCFury>(true)) {
            var configObject = vrcFury.gameObject;
            var config = vrcFury.config;
            if (config.features != null) {
                Debug.Log("Importing " + config.features.Count + " features from " + configObject.name);
                foreach (var feature in config.features) {
                    AddModel(feature, configObject);
                }
            }
        }

        AddModel(new FixWriteDefaults(), avatarObject);
        
        while (actions.Count > 0) {
            var action = actions.Min();
            actions.Remove(action);
            var builder = action.GetBuilder();
            var configPath = AnimationUtility.CalculateTransformPath(builder.featureBaseObject.transform,
                avatarObject.transform);

            builder.manager = manager;
            builder.clipBuilder = clipBuilder;
            builder.avatarObject = avatarObject;
            
            var statusMessage = "Applying " + action.GetName() + " on " + builder.avatarObject.name + " " + configPath;
            progress.Progress(1 - (actions.Count / (float)totalActionCount), statusMessage);

            action.Call();
        }
        
        progress.Progress(1, "Finalizing avatar changes");
        manager.Finish();
    }
}

}
