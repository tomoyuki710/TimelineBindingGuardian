using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 通知の受入テスト（仕様 7.4 の例、13章 T01〜T05）。差分エンジン＋通知ルールを Unity Asset なしで検証する。
    /// 文脈は Scene(L0, 継承) → Child(L1, 定義) の 2 階層。
    /// </summary>
    public sealed class NotificationTests
    {
        private const string Scene = "scene";
        private int _counter;

        [Test]
        public void Spec74_AcceptanceScenario()
        {
            HistoryState state = new HistoryState("project");

            // 初回 A=Animator, B=AudioSource, C=NULL: 通知 0
            Assert.That(Step(state, Bound("A"), Bound("B"), NullOf("C")), Is.Empty);
            Assert.That(UnreadCount(state), Is.EqualTo(0));

            // A→NULL: 通知 1。Dismiss: 通知 0。再スキャン/再起動: 通知 0
            List<NotificationRecord> lostA = Step(state, NullOf("A"), Bound("B"), NullOf("C"));
            Assert.That(lostA.Count, Is.EqualTo(1));
            Assert.That(UnreadCount(state), Is.EqualTo(1));
            NotificationRules.Dismiss(state, lostA.Select(n => n.EventId), Now());
            Assert.That(UnreadCount(state), Is.EqualTo(0));
            Assert.That(Step(state, NullOf("A"), Bound("B"), NullOf("C")), Is.Empty);
            state = state.Clone();
            Assert.That(Step(state, NullOf("A"), Bound("B"), NullOf("C")), Is.Empty);
            Assert.That(UnreadCount(state), Is.EqualTo(0));

            // B→NULL: 通知 1。Dismiss: 通知 0
            List<NotificationRecord> lostB = Step(state, NullOf("A"), NullOf("B"), NullOf("C"));
            Assert.That(lostB.Count, Is.EqualTo(1));
            NotificationRules.Dismiss(state, lostB.Select(n => n.EventId), Now());
            Assert.That(UnreadCount(state), Is.EqualTo(0));

            // A→Animator: 通知 0（履歴のみ）
            Assert.That(Step(state, Bound("A"), NullOf("B"), NullOf("C")), Is.Empty);

            // A→NULL: 新規通知 1（以前と別の eventId）
            List<NotificationRecord> lostAgain = Step(state, NullOf("A"), NullOf("B"), NullOf("C"));
            Assert.That(lostAgain.Count, Is.EqualTo(1));
            Assert.That(lostAgain[0].EventId, Is.Not.EqualTo(lostA[0].EventId));

            // C=NULL のまま: C に関する通知は一度も出ていない
            Assert.That(state.Notifications.Select(n => FindEvent(state, n.EventId).Track.TrackPathHint), Has.No.Member("C"));

            // A が無効なスキャンで一時 Unknown: 最新の確実な値を保持し、誤通知なし
            Assert.That(Step(state, Uncertain(NullOf("A")), NullOf("B"), NullOf("C")), Is.Empty);
            Assert.That(Step(state, NullOf("A"), NullOf("B"), NullOf("C")), Is.Empty);

            // 通知を消去する前に別の Track（D）が NULL になった場合: 別イベントとして追加
            Step(state, NullOf("A"), NullOf("B"), NullOf("C"), Bound("D"));
            List<NotificationRecord> lostD = Step(state, NullOf("A"), NullOf("B"), NullOf("C"), NullOf("D"));
            Assert.That(lostD.Count, Is.EqualTo(1));
            Assert.That(UnreadCount(state), Is.EqualTo(2));
        }

        [Test]
        public void Spec74_SameMiddleChangeInTenContexts_OneNotificationTenAffected()
        {
            HistoryState state = new HistoryState("project");
            Step(state, Enumerable.Range(0, 10).Select(i => ThreeLevel($"d{i}", middleNull: false)).ToArray());

            List<NotificationRecord> created = Step(state, Enumerable.Range(0, 10).Select(i => ThreeLevel($"d{i}", middleNull: true)).ToArray());

            Assert.That(created.Count, Is.EqualTo(1));
            Assert.That(FindEvent(state, created[0].EventId).AffectedContexts.Count, Is.EqualTo(10));
        }

        [Test]
        public void T05_IntentionalNullOverride_IsNotifiedWithoutApproval()
        {
            HistoryState state = new HistoryState("project");
            Step(state, ThreeLevel("d1", middleNull: false));

            List<NotificationRecord> created = Step(state, ThreeLevel("d1", middleNull: true));

            Assert.That(created.Count, Is.EqualTo(1));
            Assert.That(created[0].Status, Is.EqualTo(NotificationStatus.Unread));
        }

        [Test]
        public void RecoveredNotification_StaysUntilDismissed_AndIsResolved()
        {
            HistoryState state = new HistoryState("project");
            Step(state, Bound("A"));
            NotificationRecord lost = Step(state, NullOf("A")).Single();

            Step(state, Bound("A"));

            ChangeEvent changeEvent = FindEvent(state, lost.EventId);
            Assert.That(lost.IsUnread, Is.True);
            Assert.That(NotificationRules.IsResolved(changeEvent, NotificationRules.CreateCurrentValueLookup(state)), Is.True);
        }

        [Test]
        public void Dismiss_DoesNotChangeHistoryOrCurrentState()
        {
            HistoryState state = new HistoryState("project");
            Step(state, Bound("A"));
            NotificationRecord lost = Step(state, NullOf("A")).Single();
            int eventCount = state.Events.Count;

            NotificationRules.Dismiss(state, new[] { lost.EventId }, Now());

            Assert.That(state.Events.Count, Is.EqualTo(eventCount));
            Assert.That(state.Contexts.Single().Effective.ValueState, Is.EqualTo(ValueState.Null));
            Assert.That(lost.Status, Is.EqualTo(NotificationStatus.Dismissed));
            Assert.That(Step(state, NullOf("A")), Is.Empty);
        }

        [Test]
        public void ShadowedLowerLevelLost_IsNotified_ButHasNoEffectOnContexts()
        {
            HistoryState state = new HistoryState("project");
            Step(state, ThreeLevel("d1", middleNull: false, sceneOverride: true));

            NotificationRecord created = Step(state, ThreeLevel("d1", middleNull: true, sceneOverride: true)).Single();

            Assert.That(FindEvent(state, created.EventId).AffectsEffective, Is.False);
        }

        private List<NotificationRecord> Step(HistoryState state, params ContextSnapshot[] contexts)
        {
            _counter++;
            ScopeObservation observation = new ScopeObservation($"obs{_counter}", Now(), "test", Scene, "Assets/Scene.unity", contexts.ToList());
            DiffResult result = BindingDiffEngine.Apply(state, observation);
            return NotificationRules.CreateForNewEvents(state, result.NewEvents, observation.ObservedAtUtc);
        }

        private string Now()
        {
            return $"2026-11-27T00:{_counter / 60:00}:{_counter % 60:00}Z";
        }

        private static int UnreadCount(HistoryState state)
        {
            return NotificationRules.GetUnread(state).Count;
        }

        private static ChangeEvent FindEvent(HistoryState state, string eventId)
        {
            return state.Events.Single(e => e.EventId == eventId);
        }

        private static ContextSnapshot Bound(string track)
        {
            return TwoLevel(track, new LevelValue(ValueState.Bound, LocalState.DefinedHere, $"target-{track}", $"Child/{track}Target", "Animator"));
        }

        private static ContextSnapshot NullOf(string track)
        {
            return TwoLevel(track, new LevelValue(ValueState.Null, LocalState.DefinedHere, string.Empty, string.Empty, string.Empty));
        }

        private static ContextSnapshot TwoLevel(string track, LevelValue value)
        {
            List<LevelSnapshot> levels = new List<LevelSnapshot>
            {
                Level($"scene|d1|tl|{track}", "scene", value, ContributionKind.Inherited),
                Level($"child|dir|tl|{track}", "child", value, ContributionKind.Defined),
            };
            return Context("d1", track, value, 1, levels);
        }

        /// <summary>
        /// Scene(L0) → Middle(L1) → Child(L2)。middleNull なら Middle が NULL Override、sceneOverride なら Scene が別対象へ Override。
        /// </summary>
        private static ContextSnapshot ThreeLevel(string director, bool middleNull, bool sceneOverride = false)
        {
            LevelValue childValue = new LevelValue(ValueState.Bound, LocalState.DefinedHere, "actTarget", "Child/ActTarget", "GameObject");
            LevelValue nullValue = new LevelValue(ValueState.Null, LocalState.ExplicitOverride, string.Empty, string.Empty, string.Empty);
            LevelValue sceneValue = new LevelValue(ValueState.Bound, LocalState.ExplicitOverride, "sceneTarget", "SceneTarget", "GameObject");
            LevelValue middleValue = middleNull ? nullValue : childValue;
            LevelValue effective = sceneOverride ? sceneValue : middleValue;
            List<LevelSnapshot> levels = new List<LevelSnapshot>
            {
                Level($"scene|{director}|tl|Activation", "scene", effective, sceneOverride ? ContributionKind.Defined : ContributionKind.Inherited),
                Level("middle|dir|tl|Activation", "middle", middleValue, middleNull ? ContributionKind.Defined : ContributionKind.Inherited),
                Level("child|dir|tl|Activation", "child", childValue, ContributionKind.Defined),
            };
            int origin = sceneOverride ? 0 : (middleNull ? 1 : 2);
            return Context(director, "Activation", effective, origin, levels);
        }

        private static ContextSnapshot Context(string director, string track, LevelValue effective, int origin, List<LevelSnapshot> levels)
        {
            DirectorLocator directorLocator = new DirectorLocator($"scene-{director}", "Director", "Director#0", 0);
            TrackLocator trackLocator = new TrackLocator("tl", "Assets/T.playable", track, "AnimationTrack", track, "Animator");
            return new ContextSnapshot($"scene-{director}|tl|{track}", Scene, "Assets/Scene.unity", directorLocator, trackLocator, effective, origin, false, levels);
        }

        private static ContextSnapshot Uncertain(ContextSnapshot context)
        {
            return new ContextSnapshot(context.BindingKey, context.ScopeGuid, context.ScopePathHint, context.Director, context.Track,
                context.Effective, -1, true, context.Levels.ToList());
        }

        private static LevelSnapshot Level(string key, string owner, LevelValue value, ContributionKind contribution)
        {
            return new LevelSnapshot(key, new AssetLocator(owner, $"Assets/{owner}", AssetKind.Prefab), new DirectorLocator(key, key, key, 0), value, contribution);
        }
    }
}
