# Timeline Binding Guardian — Phase 0 技術検証報告

- 実施日: 2026-11-27
- 判定: **条件付き GO**（読取り・階層特定・代表的な復元は安全に実施可能。一部ケースは読み取り専用）

## 1. 環境

| 項目 | 値 |
|---|---|
| Unity | 6000.3.27f2（`Application.unityVersion` で確認） |
| Timeline | com.unity.timeline 1.8.13（manifest.json / packages-lock.json で確認） |
| アセンブリ名 | `Unity.Timeline` / `Unity.Timeline.Editor` |
| シリアライズ | Force Text（検証プロジェクトの設定。製品は強制しない） |
| 実行方法 | Unity batchmode（`-executeMethod`）。各検証は別プロセスで実行 |

検証コードは開発プロジェクト側 `Assets/TBGValidation/Editor/` にあり、公開パッケージには含めない。

| スクリプト | 内容 |
|---|---|
| `Phase0AssetBuilder.cs` | 検証アセット生成（作成手順そのもの） |
| `Phase0ReadProbe.cs` | 読取り検証（4.2） |
| `Phase0MutationProbe.cs` | Track 追加/並べ替え/削除、参照先削除 |
| `Phase0RestoreProbe.cs` | 復元検証（4.3） |
| `Phase0IdentityProbe.cs` | 名前変更・兄弟並べ替え・非アクティブ化後の ID 対応 |
| `Phase0Runner.cs` | batchmode エントリ（`RunBuild/RunRead/RunMutation/RunRestore/RunIdentity`、引数 `-tbgRun`, `-tbgTag`） |

実測ログ: `Assets/TBGValidation/Results~/r2_*.txt`（build, read_run1, read_run2, mutation, restore, identity）と復元各段階の YAML コピー `restore_R*_*.prefab.txt`。

実行コマンド例:
```
"C:/Program Files/Unity/Hub/Editor/6000.3.27f2/Editor/Unity.exe" -batchmode -nographics -projectPath <project> -executeMethod TimelineBindingGuardian.Validation.Phase0Runner.RunRead -tbgRun r2 -tbgTag run1 -logFile <log>
```

## 2. 検証アセット構成

Timeline（`ChildTimeline.playable`、全 Director で共有）:
AnimA / AnimB（Animation）, Activation, Audio, Signal, Group（GroupTrack）> GroupedActivation, Control（ControlTrack + ControlPlayableAsset）

| アセット | 内容 |
|---|---|
| Child.prefab | `Child/Director`（PlayableDirector + SignalReceiver）, ActorA, ActorB（Animator）, Speaker（AudioSource×2、**2番目**にバインド）, ActTarget。全 Track バインド。Control は ExposedReference（`TBG_ControlSource`） |
| Middle.prefab | Child インスタンス×2（**同名 "Child"**）+ AltActor。Child#1 のみ Activation=**NULL Override**、AnimA=**AltActor Override** |
| Parent.prefab | MiddleP, MiddleQ。MiddleQ/Child#0 Audio=NULL Override、MiddleQ/Child#1 AnimB=**MiddleP/Child#0/ActorB（別 Prefab インスタンスへのバインド）** |
| ParentVariant.prefab | Parent の Variant。MiddleP/Child#0 AnimA=`SetGenericBinding(null)`、MiddleP/Child#1 Signal=`ClearGenericBinding` |
| SceneA.unity | Parent×2（同名）。2個目の MiddleP/Child#0 Activation=SceneOnlyTarget（**Scene 固有 Override**）、単独 SceneDirector（同じ Timeline、AnimA のみバインド） |
| SceneB.unity | Parent + ParentVariant |

Base（読取り）/ Mutation / Restore / Identity の4セットを同じ手順で独立に生成して使用した。

## 3. 読取りの結果（4.2）

### 3.1 m_SceneBindings の保存形式

