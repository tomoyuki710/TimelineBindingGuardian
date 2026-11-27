using UnityEditor;
using UnityEngine;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Ping の共通処理。アセットは Project ウィンドウで強調表示されるが、Project ウィンドウが別タブの裏にあると
    /// 何も起きていないように見えるため、先に Project ウィンドウを前面に出す。
    /// </summary>
    internal static class PingUtility
    {
        public static void Ping(Object target)
        {
            if (target == null)
            {
                return;
            }

            bool isAsset = !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(target));
            if (isAsset)
            {
                EditorUtility.FocusProjectWindow();
            }

            EditorGUIUtility.PingObject(target);
        }
    }
}
