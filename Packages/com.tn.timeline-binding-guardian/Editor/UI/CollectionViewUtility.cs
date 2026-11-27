using UnityEngine.UIElements;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 一覧（ListView）の共通設定。TreeView は空のとき何も表示しないため対象外。
    /// </summary>
    internal static class CollectionViewUtility
    {
        /// <summary>
        /// 空のときに Unity 標準の英語「List is empty」ではなく、表示言語の文言を出す。
        /// </summary>
        public static void UseLocalizedEmptyText(BaseListView view)
        {
            view.makeNoneElement = CreateEmptyLabel;
        }

        private static VisualElement CreateEmptyLabel()
        {
            Label label = new Label(Lang.T("common.listEmpty"));
            label.style.paddingLeft = 6;
            label.style.paddingTop = 2;
            return label;
        }
    }
}