- `SerializedObject.FindProperty("m_SceneBindings")` は存在し、`isArray=True`、`type=vector`、`arrayElementType=DirectorGenericBinding`。
- 各要素は `key`（`PPtr<Object>`、TrackAsset を参照）と `value`（`PPtr<Object>`、バインド先）の2フィールド。
- 配列は **Track の参照で引かれ、Timeline 上の Track の並び順とは独立**している。Track の追加や並べ替えでは、配列も Override の propertyPath も変化しなかった（M1, M2）。
- バインドの無い出力（ControlTrack: `outputTargetType=<none>`、GroupTrack: 出力なし）は配列に要素を持たない。ExposedReference は別フィールド `m_ExposedReferences.m_References` にあり、Generic Binding とは混同しない（v1 では対象外）。

Child.prefab の YAML（抜粋）:
```yaml
  m_SceneBindings:
  - key: {fileID: 5806659347274132503, guid: bffdbeb0..., type: 2}   # AnimA Track
    value: {fileID: 4163924261502414818}                           # ActorA Animator
  - key: {fileID: -217182147697794589, guid: bffdbeb0..., type: 2}   # Activation Track
    value: {fileID: 3129428623841298476}
```

### 3.2 階層ごとの Override の保存形式

Override は上位 Asset の `PrefabInstance.m_Modification` に、**配列インデックス付きの propertyPath** として保存される。

Middle.prefab（Child#1 の Override）:
```yaml
    - target: {fileID: 6527573874228968112, guid: <Child.prefab>, type: 3}   # Child の Director
      propertyPath: m_SceneBindings.Array.data[0].value                      # AnimA → AltActor
      objectReference: {fileID: 8974741206802905721}
    - target: {fileID: 6527573874228968112, guid: <Child.prefab>, type: 3}
      propertyPath: m_SceneBindings.Array.data[2].value                      # Activation → 明示 NULL
      objectReference: {fileID: 0}
```

- `target` は**1つ下の Source 階層**の Director（Parent では Middle.prefab 内の Director）。
- 明示的な NULL Override は `objectReference: {fileID: 0}` で表される。
- Override されるのは `.value` だけで、`key` は Override されない（`SetGenericBinding` で既存キーの値を変えた場合）。

### 3.3 ClearGenericBinding の保存形式（重要）

ParentVariant での `ClearGenericBinding(Signal)`:
```yaml
      propertyPath: m_SceneBindings.Array.size      value: 5
      propertyPath: m_SceneBindings.Array.data[4].key    objectReference: GroupedActivation Track
      propertyPath: m_SceneBindings.Array.data[4].value  objectReference: ActTarget
```
- 要素を削除して後ろを詰めるため、**無関係な Track（GroupedActivation）の要素にも key/value の Override が発生する**。このとき実効値は変わらないが `prefabOverride=True` になる。
- 判定: `NoEntry`（配列に要素が無い）と `Null`（要素があり value が NULL）は、配列を key で検索すれば区別できる。
- 差分の正規化では、**値が Source と同じなら prefabOverride=True でも実効値の変化として扱わない**。構造的な変化（LocalOverrideChanged）としてのみ記録する。

### 3.4 有効値と変更階層の特定

採用した手順:
1. 対象 Asset を `AssetDatabase.LoadAssetAtPath<GameObject>` で直接ロードする（`LoadPrefabContents` は使わない）。`GetComponentsInChildren<PlayableDirector>(true)` で非アクティブなものも含めて列挙する。
2. `TimelineAsset.outputs` から `outputTargetType != null` の出力だけを対象にし、`GetGenericBinding(sourceObject)` で有効値を取る。
3. `PrefabUtility.GetCorrespondingObjectFromSource` を1段ずつ辿り、各階層の Director で有効値と、その要素の `value.prefabOverride` を取る。
4. 上位階層の値を `GetCorrespondingObjectFromSource(value)` で下位階層へ正規化し、下位階層の値と比べる。一致すれば継承、不一致なら「その階層で変更」とする。正規化先が無い場合は、その文脈で定義された対象（AltActor や Scene 固有のオブジェクト）として扱う。

