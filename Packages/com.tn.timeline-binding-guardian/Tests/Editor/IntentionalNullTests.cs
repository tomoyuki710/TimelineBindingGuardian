using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Playables;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 意図的な NULL の共有リスト（ユーザーの判断で仕様 8 章の方針から追加した機能）。
    /// 共有ファイルはテスト用の場所（Temp）に差し替える。素材の変更は元の内容に書き戻す。
    /// </summary>
    public sealed class IntentionalNullTests
    {
        private string _directory;
        private string _middlePath;
        private byte[] _middleOriginal;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            TestFixtureBuilder.BuildMutable();
            _middlePath = TestFixtureBuilder.PathIn(TestFixtureBuilder.MutableFolderName, "Middle.prefab");
        }

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetFullPath("Temp"), "TBGIntentionalTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            IntentionalNullRegistry.FilePathOverride = Path.Combine(_directory, IntentionalNullRegistry.FileName);
            IntentionalNullRegistry.CheckForChanges();
            _middleOriginal = File.ReadAllBytes(_middlePath);
        }

        [TearDown]
        public void TearDown()
        {
            IntentionalNullRegistry.FilePathOverride = null;
            IntentionalNullRegistry.CheckForChanges();
            if (!File.ReadAllBytes(_middlePath).SequenceEqual(_middleOriginal))
            {
                File.WriteAllBytes(_middlePath, _middleOriginal);
                AssetDatabase.ImportAsset(_middlePath, ImportAssetOptions.ForceUpdate);
            }
        }

        [Test]
        public void Notification_IsNotCreatedForIntentionalLevel_ButIsForOthers()
        {
            HistoryState state = new HistoryState("project");
            ChangeEvent intentional = Lost("event-1", "owner-a|director|timeline|1");
            ChangeEvent other = Lost("event-2", "owner-b|director|timeline|1");

            List<NotificationRecord> created = NotificationRules.CreateForNewEvents(state, new[] { intentional, other }, "now",
                key => key == "owner-a|director|timeline|1");

            Assert.That(created.Select(n => n.EventId), Is.EqualTo(new[] { "event-2" }));
        }

        [Test]
        public void Json_IsTheSameRegardlessOfOrder_AndRoundTrips()
        {
            IntentionalNullList first = new IntentionalNullList();
            first.Add(Entry("b-key"));
            first.Add(Entry("a-key"));
            IntentionalNullList second = new IntentionalNullList();
            second.Add(Entry("a-key"));
            second.Add(Entry("b-key"));

            Assert.That(first.ToJson(), Is.EqualTo(second.ToJson()));
            Assert.That(IntentionalNullList.TryParse(first.ToJson(), out IntentionalNullList parsed, out string error), Is.True, error);
            Assert.That(parsed.Entries.Select(e => e.OwnerLevelKey), Is.EqualTo(new[] { "a-key", "b-key" }));
            Assert.That(parsed.Entries[0].Reason, Is.EqualTo("reason a-key"));
        }

        [Test]
        public void BrokenOrForeignFile_IsRejected()
        {
            string json = new IntentionalNullList().ToJson();
            string conflicted = "<<<<<<< HEAD\n" + json + "=======\n" + json + ">>>>>>> branch\n";

            Assert.That(IntentionalNullList.TryParse(conflicted, out _, out string conflictError), Is.False);
            Assert.That(conflictError, Is.Not.Empty);
            Assert.That(IntentionalNullList.TryParse("{\"format\":\"Other\"}", out _, out _), Is.False);
            Assert.That(IntentionalNullList.TryParse(json.Replace("\"formatVersion\": 1", "\"formatVersion\": 99"), out _, out _), Is.False);
        }

        [Test]
        public void Registry_SavesToTheSharedFile_AndReadsExternalChanges()
        {
            Assert.That(IntentionalNullRegistry.TryAdd(Entry("a-key"), out string error), Is.True, error);
            Assert.That(IntentionalNullRegistry.Contains("a-key"), Is.True);
            Assert.That(File.ReadAllText(IntentionalNullRegistry.FilePath), Does.Contain("a-key"));

            // Pull 相当: 外から別の内容に書き換わる
            IntentionalNullList pulled = new IntentionalNullList();
            pulled.Add(Entry("b-key"));
            File.WriteAllText(IntentionalNullRegistry.FilePath, pulled.ToJson());
            File.SetLastWriteTimeUtc(IntentionalNullRegistry.FilePath, DateTime.UtcNow.AddSeconds(5));
            IntentionalNullRegistry.CheckForChanges();

            Assert.That(IntentionalNullRegistry.Contains("a-key"), Is.False);
            Assert.That(IntentionalNullRegistry.Contains("b-key"), Is.True);
            Assert.That(IntentionalNullRegistry.TryRemove("b-key", out error), Is.True, error);
            Assert.That(IntentionalNullRegistry.Entries, Is.Empty);
        }

        [Test]
        public void BrokenSharedFile_DoesNotSuppress_AndIsNotOverwritten()
        {
            string broken = "<<<<<<< HEAD\n{}\n=======\n{}\n>>>>>>> branch\n";
            File.WriteAllText(IntentionalNullRegistry.FilePath, broken);
            IntentionalNullRegistry.CheckForChanges();

            Assert.That(IntentionalNullRegistry.Contains("a-key"), Is.False);
            Assert.That(IntentionalNullRegistry.Error, Is.Not.Empty);
            Assert.That(IntentionalNullRegistry.TryAdd(Entry("a-key"), out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(File.ReadAllText(IntentionalNullRegistry.FilePath), Is.EqualTo(broken));
        }

        [Test]
        public void RegisteredLevel_HidesExistingNotification_AndIsNotNotifiedAgain()
        {
            GuardianHistory history = CreateHistory();
            history.Record(new[] { new SceneBindingScanner().ScanPrefabAsset(_middlePath) }, "baseline");

            SetActivation(false);
            RecordSummary lost = Record(history);
            Assert.That(lost.NewNotifications.Count, Is.EqualTo(1));
            ChangeEvent lostEvent = lost.NewEvents.Single(e => e.ChangeType == ChangeType.BindingLost);
            Assert.That(history.GetNotifications(false).Count, Is.EqualTo(1));

            Assert.That(IntentionalNullRegistry.TryAdd(Entry(lostEvent.OwnerLevelKey), out string error), Is.True, error);

            Assert.That(history.GetNotifications(false), Is.Empty, "the existing notification is hidden");
            Assert.That(history.IsIntentional(lostEvent.OwnerLevelKey), Is.True);

            // Bound に戻して再び NULL にしても通知しない（登録がある限り）
            SetActivation(true);
            Record(history);
            SetActivation(false);
            RecordSummary again = Record(history);
            Assert.That(again.NewEvents.Any(e => e.ChangeType == ChangeType.BindingLost), Is.True, "the change is still recorded in the history");
            Assert.That(again.NewNotifications, Is.Empty);

            // 登録を外すと、未消去の通知は再び表示される
            Assert.That(IntentionalNullRegistry.TryRemove(lostEvent.OwnerLevelKey, out error), Is.True, error);
            Assert.That(history.GetNotifications(false).Count, Is.EqualTo(1));
        }

        private GuardianHistory CreateHistory()
        {
            HistoryStore store = new HistoryStore(Path.Combine(_directory, "history"), "test-project");
            store.Load();
            return new GuardianHistory(store, null, IntentionalNullRegistry.Contains);
        }

        private RecordSummary Record(GuardianHistory history)
        {
            RecordSummary summary = history.Record(new[] { new SceneBindingScanner().ScanPrefabAsset(_middlePath) }, "test");
            Assert.That(summary.Saved, Is.True, summary.Error);
            return summary;
        }

        private void SetActivation(bool bound)
        {
            using (PrefabUtility.EditPrefabContentsScope scope = new PrefabUtility.EditPrefabContentsScope(_middlePath))
            {
                PlayableDirector director = scope.prefabContentsRoot.transform.GetChild(0).GetComponentInChildren<PlayableDirector>(true);
                string timelinePath = AssetDatabase.GetAssetPath(director.playableAsset);
                UnityEngine.Timeline.TrackAsset track = TestFixtureBuilder.FindTrack(timelinePath, TestFixtureBuilder.TrackActivation);
                if (bound)
                {
                    // Child から継承した値に戻す（Override を外す）
                    using (SerializedObject serializedObject = new SerializedObject(director))
                    {
                        SerializedProperty array = serializedObject.FindProperty("m_SceneBindings");
                        for (int i = 0; i < array.arraySize; i++)
                        {
                            SerializedProperty element = array.GetArrayElementAtIndex(i);
                            if (element.FindPropertyRelative("key").objectReferenceValue == track)
                            {
                                PrefabUtility.RevertPropertyOverride(element.FindPropertyRelative("value"), InteractionMode.AutomatedAction);
                            }
                        }
                    }

                    return;
                }

                director.SetGenericBinding(track, null);
                PrefabUtility.RecordPrefabInstancePropertyModifications(director);
            }
        }

        private static ChangeEvent Lost(string eventId, string ownerLevelKey)
        {
            LevelValue bound = new LevelValue(ValueState.Bound, LocalState.DefinedHere, "target", "target", "GameObject");
            LevelValue lost = new LevelValue(ValueState.Null, LocalState.ExplicitOverride, string.Empty, string.Empty, string.Empty);
            return new ChangeEvent(eventId, "observation", "now", ChangeType.BindingLost, ownerLevelKey,
                new AssetLocator("owner", "Assets/Owner.prefab", AssetKind.Prefab),
                new DirectorLocator("director", "Director", "Director#0", 0),
                new TrackLocator("timeline", "Assets/Timeline.playable", "1", "ActivationTrack", "Track", "GameObject"),
                bound, lost, false, string.Empty);
        }

        private static IntentionalNullEntry Entry(string key)
        {
            return new IntentionalNullEntry(key, "Assets/Owner.prefab", "Director", "Assets/Timeline.playable", "Track", "reason " + key, "2026-11-27T00:00:00Z");
        }
    }
}
