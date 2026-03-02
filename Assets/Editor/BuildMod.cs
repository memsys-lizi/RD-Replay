#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ThunderKit.Core.Data;
using UnityEditor;
using UnityEngine;
using ThunderKit.Core.Pipelines;
using UnityEditorInternal;
using Debug = UnityEngine.Debug;

public class BuildMod : EditorWindow
{
    private string pluginsPath = "";
    private Pipeline selectedPipeline = null;
    private Vector2 scrollPosition;
    
    // 用于存储偏好设置的键
    private const string MOD_PATH_PREFS_KEY = "BuildMod_Path";
    private const string PIPELINE_PREFS_KEY = "BuildMod_Pipeline";

    [MenuItem("Tools/Build Mod")]
    public static void ShowWindow()
    {
        GetWindow<BuildMod>("Build Mod");
    }

    private void OnEnable()
    {
        // 窗口启用时加载保存的数据
        LoadSavedData();
    }

    private void OnDisable()
    {
        // 窗口禁用时保存数据
        SaveData();
    }

    private void OnDestroy()
    {
        // 窗口销毁时保存数据
        SaveData();
    }

    private void LoadSavedData()
    {
        // 加载保存的mod路径
        if (EditorPrefs.HasKey(MOD_PATH_PREFS_KEY))
        {
            pluginsPath = EditorPrefs.GetString(MOD_PATH_PREFS_KEY);
        }
        
        // 加载保存的Pipeline
        if (EditorPrefs.HasKey(PIPELINE_PREFS_KEY))
        {
            string pipelinePath = EditorPrefs.GetString(PIPELINE_PREFS_KEY);
            if (!string.IsNullOrEmpty(pipelinePath))
            {
                selectedPipeline = AssetDatabase.LoadAssetAtPath<Pipeline>(pipelinePath) as Pipeline;
            }
        }
    }

    private void SaveData()
    {
        // 保存mod路径
        EditorPrefs.SetString(MOD_PATH_PREFS_KEY, pluginsPath);
        
        // 保存Pipeline的Asset路径
        if (selectedPipeline != null)
        {
            string pipelinePath = AssetDatabase.GetAssetPath(selectedPipeline);
            EditorPrefs.SetString(PIPELINE_PREFS_KEY, pipelinePath);
        }
        else
        {
            EditorPrefs.DeleteKey(PIPELINE_PREFS_KEY);
        }
    }

    void OnGUI()
    {
        GUILayout.Label("Mod Build Configuration", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Mod Path Selection
        EditorGUILayout.LabelField("Plugins Path", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        pluginsPath = EditorGUILayout.TextField("Plugins Directory", pluginsPath);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string selectedPath = EditorUtility.OpenFolderPanel("Select BepInEx Plugins Directory", pluginsPath, "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                pluginsPath = selectedPath;
                SaveData(); // 选择后立即保存
            }
        }
        GUILayout.EndHorizontal();

        EditorGUILayout.Space();
        
        // Pipeline Selection
        EditorGUILayout.LabelField("Pipeline", EditorStyles.boldLabel);
        Pipeline previousPipeline = selectedPipeline;
        selectedPipeline = (Pipeline)EditorGUILayout.ObjectField("Select Pipeline", selectedPipeline, typeof(Pipeline), false);
        
        // 如果Pipeline选择发生变化，保存数据
        if (previousPipeline != selectedPipeline)
        {
            SaveData();
        }

        EditorGUILayout.Space();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Select the directory where your mod files are located, choose a pipeline to execute, and select an assembly definition", MessageType.Info);
        EditorGUILayout.Space();

        // Build Button
        GUI.enabled = !string.IsNullOrEmpty(pluginsPath) && selectedPipeline != null;
        if (GUILayout.Button("Build Mod", GUILayout.Height(30)))
        {
            BuildModFunction();
        }
        GUI.enabled = true;
        
        // Play Game Button
        EditorGUILayout.Space();
        if (GUILayout.Button("Play Game", GUILayout.Height(30)))
        {
            PlayGame();
        }

        if (!string.IsNullOrEmpty(pluginsPath))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Selected Path:", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(pluginsPath);
        }
        
        if (selectedPipeline != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Selected Pipeline:", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(selectedPipeline.name);
        }
    }

    async Task BuildModFunction()
    {
        try
        {
            Debug.Log("Building mod at path: " + pluginsPath);
            Debug.Log("Using pipeline: " + (selectedPipeline ? selectedPipeline.name : "None"));
            await selectedPipeline.Execute();
            string outputPath = Path.Combine(new DirectoryInfo(Application.dataPath).Parent.FullName, "ThunderKit");
            string modPath = Path.Combine(pluginsPath, selectedPipeline.manifest.Identity.Name);
            if (!Directory.Exists(modPath))
            {
                Directory.CreateDirectory(modPath);
            }
            string[] assetsBundles = Directory.GetFiles(Path.Combine(outputPath, "AssetBundleStaging"), "*.assets", SearchOption.AllDirectories);
            foreach (var assetsBundle in assetsBundles)
            {
                File.Copy(assetsBundle, Path.Combine(modPath, Path.GetFileName(assetsBundle)),true);
            }
            string dll = Directory.GetFiles(Path.Combine(outputPath, "Libraries"), "*.dll", SearchOption.AllDirectories)[0];
            File.Copy(dll,Path.Combine(modPath, Path.GetFileName(Path.Combine(outputPath, "Libraries",selectedPipeline.manifest.Identity.Name +".dll"))),true);
            Debug.Log("Mod构建成功.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
    
    void PlayGame()
    {
        string gamePath = Path.Combine(new DirectoryInfo(pluginsPath).Parent.Parent.FullName);
        Process.Start(Path.Combine(gamePath, Path.GetFileName(gamePath) + ".exe"));
    }
}
#endif