実測結果（Base、抜粋）:

| Director（Parent 文脈） | Track | Child | Middle | Parent | 特定された変更階層 |
|---|---|---|---|---|---|
| MiddleP/Child#1 | Activation | ActTarget | NULL（Override=True） | NULL（Override=False） | **Middle** |
| MiddleP/Child#1 | AnimA | ActorA | AltActor（Override=True） | AltActor（継承） | **Middle** |
| MiddleQ/Child#0 | Audio | Speaker | Speaker（継承） | NULL（Override=True） | **Parent** |
| MiddleQ/Child#1 | AnimB | ActorB | ActorB（継承） | MiddleP/Child#0/ActorB（Override=True） | **Parent**（別インスタンス参照） |
| Variant: MiddleP/Child#1 | Signal | Receiver | Receiver | Receiver | **Variant**（NoEntry） |
| SceneA 2個目: MiddleP/Child#0 | Activation | ActTarget | ActTarget | ActTarget | **Scene**（SceneOnlyTarget） |

- 同名 Child の2インスタンス、同じ型の AudioSource 2個（2番目へのバインド）、同名 Parent の2インスタンス（Scene）を、取り違えずに識別できた。
- **GO 条件「少なくとも3階層の同一 Director/Track を誤対応なく識別し、変更階層・継承/Override を特定」を満たす**（Child→Middle→Parent→Variant/Scene の4〜5階層）。

### 3.5 Override 取得 API の比較

| API | 結果・注意点 |
|---|---|
| `SerializedProperty.prefabOverride`（要素の `value`） | 階層ごとに「この階層で Override されているか」が正確に取れる。**主判定に採用**。 |
| `PrefabUtility.GetPropertyModifications(obj)` | **最も外側の PrefabInstance の全 Modification** を返す（兄弟 Director の分も含む）。`mod.target == GetCorrespondingObjectFromSource(director)` で絞り込む必要がある。propertyPath はインデックスなので、Source 側の配列の key と対応付けて解釈する。証拠（overrideEvidence）として採用。 |
| `PrefabUtility.GetObjectOverrides(root, true)` | Director 単位で「Override あり」が分かるだけで、Track 単位の情報は無い。補助用。 |

### 3.6 ID の安定性

| 対象 | GlobalObjectId の形式 | 安定性 |
|---|---|---|
| Prefab Asset 内の Director（直接ロード） | `V1-1-<assetGuid>-<fileId>-0`。ネストした Prefab の要素は、その Asset 内の stripped fileId になる | 別プロセス2回（run1/run2）で**完全一致**。名前変更・兄弟並べ替え・非アクティブ化の後も一致（Identity 6/6 PASS） |
| Track | `V1-3-<timelineGuid>-<fileId>-0`（GlobalObjectId は fileId を **unsigned** で表示し、`TryGetGUIDAndLocalFileIdentifier` は **signed long** で返す。例: 18229561926011757027 と -217182147697794589） | 安定。保存時は signed 64bit の文字列に統一する |
| Scene 内の Director | `V1-2-<sceneGuid>-<sourceFileId>-<prefabInstanceId>` | 安定。同じ Parent の2インスタンスは prefabInstanceId で区別できる |
| `LoadPrefabContents` で開いた一時 Scene | `V1-2-...`（プレビュー Scene） | **Asset 直接ロード時の ID と一致しない**。永続 ID には使わない（仕様の注意どおり） |

instanceID はプロセスごとに変わる（run1/run2 の差分は instanceID だけだった）。

### 3.7 読取りによる副作用

- Prefab Asset の直接ロード、`SerializedObject` での読取り、`LoadPrefabContents`/`UnloadPrefabContents`、Scene のオープンのいずれでも、**ファイルのハッシュは不変**（`AssetDatabase.SaveAssets()` の後でも変化なし）。`EditorUtility.IsDirty`=false、`scene.isDirty`=false（7/7 PASS）。
- Inspector の生成、Timeline ウィンドウを開く、Evaluate は行っていない。

