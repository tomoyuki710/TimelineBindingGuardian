using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_6000_4_OR_NEWER
using ItemId = UnityEngine.EntityId;
#else
using ItemId = System.Int32;
#endif

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Hierarchy に、未消去の通知がある Director の GameObject と、その祖先へ件数付きの小アイコンを出す。
    /// - GUI 描画中は作成済みのキャッシュを引くだけ（検査・オブジェクト解決はしない）。
    /// - 祖先はイベントの集合で数え、同じ通知を重複して数えない（展開していなくても親で分かる）。
    /// - アイコンをクリックすると、その対象の通知だけを表示した状態でウィンドウを開く。
    /// - Scene の見出し行にも、その Scene 内の通知をまとめて出す（Hierarchy は見出し行でも Scene の handle が表す ID を渡して呼ぶ）。
    /// - 行の ID は Unity 6000.4 以降は EntityId、それより前は Instance ID（HierarchyItemId で扱う）。
    /// </summary>
    [InitializeOnLoad]
    public static class HierarchyIndicator
    {
        private const float IconSize = 16f;
        // Scene の見出し行の右端にある Unity 標準の「⋮」メニューボタンを避ける幅
        private const float SceneMenuButtonWidth = 16f;

        // GameObject の行の ID → 関係する未読の通知（eventId）
        private static readonly Dictionary<ItemId, HashSet<string>> _cache = new Dictionary<ItemId, HashSet<string>>();
        // Scene のキー（HierarchyItemId.SceneKey）→ その Scene 内の Director に関係する未読の通知（eventId）。GameObject の ID と混ざらないよう別に持つ
        private static readonly Dictionary<ulong, HashSet<string>> _sceneCache = new Dictionary<ulong, HashSet<string>>();
        private static bool _rebuildScheduled;
        private static GUIContent _icon;

        static HierarchyIndicator()
        {
#if UNITY_6000_4_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyItemGUI;
#else
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
#endif
            GuardianHistory.Changed += ScheduleRebuild;
            GuardianSettings.Changed += ScheduleRebuild;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            ScheduleRebuild();
        }

        public static void ScheduleRebuild()
        {
            if (_rebuildScheduled)
            {
                return;
            }

            _rebuildScheduled = true;
            EditorApplication.delayCall += Rebuild;
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            ScheduleRebuild();
        }

        private static void OnSceneClosed(Scene scene)
        {
            ScheduleRebuild();
        }

        private static void Rebuild()
        {
            _rebuildScheduled = false;
            _cache.Clear();
            _sceneCache.Clear();
            if (!GuardianSettings.Instance.HierarchyIconsEnabled)
            {
                EditorApplication.RepaintHierarchyWindow();
                return;
            }

            foreach ((NotificationRecord _, ChangeEvent changeEvent) in GuardianHistory.Shared.GetNotifications(false))
            {
                foreach (AffectedContext context in changeEvent.AffectedContexts)
                {
                    AddToHierarchy(ResolveDirectorObject(context.ContextBindingKey), changeEvent.EventId);
                }
            }

            EditorApplication.RepaintHierarchyWindow();
        }

        /// <summary>
        /// 文脈キー（Director の GlobalObjectId | Timeline | Track）から、読み込み済み Scene の Director の GameObject を引く。
        /// </summary>
        private static GameObject ResolveDirectorObject(string contextBindingKey)
        {
            int separator = contextBindingKey.IndexOf('|');
            string directorId = separator > 0 ? contextBindingKey.Substring(0, separator) : contextBindingKey;
            if (!GlobalObjectId.TryParse(directorId, out GlobalObjectId id))
            {
                return null;
            }

            Component component = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Component;
            bool isSceneObject = component != null && component.gameObject.scene.IsValid();
            return isSceneObject ? component.gameObject : null;
        }

        private static void AddToHierarchy(GameObject gameObject, string eventId)
        {
            if (gameObject == null)
            {
                return;
            }

            Transform current = gameObject.transform;
            while (current != null)
            {
                AddEvent(_cache, HierarchyItemId.For(current.gameObject), eventId);
                current = current.parent;
            }

            AddEvent(_sceneCache, HierarchyItemId.SceneKey(gameObject.scene), eventId);
        }

        private static void AddEvent<TKey>(Dictionary<TKey, HashSet<string>> cache, TKey key, string eventId)
        {
            if (!cache.TryGetValue(key, out HashSet<string> events))
            {
                events = new HashSet<string>();
                cache.Add(key, events);
            }

            events.Add(eventId);
        }

        private static void OnHierarchyItemGUI(ItemId itemId, Rect selectionRect)
        {
            if (_cache.Count == 0)
            {
                return;
            }

            // Scene の見出し行（行の ID が読み込み済み Scene の handle を表す）
            ulong sceneKey = HierarchyItemId.SceneKey(itemId);
            if (_sceneCache.TryGetValue(sceneKey, out HashSet<string> sceneEvents) && TryGetLoadedSceneName(sceneKey, out string sceneName))
            {
                Rect sceneRect = new Rect(selectionRect.x, selectionRect.y, selectionRect.width - SceneMenuButtonWidth, selectionRect.height);
                DrawIndicator(sceneRect, sceneEvents, sceneName);
                return;
            }

            if (_cache.TryGetValue(itemId, out HashSet<string> events))
            {
                DrawIndicator(selectionRect, events, GetName(itemId));
            }
        }

        private static bool TryGetLoadedSceneName(ulong sceneKey, out string name)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (HierarchyItemId.SceneKey(scene) == sceneKey)
                {
                    name = scene.name;
                    return true;
                }
            }

            name = string.Empty;
            return false;
        }

        private static void DrawIndicator(Rect selectionRect, HashSet<string> events, string name)
        {
            if (_icon == null)
            {
                _icon = new GUIContent(EditorGUIUtility.IconContent("console.warnicon.sml").image);
            }

            string countText = events.Count.ToString();
            float countWidth = EditorStyles.miniLabel.CalcSize(new GUIContent(countText)).x;
            Rect iconRect = new Rect(selectionRect.xMax - IconSize - countWidth - 2, selectionRect.y, IconSize, selectionRect.height);
            Rect countRect = new Rect(iconRect.xMax, selectionRect.y, countWidth + 2, selectionRect.height);
            _icon.tooltip = Lang.T("views.hierarchy.tooltip", events.Count);

            // 右クリックはボタンより先に判定する（Hierarchy 自体のコンテキストメニューは出さない）
            Rect hitRect = new Rect(iconRect.x, iconRect.y, iconRect.width + countRect.width, iconRect.height);
            Event current = Event.current;
            bool isRightClick = current.type == EventType.ContextClick || (current.type == EventType.MouseDown && current.button == 1);
            if (isRightClick && hitRect.Contains(current.mousePosition))
            {
                ShowContextMenu(name, events);
                current.Use();
                return;
            }

            if (GUI.Button(iconRect, _icon, GUIStyle.none) || GUI.Button(countRect, countText, EditorStyles.miniLabel))
            {
                OnIconClicked(name, events);
            }
        }

        private static void OnIconClicked(string name, HashSet<string> events)
        {
            GuardianWindow.ShowNotificationsFor(events.ToList(), name);
        }

        /// <summary>
        /// 右クリックメニュー: この行に数えられている通知をその場で消す。
        /// 消すのはこの変化の通知だけで、また新しく外れたときは改めて通知される（承認・恒久的な無視ではない）。
        /// </summary>
        private static void ShowContextMenu(string name, HashSet<string> events)
        {
            List<string> eventIds = events.ToList();
            GenericMenu menu = new GenericMenu();
            menu.AddDisabledItem(new GUIContent(Lang.T("views.hierarchy.menu.header", eventIds.Count, name)));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent(Lang.T("views.hierarchy.menu.dismiss", eventIds.Count)), false, () => OnDismissSelected(eventIds));
            menu.AddItem(new GUIContent(Lang.T("views.hierarchy.menu.show")), false, () => GuardianWindow.ShowNotificationsFor(eventIds, name));
            menu.ShowAsContext();
        }

        private static void OnDismissSelected(List<string> eventIds)
        {
            if (!GuardianHistory.Shared.Dismiss(eventIds, out string error))
            {
                Debug.LogWarning($"[Timeline Binding Guardian] Notifications could not be dismissed: {error}");
            }
        }

        private static string GetName(ItemId itemId)
        {
            Object target = HierarchyItemId.ToObject(itemId);
            return target != null ? target.name : Lang.T("views.hierarchy.selectedObject");
        }
    }
}
