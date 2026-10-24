#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Planet Generator 专用 Inspector：只管理 Mesh 的 Rebuild / Bake。
/// 输入：PlanetGenerator 序列化字段、当前 MeshFilter / MeshRenderer 状态。
/// 输出：编辑器表单、状态检查、Rebuild 和保存 .asset 按钮。
/// 不创建 Material、不执行 Refresh、不覆盖 MeshRenderer 的材质。
/// </summary>
[CustomEditor(typeof(PlanetGenerator))]
public sealed class PlanetGeneratorEditor : Editor
{
    // Unity 菜单输入：Hierarchy 右键 -> 3D Object -> Planet Generator。
    // 输出：自动创建带 MeshFilter / MeshRenderer / PlanetGenerator 的 GameObject。
    [MenuItem("GameObject/3D Object/Planet Generator", false, 20)]
    private static void CreatePlanet(MenuCommand menu)
    {
        var go = new GameObject("Planet Generator");
        GameObjectUtility.SetParentAndAlign(go, menu.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create Planet Generator");
        go.AddComponent<PlanetGenerator>();
        // AddComponent 后 OnEnable 会按需创建初始 Mesh；这里绝不创建材质。
        Selection.activeGameObject = go;
    }

    public override void OnInspectorGUI()
    {
        // 不直接使用 DrawDefaultInspector：显式指定组员看到的英文/中文标签。
        // SerializedProperty 仍然支持 Unity 的 Undo / Prefab Override / 多对象序列化。
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("subdivisions"),
            new GUIContent("Subdivisions / 球体细分", serializedObject.FindProperty("subdivisions").tooltip));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("seed"),
            new GUIContent("Seed / 随机种子", serializedObject.FindProperty("seed").tooltip));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("heightSettings"),
            new GUIContent("Terrain Settings / 地形设置", serializedObject.FindProperty("heightSettings").tooltip), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("bakedMesh"),
            new GUIContent("Baked Mesh / 烘焙网格", serializedObject.FindProperty("bakedMesh").tooltip));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("boundsRadius"),
            new GUIContent("Bounds Radius / 包围盒半径", serializedObject.FindProperty("boundsRadius").tooltip));
        serializedObject.ApplyModifiedProperties();

        var generator = (PlanetGenerator)target;
        var renderer = generator.GetComponent<MeshRenderer>();
        Mesh mesh = generator.CurrentMesh;
        Material material = renderer != null ? renderer.sharedMaterial : null;

        EditorGUILayout.Space(8);
        string meshInfo = mesh == null ? "Mesh: Not generated / 尚未生成" :
            $"Mesh: {mesh.vertexCount} vertices / 顶点";
        bool hasHeight = mesh != null && mesh.HasVertexAttribute(VertexAttribute.TexCoord2);
        bool hasExpectedShader = material != null && material.shader != null &&
            material.HasProperty("_BaseStrength") &&
            material.HasProperty("_MountainStrength") &&
            material.HasProperty("_CraterStrength") &&
            material.HasProperty("_NormalTiling");
        string materialInfo = material == null ? "Material: Not assigned / 未设置" :
            $"Material: {material.name}\nShader: {(material.shader != null ? material.shader.name : "Missing / 丢失")}";
        EditorGUILayout.HelpBox(meshInfo + "\n" + materialInfo,
            (hasHeight && hasExpectedShader) ? MessageType.Info : MessageType.Warning);

        if (!hasHeight)
            EditorGUILayout.HelpBox(
                "Mesh is missing precomputed UV2 terrain data. Click Rebuild. / " +
                "当前 Mesh 缺少 UV2 地形数据，请点击 Rebuild。", MessageType.Warning);
        if (material == null)
            EditorGUILayout.HelpBox(
                "Create a Material in Assets, select the Planet Shader, then assign it to " +
                "MeshRenderer > Materials > Element 0. / " +
                "在 Assets 中创建材质、选择 Planet Shader，再拖入 MeshRenderer 的 Element 0。", MessageType.Warning);
        else if (material.shader == null || !material.shader.isSupported)
            EditorGUILayout.HelpBox(
                "Shader is missing, unsupported, or failed to compile. Check the Shader Inspector and Console. / " +
                "Shader 丢失、不受支持或编译失败，请检查 Shader Inspector 和 Console。", MessageType.Error);
        else if (!hasExpectedShader)
            EditorGUILayout.HelpBox(
                "This Material does not expose the Planet terrain properties. / " +
                "当前材质没有 Planet 地形属性，请检查所选 Shader。", MessageType.Warning);

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "Seed / Noise / Crater Distribution / Subdivisions -> Rebuild. / " +
            "修改随机种子、噪声、陨石坑分布、细分级数后点 Rebuild。\n" +
            "Colors / Normal / Height Strength / Graphic Shading -> Edit Material directly. / " +
            "颜色、法线、地形强度、三渲二参数：直接调材质，实时生效。\n" +
            "This component never creates or resets Materials. / 本组件不会创建或重置材质。",
            MessageType.Info);

        if (GUILayout.Button("1. Rebuild Mesh / 重新生成网格", GUILayout.Height(28)))
        {
            // 输入：当前 Inspector 里的生成参数。
            // 输出：新的临时 Mesh（改变 UV2~UV5），不会改 Material。
            Undo.RecordObject(generator, "Rebuild Planet Generator Mesh");
            var filter = generator.GetComponent<MeshFilter>();
            Undo.RecordObject(filter, "Assign Planet Generator Mesh");
            generator.Rebuild();
            EditorUtility.SetDirty(generator);
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("2. Bake Mesh Asset / 烘焙网格资产", GUILayout.Height(28)))
        {
            // 输入：CurrentMesh（CPU 预计算过的球体 + 高度/梯度）。
            // 输出：Project 中的新 Mesh .asset，并由 Generator 引用它。
            if (generator.CurrentMesh == null)
                generator.Rebuild();
            string path = EditorUtility.SaveFilePanelInProject(
                "Bake Planet Generator Mesh / 保存星球网格",
                "PlanetGenerator_Baked", "asset",
                "Save the precomputed Mesh data (UV2-UV5). / 保存预计算高度和梯度数据。",
                "Assets/PlanetGenerator/BakedMesh");
            if (!string.IsNullOrEmpty(path))
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                // 复制一份，不能直接把生成器持有的临时 Mesh 当成 Project Asset。
                Mesh copy = Object.Instantiate(generator.CurrentMesh);
                copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(copy, path);
                AssetDatabase.SaveAssets();

                Undo.RecordObject(generator, "Bake Planet Generator Mesh");
                Undo.RecordObject(generator.GetComponent<MeshFilter>(), "Assign Baked Mesh");
                generator.SetBakedMesh(copy);
                EditorUtility.SetDirty(generator);
                EditorGUIUtility.PingObject(copy);
                Debug.Log("[Planet Generator] Mesh baked / 网格已烘焙：" + path +
                          "。Material unchanged / 材质未修改。", generator);
                SceneView.RepaintAll();
            }
        }
    }
}
#endif
