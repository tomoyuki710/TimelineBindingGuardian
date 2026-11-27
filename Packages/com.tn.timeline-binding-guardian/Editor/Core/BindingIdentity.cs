using System.Globalization;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// 永続キーの生成。Hierarchy パス・名前・配列インデックス・InstanceID は使わない。
    /// </summary>
    public static class BindingIdentity
    {
        private const char Separator = '|';

        /// <summary>
        /// Director（文脈込みの GlobalObjectId）+ Timeline GUID + Track fileId で BindingKey を作る。
        /// </summary>
        public static string CreateBindingKey(string directorGlobalObjectId, string timelineGuid, string trackFileId)
        {
            return string.Concat(directorGlobalObjectId, Separator, timelineGuid, Separator, trackFileId);
        }

        /// <summary>
        /// Owner 階層のキー: その階層を所有する Asset/Scene の GUID + その階層の Director の GlobalObjectId + Timeline GUID + Track fileId。
        /// 同じ Middle.prefab 内の同じ Director の同じ Track は、どの Scene から観測しても同じキーになる。
        /// </summary>
        public static string CreateOwnerLevelKey(string ownerGuid, string directorGlobalObjectId, string timelineGuid, string trackFileId)
        {
            return string.Concat(ownerGuid, Separator, directorGlobalObjectId, Separator, timelineGuid, Separator, trackFileId);
        }

        /// <summary>
        /// fileId を符号付き 64bit の文字列にする。GlobalObjectId は unsigned 表示のため、保存時はこちらに統一する。
        /// </summary>
        public static string FormatFileId(long fileId)
        {
            return fileId.ToString(CultureInfo.InvariantCulture);
        }
    }
}