### 3.8 制御された異常状態（Mutation）

| 操作 | 観測結果 | 状態の分類 |
|---|---|---|
| Track 追加（AnimNew） | 配列には要素が追加されない。既存の要素と propertyPath は不変 | 新しい Track は NoEntry（TrackAdded。通知しない） |
| Track 並べ替え | 配列も propertyPath も不変 | 変化なし |
| Track 削除（Audio） | 配列の要素は残り、**key が NULL（storedInstanceID≠0）**になる。上位の Override（data[3].value）も残る | 古いキー。TrackRemoved（BindingLost にしない） |
| 同じファイル内のバインド先を削除（Child の ActorB） | Child の value は保存時に `{fileID: 0}` へ変わる | Child の階層で Bound→Null（BindingLost） |
| 下位 Prefab の対象を削除したことで、上位 Override の参照先が無くなる（Parent の別インスタンス参照） | YAML には**非0の fileID（stripped Animator）が残る**が、`SerializedProperty` と `PropertyModification.objectReference` はどちらも NULL（instanceID=0）を返す | **公開 API では明示 NULL と区別できない**ため MissingReference と判定できない。Null として扱う |

## 4. 復元の結果（4.3、Restore セット）

すべて `PrefabUtility.EditPrefabContentsScope(assetPath)` 内で、**変更が実際に入っている階層の Asset**に対して実施した。各ステップで Child/Middle/Parent/Variant のバインド表と Modification 一覧を、前後で照合した。

| ID | 内容 | 方法 | 結果 |
|---|---|---|---|
| R1 | Parent の NULL Override → 継承 | 要素の `value` プロパティに `RevertPropertyOverride(prop, AutomatedAction)` | PASS。変化は Parent の1行と Variant の1行（継承による伝播）のみ。YAML の差分は対象 Modification の1ブロック（4行）の削除だけ |
| R2a | Middle の NULL Override → 継承 | 同上 | PASS。Middle の1行と、その伝播（Parent 2行、Variant 2行）のみ |
| R2b | 継承 → 明示 NULL Override を復元 | `SetGenericBinding(track, null)` + `RecordPrefabInstancePropertyModifications` | PASS。状態とファイルが R2a の前と**完全一致**（YAML もバイト単位で同じ） |
| R3a/b | 別対象の Override を変更してから元に戻す | `SerializedProperty.objectReferenceValue` + `ApplyModifiedPropertiesWithoutUndo` | PASS。状態とファイルが R3a の前と完全一致 |
| R5 | NoEntry（ClearGenericBinding）→ 継承 | `m_SceneBindings` 配列全体に `RevertPropertyOverride` | Signal は継承値に戻り、要素がずれたことによる GroupedActivation の見かけ上の Override も消えた（実効値の変化は Signal のみ）。ただし**その Director の、その階層にある全バインド Override が戻る**ため、最小操作ではない |
| R4 | Scene 固有 Override → 継承、その後 Undo | `RevertPropertyOverride(prop, UserAction)`、`Undo.PerformUndo()` | 値は継承に戻り、Undo で元の Override に戻った。兄弟インスタンスは不変。ファイルは不変（未保存）。**ただし `scene.isDirty` は立たなかった** |

R1〜R3 の後、他の Track・兄弟インスタンス・他の Override・fileID・GUID に意図しない変化は無かった。

## 5. 状態分類（実装で使う判定規則）

対象は `outputTargetType != null` の出力のみ。

| ValueState | 判定 |
|---|---|
| Bound | `key == track` の要素があり、value が解決できる |
| Null | 要素があり value が NULL（stored instanceID=0） |
| MissingReference | 要素があり value が NULL で、stored instanceID≠0（**公開 API で証拠が得られた場合だけ**） |
| NoEntry | `key == track` の要素が無い |
| Unknown | 同じ key の要素が複数ある、ロード失敗、例外など |
| NotApplicable | バインド対象型の無い出力（Control/Group）、Track が削除済み |

