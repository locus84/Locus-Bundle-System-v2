using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using BundleSystem;
using System.Linq;

public class AssetBundleTrackingVisualizer : EditorWindow
{
    [MenuItem("Window/Asset Management/AssetBundle Tracking Visualizer")]
    static void Init() => EditorWindow.GetWindow<AssetBundleTrackingVisualizer>(false, "AssetBundle TrackInfos").Show();
    Dictionary<string, List<KeyValuePair<int, TrackInfo>>> m_ProcessedDict = null;
    Vector2 m_ScrollPosition;
    Dictionary<string, bool> m_Foldout = new Dictionary<string, bool>();
    bool m_LiveUpdate = true;
    bool m_ForceExpend = false;

    void OnGUI()
    {
        if (m_LiveUpdate || m_ProcessedDict == null)
        {
            var snapShot = BundleManager.GetTrackingSnapshot();
            m_ProcessedDict = snapShot.GroupBy(kv => kv.Value.BundleName).OrderByDescending(grp => grp.Count()).ToDictionary(grp => grp.Key, grp => grp.ToList());
        }

        EditorGUILayout.BeginHorizontal();
        m_LiveUpdate = EditorGUILayout.Toggle("Live Update", m_LiveUpdate);
        m_ForceExpend = EditorGUILayout.Toggle("Force Expend", m_ForceExpend);
        EditorGUILayout.EndHorizontal();

        DrawUILine(Color.gray);

        EditorGUILayout.BeginHorizontal();
        EditorGUI.indentLevel++;
        EditorGUILayout.LabelField($"Track ID", GUILayout.Width(80));
        EditorGUILayout.LabelField($"Track Owner", GUILayout.Width(150));
        EditorGUILayout.LabelField($"Loaded Asset", GUILayout.Width(150));
        EditorGUILayout.LabelField($"Status", GUILayout.Width(140));
        EditorGUILayout.LabelField($"Loaded Time", GUILayout.Width(120));
        EditorGUI.indentLevel--;
        EditorGUILayout.EndHorizontal();

        DrawUILine(Color.gray);

        m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition, false, false);

        foreach (var kv in m_ProcessedDict)
        {
            var foldOut = default(bool);

            if (m_ForceExpend)
            {
                EditorGUILayout.Foldout(true, $"{kv.Key} - {kv.Value.Count}");
                foldOut = true;
            }
            else
            {
                foldOut = EditorGUILayout.Foldout(m_Foldout.TryGetValue(kv.Key, out var value) && value, $"BundleName - {kv.Key}, TrackCount - {kv.Value.Count}");
                m_Foldout[kv.Key] = foldOut;
            }

            if (foldOut)
            {
                EditorGUI.indentLevel++;
                foreach (var trackKv in kv.Value)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"{trackKv.Key}", GUILayout.Width(80));
                    EditorGUILayout.ObjectField(trackKv.Value.Owner, typeof(UnityEngine.Object), true, GUILayout.Width(150));
                    EditorGUILayout.ObjectField(trackKv.Value.Asset, typeof(UnityEngine.Object), false, GUILayout.Width(150));
                    EditorGUILayout.LabelField(trackKv.Value.Status.ToString(), GUILayout.Width(140));
                    EditorGUILayout.LabelField(trackKv.Value.LoadTime.ToString("0.0"), GUILayout.Width(120));
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void Update()
    {
        if (m_LiveUpdate) Repaint();
    }

    static void DrawUILine(Color color, int thickness = 2, int padding = 10)
    {
        Rect r = EditorGUILayout.GetControlRect(GUILayout.Height(padding + thickness));
        r.height = thickness;
        r.y += padding / 2;
        r.x -= 2;
        r.width += 6;
        EditorGUI.DrawRect(r, color);
    }
}