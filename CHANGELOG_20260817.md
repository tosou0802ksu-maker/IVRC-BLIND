# BLIND プログラム班 - 8月17日更新

## 📦 新機能（計6個スクリプト）

### ✅ CheckpointManager
- セーブポイント管理
- 赤ボタン = 地点1 / 青 = 地点2 / 緑 = 地点3
- **1人が罠に触れたら全員が現在地に復帰**

### ✅ ToggleDoor
- 赤/青/緑の「開くドア」「閉じるドア」
- ボタン到達時に自動で切り替わる
- 「赤に到達 → 赤閉じるドアが閉じて前に戻れなくなる」みたいな導線制御

### ✅ QuizManager + QuizChoiceButton
- クイズ部屋
- 過去の人だけが問題文を読める
- 不正解で床が抜けて落下 → セーブポイント復帰
- 再挑戦可能（完全一回きりにすると詰むため）

### ✅ PressurePlate + PressurePlateDoor
- 感圧板（複数枚対応）
- 2人（以上）が板に乗ると扉が開く
- 3人中2人が踏んでる間に残り1人が先へ進む動線

### ✅ 配置手順.md
- ワールド班向けInspector設定ガイド
- 各スクリプトのフィールド説明と値を全記載
- カリングマスク設定の参考表付き

---

## 🔧 既存スクリプト修正

### ⚠️ PlayerVisionController — 重大バグ修正
- **修正前**: シーンの `Camera.cullingMask` を書き換え
  → VRChat実機では何も起きない（機能しない）
- **修正後**: `VRCCameraSettings.ScreenCamera.CullingMask` を書き換え
  → VRChat実機で効く ✅
  + `OnVRCCameraSettingsChanged` で自動再適用

### 🔧 HazardZone
- CheckpointManager 連携追加
- 旧 GameManager はフォールバック化

### 🔧 ShuffleSequenceManager
- CheckpointManager との連携追加
- ボタン押下で「シャッフル＋セーブポイント登録＋扉開閉」が同時実行

---

## ⚙️ その他の設定

- **Memory レイヤー** 追加（index 24）
  - 22 = Thermal, 23 = Echo, 24 = Memory
- 全スクリプトのコンパイル確認済み
- 全 Program Asset の生成完了
- p1.unity は無編集（ワールド班の作業優先）

---

## 📋 未実装（今後）

- スプリンクラー（サーモ＋エコロケ同時無効化）
- 偽ボタン（ランプの色にない色。過去の人だけ見分ける）
- RemotePlayerProxyManager との球体/熱源の実装
- クイズの具体的な出題内容・オブジェクト配置

---

**コミット**: `aaf9b55` on main  
**配置ガイド**: `Assets/_BLIND/配置手順.md`
