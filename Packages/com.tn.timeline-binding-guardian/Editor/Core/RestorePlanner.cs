using System.Collections.Generic;
using System.Linq;

namespace TimelineBindingGuardian
{
    public enum RestoreOperation
    {
        // その階層の value の Override だけを除去し、下位階層から継承した値に戻す
        RevertToInherited,
        // その階層に値（参照または NULL）を明示的に設定する
        SetValue,
    }

    /// <summary>
    /// 復元の判定に必要な現在の状態。Unity 側（BindingRestoreService）が集める。
    /// </summary>
    public sealed class RestoreInput
    {
        public ChangeEvent Event { get; set; }
        // その階層の Director が Prefab インスタンスの一部（継承元がある）
        public bool LevelIsInstance { get; set; }
        // その階層の m_SceneBindings に、この Track の要素が今あるか
        public bool CurrentEntryExists { get; set; }
        public LevelValue CurrentValue { get; set; } = LevelValue.None;
        public bool CurrentValueOverridden { get; set; }
        // 継承に戻した場合の値（1 つ下の階層の現在値。表示用）。インスタンスでなければ None
        public LevelValue InheritedValue { get; set; } = LevelValue.None;
        // 継承に戻した場合の値が、変更前の値と同じ対象か（階層の違いを読み替えて比べた結果）
        public bool InheritedMatchesBefore { get; set; } = true;
        // 変更前の参照先を今も解決できるか（型も一致）
        public bool TargetResolvable { get; set; }
        // 書き込めない理由（空なら書き込める）
        public string ReadOnlyReason { get; set; } = string.Empty;
        // 未保存の Prefab Mode など、競合の理由（空なら競合なし）
        public string ConflictReason { get; set; } = string.Empty;
    }

    public sealed class RestoreOption
    {
        public RestoreOption(RestoreOperation operation, LevelValue resultValue, string description, bool isRecommended)
        {
            Operation = operation;
            ResultValue = resultValue;
            Description = description;
            IsRecommended = isRecommended;
        }

        public RestoreOperation Operation { get; }
        // 実行後にその階層で得られる値
        public LevelValue ResultValue { get; }
        public string Description { get; }
        public bool IsRecommended { get; }
    }

    public sealed class RestorePlan
    {
        public List<RestoreOption> Options { get; } = new List<RestoreOption>();
        // 復元できない理由（1 つでもあれば実行しない。Prefab を開く案内に留める）
        public List<string> Refusals { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool CanRestore => Refusals.Count == 0 && Options.Count > 0;
        // 既に変更前と同じ状態（戻す必要が無い）
        public bool IsAlreadyInEarlierState { get; set; }
    }

    /// <summary>
    /// 復元の可否と方法を決める（仕様 10 章）。Unity API に依存しない。
    /// - 戻す先は「そのイベントの変更前の状態」、変更するのはそのイベントの Owner 階層だけ。
    /// - 変更前が継承なら「継承に戻す」（推奨）と「変更前の値を明示的に設定」を区別して示す。継承元が変わっていれば警告。
    /// - Phase 0 で安全性を確認していない操作（配列要素の追加・削除が必要なもの）は行わない。
    /// </summary>
    public static class RestorePlanner
    {
        private static readonly HashSet<ChangeType> RestorableTypes = new HashSet<ChangeType>
        {
            ChangeType.BindingLost,
            ChangeType.TargetChanged,
            ChangeType.BindingAssigned,
            ChangeType.LocalOverrideChanged,
        };

        public static RestorePlan Create(RestoreInput input)
        {
            RestorePlan plan = new RestorePlan();
            AddRefusals(input, plan);
            if (plan.Refusals.Count > 0)
            {
                return plan;
            }

            LevelValue before = input.Event.Before;
            bool beforeWasInherited = before.LocalState == LocalState.Inherited && input.LevelIsInstance;
            if (beforeWasInherited)
            {
                AddInheritedOptions(input, plan);
            }
            else
            {
                AddSetValueOption(input, plan, true);
            }

            if (plan.Options.Count == 0 && plan.Refusals.Count == 0)
            {
                plan.Refusals.Add(Lang.T("restore.refusal.alreadyEarlierState"));
                plan.IsAlreadyInEarlierState = true;
            }

            return plan;
        }