| LocalState | 判定 |
|---|---|
| DefinedHere | Prefab インスタンスではない Director（Child、Scene の単独 Director） |
| ExplicitOverride | 要素の `value.prefabOverride=True` で、正規化した値が Source と異なる |
| Inherited | `value.prefabOverride=False`、または値が Source と同じ（ずれによる見かけ上の Override。構造変化として記録） |
| Unknown | NoEntry で `Array.size` が Override されている場合など、原因が配列の構造変化にある場合 |

stale key（`key` が NULL で instanceID≠0）は、削除された Track の残骸として扱い、通知の対象にしない。

## 6. 採用する方法

- **読取り**: Asset の直接ロード + `SerializedObject`（読み取りのみ）+ `GetGenericBinding` + `GetCorrespondingObjectFromSource` を1段ずつ辿る + `GlobalObjectId.GetGlobalObjectIdSlow`。
- **永続キー**: Director は Asset 直接ロード時の GlobalObjectId、Track は Timeline の GUID + fileId（signed 64bit 文字列）。表示用には「名前#兄弟インデックス」のパスを併記する。
- **継承に戻す復元**: `EditPrefabContentsScope` + 該当要素の `value` に `RevertPropertyOverride`。
- **Override 値（NULL を含む）の復元**: `EditPrefabContentsScope` + `SetGenericBinding` + `RecordPrefabInstancePropertyModifications`、または `SerializedProperty` での設定（どちらも検証済み。製品では片方に統一する）。
- **Scene の復元**: `RevertPropertyOverride(UserAction)` など Undo 対応の API を使い、**`EditorSceneManager.MarkSceneDirty` を明示的に呼ぶ**。保存はユーザーに任せる。
- YAML の直接書き換えは行わない。YAML は検証時の観察にだけ使う。

## 7. 未対応・制約・未検証

| 項目 | 扱い |
|---|---|
| NoEntry（ClearGenericBinding）からの復元 | 配列全体の Revert でしか戻せない。その Director のその階層に他のバインド Override が無い場合だけ許可するか、**v1 では読み取り専用**（該当 Prefab を開く案内）にする |
| 下位 Prefab の対象削除による上位参照の消失 | 公開 API では MissingReference と判定できず、Null として通知する。旧参照先は履歴から表示し、自動復元はしない（参照先が存在しないため） |
| Prefab Asset 保存の Undo | `EditPrefabContentsScope` は即保存し、Undo の対象外。**復元前のバックアップと、別途の戻す操作**が必要（Phase 4 で設計） |
| `RevertPropertyOverride` での Scene の dirty | 自動では立たない。明示的に `MarkSceneDirty` を呼ぶ |
| GUI の Unity Editor 再起動後の ID | batchmode の別プロセスで代用して確認。GUI での再起動は未検証 |
| Inspector を開いたときに配列が同期される可能性 | 未検証（検査では Inspector を生成しない） |
| ユーザーが開いている Scene がある状態で、閉じた Scene を Additive で読むこと | 未検証（batchmode では Single で開いた）。Phase 1/3 で確認する |
| 監視イベント API（ObjectChangeEvents、AssetPostprocessor など） | Phase 0 の目的（A/B）の範囲外。Phase 3 で確認する |
| Packages 配下・読み取り専用・モデル Prefab の編集不可判定 | 未検証（Phase 4） |
| 大量の Director での負荷 | 未検証（Phase 5） |

## 8. 判定

**条件付き GO**。

- GO 条件のうち、「3階層以上で同じ Director/Track を誤対応なく識別」「有効値と変更階層、継承/Override の特定」「代表的な NULL Override から元の継承・値への復元」「兄弟インスタンスと他 Track への副作用なし」「読取りで既存 Asset が変わらない」は、すべて実測で満たした。
- 条件: NoEntry（ClearGenericBinding 由来）の復元と、MissingReference の判定は、上記の制約に従って v1 では読み取り専用または Null 扱いとする。Prefab Asset への復元は Undo 不可のため、バックアップ設計を Phase 4 の前提とする。
