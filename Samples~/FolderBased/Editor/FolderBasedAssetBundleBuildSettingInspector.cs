using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Net;
using System.IO;

namespace BundleSystem
{
    [DisallowMultipleComponent]
    [CustomEditor(typeof(FolderBasedAssetBundleBuildSetting))]
    public class FolderBasedAssetBundleBuildSettingInspector : Editor
    {
        SerializedProperty m_SettingsProperty;
        SerializedProperty m_OutputPath;
        SerializedProperty m_EmulateBundle;
        SerializedProperty m_EmulateUseRemoteFolder;
        SerializedProperty m_CleanCache;
        SerializedProperty m_RemoteURL;
        ReorderableList list;

        SerializedProperty m_ForceRebuld;
        SerializedProperty m_UseCacheServer;
        SerializedProperty m_CacheServerHost;
        SerializedProperty m_CacheServerPort;

        SerializedProperty m_UseFtp;
        SerializedProperty m_FtpHost;
        SerializedProperty m_FtpUser;
        SerializedProperty m_FtpPass;

        private void OnEnable()
        {
            m_SettingsProperty = serializedObject.FindProperty("FolderSettings");
            m_OutputPath = serializedObject.FindProperty("OutputFolder");
            m_EmulateBundle = serializedObject.FindProperty("EmulateInEditor");
            m_EmulateUseRemoteFolder = serializedObject.FindProperty("EmulateWithoutRemoteURL");
            m_CleanCache = serializedObject.FindProperty("CleanCacheInEditor");
            m_RemoteURL = serializedObject.FindProperty("RemoteURL");

            m_ForceRebuld = serializedObject.FindProperty("ForceRebuild");

            m_UseFtp = serializedObject.FindProperty("UseFtp");
            m_FtpHost = serializedObject.FindProperty("FtpHost");
            m_FtpUser = serializedObject.FindProperty("FtpUserName");
            m_FtpPass = serializedObject.FindProperty("FtpUserPass");

            var settings = target as FolderBasedAssetBundleBuildSetting;

            list = new ReorderableList(serializedObject, m_SettingsProperty, true, true, true, true)
            {
                drawHeaderCallback = rect =>
                {
                    EditorGUI.LabelField(rect, "Bundle List");
                },

                elementHeightCallback = index =>
                {
                    var element = m_SettingsProperty.GetArrayElementAtIndex(index);
                    return EditorGUI.GetPropertyHeight(element, element.isExpanded);
                },

                drawElementCallback = (rect, index, a, h) =>
                {
                    // get outer element
                    var element = m_SettingsProperty.GetArrayElementAtIndex(index);
                    rect.xMin += 10;
                    EditorGUI.PropertyField(rect, element, new GUIContent(settings.FolderSettings[index].BundleName), element.isExpanded);
                }
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var setting = target as FolderBasedAssetBundleBuildSetting;

            list.DoLayoutList();
            bool allowBuild = true;
            if (!setting.IsValid())
            {
                GUILayout.Label("Duplicate or Empty BundleName detected");
                allowBuild = false;
            }

            GUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(m_OutputPath);
            if (GUILayout.Button("Open", GUILayout.ExpandWidth(false))) EditorUtility.RevealInFinder(setting.OutputPath);
            GUILayout.EndHorizontal();
            EditorGUILayout.PropertyField(m_RemoteURL);
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(m_EmulateBundle);
            EditorGUILayout.PropertyField(m_EmulateUseRemoteFolder);
            EditorGUILayout.PropertyField(m_CleanCache);
            EditorGUILayout.PropertyField(m_ForceRebuld);
            
            EditorGUILayout.PropertyField(m_UseFtp);
            if(m_UseFtp.boolValue)
            {
                EditorGUI.indentLevel ++;
                EditorGUILayout.PropertyField(m_FtpHost);
                EditorGUILayout.PropertyField(m_FtpUser);
                m_FtpPass.stringValue = EditorGUILayout.PasswordField("Ftp Password", m_FtpPass.stringValue);
                EditorGUI.indentLevel --;
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUI.BeginDisabledGroup(Application.isPlaying);

            if(AssetBundleBuildSetting.TryGetActiveSetting(out var prevSetting, false) && prevSetting == setting)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(!setting.UseFtp);
                if (allowBuild && GUILayout.Button("Upload(FTP)"))
                {
                    Upload(setting);
                    GUIUtility.ExitGUI();
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUI.EndDisabledGroup();
            serializedObject.ApplyModifiedProperties();
        }

        static void Upload(FolderBasedAssetBundleBuildSetting setting)
        {
            System.Exception exception = null;
            try
            {
                var buildTargetString = EditorUserBuildSettings.activeBuildTarget.ToString();
                var credential = new NetworkCredential(setting.FtpUserName, setting.FtpUserPass);
                var uploadRootPath = Utility.CombinePath(setting.FtpHost, buildTargetString);
                var dirInfo = new DirectoryInfo(setting.OutputPath);
                var files = dirInfo.GetFiles();
                var progress = 0f;
                var progressStep = 1f / files.Length;
                foreach (var fileInfo in dirInfo.GetFiles())
                {
                    byte[] data = File.ReadAllBytes(fileInfo.FullName);
                    EditorUtility.DisplayProgressBar("Uploading AssetBundles", fileInfo.Name, progress);
                    FtpUpload(Utility.CombinePath(uploadRootPath, fileInfo.Name), data, credential);
                    progress += progressStep;
                }
            }
            catch (System.Exception e)
            {
                exception = e;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (exception == null)
            {
                EditorUtility.DisplayDialog("Upload Success", "Uploaded All AssetBundles", "Confirm");
            }
            else
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Upload Failed", "Got Error while uploading see console for detail.", "Confirm");
            }
        }
        
        static void FtpUpload(string path, byte[] data, NetworkCredential credential)
        {
            FtpWebRequest req = (FtpWebRequest)WebRequest.Create(path);
            req.Method = WebRequestMethods.Ftp.UploadFile;
            req.Credentials = credential;

            req.ContentLength = data.Length;
            using (Stream reqStream = req.GetRequestStream())
            {
                reqStream.Write(data, 0, data.Length);
            }

            using (FtpWebResponse resp = (FtpWebResponse)req.GetResponse())
            {
                if (resp.StatusCode != FtpStatusCode.ClosingControl)
                {
                    throw new System.Exception($"File Upload Failed to {path}, Code : {resp.StatusCode}");
                }
            }
        }
    }

}