        private static void AddRefusals(RestoreInput input, RestorePlan plan)
        {
            ChangeEvent changeEvent = input.Event;
            if (changeEvent.OriginUndetermined)
            {
                plan.Refusals.Add(Lang.T("restore.refusal.undetermined"));
            }

            if (!RestorableTypes.Contains(changeEvent.ChangeType))
            {
                plan.Refusals.Add(Lang.T("restore.refusal.unsupportedType", changeEvent.ChangeType));
            }

            ValueState beforeState = changeEvent.Before.ValueState;
            bool isRestorableValue = beforeState == ValueState.Bound || beforeState == ValueState.Null;
            if (!isRestorableValue)
            {
                plan.Refusals.Add(Lang.T("restore.refusal.unsupportedBeforeState", beforeState));
            }

            if (!string.IsNullOrEmpty(input.ReadOnlyReason))
            {
                plan.Refusals.Add(input.ReadOnlyReason);
            }

            if (!string.IsNullOrEmpty(input.ConflictReason))
            {
                plan.Refusals.Add(input.ConflictReason);
            }

            if (!input.CurrentEntryExists)
            {
                plan.Refusals.Add(Lang.T("restore.refusal.entryMissingRestore"));
            }
        }

        private static void AddInheritedOptions(RestoreInput input, RestorePlan plan)
        {
            LevelValue before = input.Event.Before;
            bool alreadyInherited = !input.CurrentValueOverridden && input.InheritedMatchesBefore;
            if (alreadyInherited)
            {
                // 今も継承していて、継承した値も変更前と同じ（既に変更前の状態）
                return;
            }

            if (input.CurrentValueOverridden)
            {
                plan.Options.Add(new RestoreOption(RestoreOperation.RevertToInherited, input.InheritedValue,
                    Lang.T("restore.option.revertToBefore"), true));
                if (!input.InheritedMatchesBefore)
                {
                    plan.Warnings.Add(Lang.T("restore.warning.inheritedChanged", Describe(input.InheritedValue), Describe(before)));
                }
            }

            AddSetValueOption(input, plan, false);
        }

        private static void AddSetValueOption(RestoreInput input, RestorePlan plan, bool isRecommended)
        {
            LevelValue before = input.Event.Before;
            bool needsTarget = before.ValueState == ValueState.Bound;
            if (needsTarget && !input.TargetResolvable)
            {
                if (isRecommended)
                {
                    plan.Refusals.Add(Lang.T("restore.refusal.targetUnresolvable", before.TargetHint));
                }

                return;
            }

            bool alreadySame = input.CurrentValue.HasSameValue(before);
            bool alreadyExplicit = input.CurrentValueOverridden || !input.LevelIsInstance;
            if (alreadySame && alreadyExplicit)
            {
                return;
            }

            string description = before.ValueState == ValueState.Null
                ? Lang.T("restore.option.setNone")
                : Lang.T("restore.option.setTarget", before.TargetHint);
            plan.Options.Add(new RestoreOption(RestoreOperation.SetValue, before, description, isRecommended || plan.Options.Count == 0));
        }

        private static string Describe(LevelValue value)
        {
            return value.ValueState == ValueState.Bound ? value.TargetHint : value.ValueState.ToString();
        }

        /// <summary>
        /// 履歴のイベントによらず、現在その階層にある Override を外して継承に戻す（例: 監視を入れる前からある NULL の Override）。
        /// 操作は Phase 0 で検証済みの「要素の value の Override 除去」だけ。
        /// </summary>
        public static RestorePlan CreateRemoveOverride(RestoreInput input)
        {
            RestorePlan plan = new RestorePlan();
            if (!string.IsNullOrEmpty(input.ReadOnlyReason))
            {
                plan.Refusals.Add(input.ReadOnlyReason);
            }

            if (!string.IsNullOrEmpty(input.ConflictReason))
            {
                plan.Refusals.Add(input.ConflictReason);
            }

            if (!input.LevelIsInstance)
            {
                plan.Refusals.Add(Lang.T("restore.refusal.notInstance"));
            }

            if (!input.CurrentEntryExists)
            {
                plan.Refusals.Add(Lang.T("restore.refusal.entryMissingRemove"));
            }
            else if (!input.CurrentValueOverridden && plan.Refusals.Count == 0)
            {
                plan.Refusals.Add(Lang.T("restore.refusal.notOverridden"));
                plan.IsAlreadyInEarlierState = true;
            }

            if (plan.Refusals.Count == 0)
            {
                plan.Options.Add(new RestoreOption(RestoreOperation.RevertToInherited, input.InheritedValue,
                    Lang.T("restore.option.revertToSource"), true));
            }

            return plan;
        }

        public static RestoreOption GetRecommended(RestorePlan plan)
        {
            return plan.Options.FirstOrDefault(o => o.IsRecommended) ?? plan.Options.FirstOrDefault();
        }
    }
}
