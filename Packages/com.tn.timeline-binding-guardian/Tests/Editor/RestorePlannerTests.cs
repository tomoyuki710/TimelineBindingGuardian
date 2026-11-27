using System.Linq;
using NUnit.Framework;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 復元の判定のテスト（仕様 10 章、13章 T16〜T19 の判定部分）。
    /// </summary>
    public sealed class RestorePlannerTests
    {
        [Test]
        public void T16_InheritedBefore_RevertIsRecommended_ExplicitSetIsAlternative()
        {
            RestoreInput input = Input(Lost(Inherited("actTarget")), current: NullOverride(), inherited: Bound("actTarget"));

            RestorePlan plan = RestorePlanner.Create(input);

            Assert.That(plan.CanRestore, Is.True);
            Assert.That(RestorePlanner.GetRecommended(plan).Operation, Is.EqualTo(RestoreOperation.RevertToInherited));
            Assert.That(plan.Options.Select(o => o.Operation), Is.EquivalentTo(new[] { RestoreOperation.RevertToInherited, RestoreOperation.SetValue }));
            Assert.That(plan.Warnings, Is.Empty);
        }

        [Test]
        public void T18_InheritedValueChangedSinceThen_Warns()
        {
            RestoreInput input = Input(Lost(Inherited("actTarget")), current: NullOverride(), inherited: Bound("otherTarget"));
            input.InheritedMatchesBefore = false;

            RestorePlan plan = RestorePlanner.Create(input);

            Assert.That(plan.CanRestore, Is.True);
            Assert.That(plan.Warnings.Single(), Does.Contain("inherited value has changed"));
        }

        [Test]
        public void T17_ExplicitOverrideBefore_SetValue()
        {
            ChangeEvent changeEvent = Event(ChangeType.BindingLost, Explicit("altActor"), NullValue());
            RestoreInput input = Input(changeEvent, current: NullOverride(), inherited: Bound("actorA"));

            RestorePlan plan = RestorePlanner.Create(input);

            RestoreOption option = plan.Options.Single();
            Assert.That(option.Operation, Is.EqualTo(RestoreOperation.SetValue));
            Assert.That(option.ResultValue.TargetGlobalObjectId, Is.EqualTo("altActor"));
        }

        [Test]
        public void T17_ExplicitNullBefore_SetNull()
        {
            LevelValue explicitNull = new LevelValue(ValueState.Null, LocalState.ExplicitOverride, string.Empty, string.Empty, string.Empty);
            ChangeEvent changeEvent = Event(ChangeType.BindingAssigned, explicitNull, Inherited("actTarget"));
            RestoreInput input = Input(changeEvent, current: Inherited("actTarget"), inherited: Bound("actTarget"), currentOverridden: false);

            RestorePlan plan = RestorePlanner.Create(input);

            RestoreOption option = plan.Options.Single();
            Assert.That(option.Operation, Is.EqualTo(RestoreOperation.SetValue));
            Assert.That(option.ResultValue.ValueState, Is.EqualTo(ValueState.Null));
        }

        [Test]
        public void DefinitionLevel_SetValue()
        {
            LevelValue defined = new LevelValue(ValueState.Bound, LocalState.DefinedHere, "actorA", "Child/ActorA", "Animator");
            ChangeEvent changeEvent = Event(ChangeType.BindingLost, defined, NullValue());
            RestoreInput input = Input(changeEvent, current: NullValue(), inherited: LevelValue.None, isInstance: false, currentOverridden: false);

            RestorePlan plan = RestorePlanner.Create(input);

            Assert.That(plan.Options.Single().Operation, Is.EqualTo(RestoreOperation.SetValue));
        }

        [Test]
        public void T19_UndeterminedOrigin_IsRefused()
        {
            ChangeEvent changeEvent = new ChangeEvent("id", "obs", "t", ChangeType.BindingLost, "key", Owner(), Director(), Track(),
                Inherited("actTarget"), NullValue(), true, string.Empty);

            RestorePlan plan = RestorePlanner.Create(Input(changeEvent, current: NullOverride(), inherited: Bound("actTarget")));

            Assert.That(plan.CanRestore, Is.False);
            Assert.That(plan.Refusals, Has.Some.Contains("undetermined"));
        }

        [Test]
        public void T19_MissingTarget_IsRefused()
        {
            ChangeEvent changeEvent = Event(ChangeType.BindingLost, Explicit("deleted"), NullValue());
            RestoreInput input = Input(changeEvent, current: NullOverride(), inherited: Bound("actorA"));
            input.TargetResolvable = false;

            RestorePlan plan = RestorePlanner.Create(input);

            Assert.That(plan.CanRestore, Is.False);
            Assert.That(plan.Refusals.Single(), Does.Contain("can no longer be found"));
        }

        [Test]
        public void T19_NoEntryBeforeOrNow_IsRefused()
        {
            LevelValue noEntry = new LevelValue(ValueState.NoEntry, LocalState.ExplicitOverride, string.Empty, string.Empty, string.Empty);
            RestorePlan beforeNoEntry = RestorePlanner.Create(Input(Event(ChangeType.BindingAssigned, noEntry, Bound("x")), current: Bound("x"), inherited: Bound("x")));

            RestoreInput entryMissing = Input(Lost(Inherited("actTarget")), current: NullOverride(), inherited: Bound("actTarget"));
            entryMissing.CurrentEntryExists = false;
            RestorePlan nowNoEntry = RestorePlanner.Create(entryMissing);

            Assert.That(beforeNoEntry.CanRestore, Is.False);
            Assert.That(nowNoEntry.CanRestore, Is.False);
            Assert.That(nowNoEntry.Refusals.Single(), Does.Contain("does not exist"));
        }

        [Test]
        public void T19_ReadOnlyAndConflict_AreRefused()
        {
            RestoreInput input = Input(Lost(Inherited("actTarget")), current: NullOverride(), inherited: Bound("actTarget"));
            input.ReadOnlyReason = "The prefab is in a read-only package.";
            input.ConflictReason = "The prefab is open in Prefab Mode with unsaved changes.";

            RestorePlan plan = RestorePlanner.Create(input);

            Assert.That(plan.Refusals.Count, Is.EqualTo(2));
            Assert.That(plan.CanRestore, Is.False);
        }

        [Test]
        public void AlreadyInEarlierState_IsRefused()
        {
            ChangeEvent changeEvent = Event(ChangeType.TargetChanged, Explicit("altActor"), Explicit("other"));
            RestoreInput input = Input(changeEvent, current: Explicit("altActor"), inherited: Bound("actorA"));

            RestorePlan plan = RestorePlanner.Create(input);

            Assert.That(plan.CanRestore, Is.False);
            Assert.That(plan.Refusals.Single(), Does.Contain("already"));
        }

        [Test]
        public void InheritedBefore_AndStillInheritedWithSameValue_IsAlreadyRestored()
        {
            RestoreInput input = Input(Lost(Inherited("actTarget")), current: Inherited("actTarget"), inherited: Bound("actTarget"), currentOverridden: false);

            RestorePlan plan = RestorePlanner.Create(input);

            Assert.That(plan.CanRestore, Is.False);
            Assert.That(plan.Refusals.Single(), Does.Contain("already"));
            Assert.That(plan.IsAlreadyInEarlierState, Is.True);
        }

        [Test]
        public void InheritedBefore_StillInheritedButSourceChanged_OffersExplicitSet()
        {
            RestoreInput input = Input(Lost(Inherited("actTarget")), current: Inherited("other"), inherited: Bound("other"), currentOverridden: false);
            input.InheritedMatchesBefore = false;

            RestorePlan plan = RestorePlanner.Create(input);

            Assert.That(plan.Options.Single().Operation, Is.EqualTo(RestoreOperation.SetValue));
        }

        [Test]
        public void RemoveOverride_OverriddenNull_OffersRevertOnly()
        {
            RestoreInput input = Input(Lost(Inherited("x")), current: NullOverride(), inherited: Bound("actTarget"));

            RestorePlan plan = RestorePlanner.CreateRemoveOverride(input);

            Assert.That(plan.Options.Single().Operation, Is.EqualTo(RestoreOperation.RevertToInherited));
        }

        [Test]
        public void RemoveOverride_NotOverriddenOrDefinitionLevel_IsRefused()
        {
            RestorePlan notOverridden = RestorePlanner.CreateRemoveOverride(
                Input(Lost(Inherited("x")), current: Inherited("x"), inherited: Bound("x"), currentOverridden: false));
            RestorePlan definition = RestorePlanner.CreateRemoveOverride(
                Input(Lost(Inherited("x")), current: Bound("x"), inherited: LevelValue.None, isInstance: false, currentOverridden: false));

            Assert.That(notOverridden.CanRestore, Is.False);
            Assert.That(notOverridden.IsAlreadyInEarlierState, Is.True);
            Assert.That(definition.CanRestore, Is.False);
            Assert.That(definition.Refusals.Single(), Does.Contain("not a prefab instance"));
        }

        [Test]
        public void TrackRemovedEvent_IsRefused()
        {
            RestorePlan plan = RestorePlanner.Create(Input(Event(ChangeType.TrackRemoved, Bound("x"), LevelValue.None), current: Bound("x"), inherited: Bound("x")));

            Assert.That(plan.CanRestore, Is.False);
        }

        private static RestoreInput Input(ChangeEvent changeEvent, LevelValue current, LevelValue inherited, bool isInstance = true, bool currentOverridden = true)
        {
            return new RestoreInput
            {
                Event = changeEvent,
                LevelIsInstance = isInstance,
                CurrentEntryExists = true,
                CurrentValue = current,
                CurrentValueOverridden = currentOverridden,
                InheritedValue = inherited,
                TargetResolvable = true,
            };
        }

        private static ChangeEvent Lost(LevelValue before)
        {
            return Event(ChangeType.BindingLost, before, NullValue());
        }

        private static ChangeEvent Event(ChangeType type, LevelValue before, LevelValue after)
        {
            return new ChangeEvent("id", "obs", "2026-11-27T00:00:00Z", type, "middle|dir|tl|Activation", Owner(), Director(), Track(), before, after, false, string.Empty);
        }

        private static LevelValue Inherited(string target)
        {
            return new LevelValue(ValueState.Bound, LocalState.Inherited, target, $"Middle/Child/{target}", "GameObject");
        }

        private static LevelValue Explicit(string target)
        {
            return new LevelValue(ValueState.Bound, LocalState.ExplicitOverride, target, $"Middle/{target}", "Animator");
        }

        private static LevelValue Bound(string target)
        {
            return new LevelValue(ValueState.Bound, LocalState.DefinedHere, target, $"Child/{target}", "GameObject");
        }

        private static LevelValue NullValue()
        {
            return new LevelValue(ValueState.Null, LocalState.ExplicitOverride, string.Empty, string.Empty, string.Empty);
        }

        private static LevelValue NullOverride()
        {
            return NullValue();
        }

        private static AssetLocator Owner()
        {
            return new AssetLocator("middle", "Assets/Middle.prefab", AssetKind.Prefab);
        }

        private static DirectorLocator Director()
        {
            return new DirectorLocator("dir", "Middle/Child/Director", "Middle#0/Child#1/Director#0", 0);
        }

        private static TrackLocator Track()
        {
            return new TrackLocator("tl", "Assets/T.playable", "1", "ActivationTrack", "Activation", "GameObject");
        }
    }
}
