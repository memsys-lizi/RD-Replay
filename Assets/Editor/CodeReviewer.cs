#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;
using System;
using System.Threading.Tasks;
using CodeSearcher;
using UnityEngine.Networking.PlayerConnection;
[InitializeOnLoad]
public class CodeReviewer
{
    static CodeReviewer()
    {
        CompilationPipeline.compilationStarted += OnAssemblyCompilationFinished;
    }
    
    private static void OnAssemblyCompilationFinished(object assemblyPath)
    {
        Debug.Log($"编译开始于: {DateTime.Now}");
        try
        {
            AttributeTypeSearcher<SerializableAttribute> searcher =
                new AttributeTypeSearcher<SerializableAttribute>(Application.dataPath + "\\Scripts");
            var result = searcher.Search(((current, total, name) =>
            {
                Debug.Log($"正在处理 {name}");
            }));
            Debug.Log($"找到 {result.Types.Count} 个标记为 Serializable 的类");
            foreach (var type in result.Types)
            {
                foreach (var usage in type.Usages)
                {
                    if (usage.Member?.Type.Contains("List<") ?? false)
                    {
                        CodeModifier.InsertCodeAbove(usage, "#error 无法在List中使用[Serializable]标记的类");
                        Debug.Log($"已在 {usage.FilePath} 的 {usage.FullContext} 中插入错误信息");
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }
}
#endif